using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Data.SQLite;
using VisionQC.LocalAgent;
using VisionQC.LocalAgent.Services;
using VisionQC.LocalAgent.Persistence;

class IncrementalImportTests
{
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static readonly List<PositionAliasDefinition> Defs=new List<PositionAliasDefinition>{
        new PositionAliasDefinition{key="CA_TOP",name="TCA",aliases=new List<string>{"CA(TOP)"}},
        new PositionAliasDefinition{key="CA_BOT",name="BCA",aliases=new List<string>{"CA(BOT)"}},
        new PositionAliasDefinition{key="AN_TOP",name="TAN",aliases=new List<string>{"AN(TOP)"}},
        new PositionAliasDefinition{key="AN_BOT",name="BAN",aliases=new List<string>{"AN(BOT)"}}};
    static readonly AnalysisProjection.ThresholdValue[] Thresholds={new AnalysisProjection.ThresholdValue{position="TCA",tool="Crack",value=.95}};
    static readonly AnalysisProjection.ActualNgValue[] Actual={new AnalysisProjection.ActualNgValue{day="2026-02-03",cell="000001",position="TCA",path="C:\\actual.jpg"}};
    const string Header="Date,Time,Cell ID,Position,Total_result,FullPath,WorkspaceType,WorkspaceName,WorkspaceKey,Crack_result,Crack_score,Edge_result,Edge_score,Foil_result,Foil_score,Weld_result,Weld_score\r\n";
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static Dictionary<string,object> Obj(object value){return (Dictionary<string,object>)Json.DeserializeObject(Json.Serialize(value));}
    static Dictionary<string,object> Finish(AnalysisService service,object value,bool success=true){var status=Obj(value);while(Convert.ToBoolean(status["running"])){Thread.Sleep(10);status=Obj(service.Status(Json.Serialize(new{analysisId=status["analysisId"]})));}if(success)Check(Convert.ToBoolean(status["completed"]),Json.Serialize(status));return status;}
    static object Options(){return new AgentHistoryFileImportRequest{positionDefinitions=Defs};}
    static object Bindings(string[] paths,string[] positions){return paths.Select((path,i)=>new CsvHistoryFileImporter.FileAssignment{path=path,position=positions[i]}).ToArray();}
    static Dictionary<string,object> Import(AnalysisService service,string[] paths,string[] positions,string id=null){return Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position=id==null?null:positions[0],filePaths=paths,fileAssignments=Bindings(paths,positions),options=Options()})));}
    static Dictionary<string,object> Dashboard(AnalysisService service,string id){return Obj(Finish(service,service.Dashboard(Json.Serialize(new{analysisId=id,positionDefinitions=Defs})))["result"]);}
    static void Settings(AnalysisService service,string id){Finish(service,service.Thresholds(Json.Serialize(new{analysisId=id,thresholds=Thresholds})));string batch=Guid.NewGuid().ToString("N");foreach(string action in new[]{"begin","append","commit"})service.ActualNg(Json.Serialize(new{analysisId=id,batchId=batch,action,actualNg=action=="append"?Actual:null}));}
    static AnalysisProjection Projection(AnalysisService service){var job=typeof(AnalysisService).GetField("_job",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(service);return (AnalysisProjection)job.GetType().GetField("Projection",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(job);}
    static long Scalar(string path,string sql){using(var c=new SQLiteConnection("Data Source="+path+";Version=3;")){c.Open();using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return Convert.ToInt64(cmd.ExecuteScalar());}}}
    static void Compare(AnalysisService actual,string id,string root,string[] paths,string[] positions){using(var expected=new AnalysisService(Path.Combine(root,Guid.NewGuid().ToString("N")))){var imported=Import(expected,paths,positions);string expectedId=(string)imported["analysisId"];Settings(expected,expectedId);var actualModel=Dashboard(actual,id);var expectedModel=Dashboard(expected,expectedId);actualModel.Remove("cursor");expectedModel.Remove("cursor");string a=Json.Serialize(actualModel),b=Json.Serialize(expectedModel);if(a!=b){File.WriteAllText(Path.Combine(root,"actual.json"),a);File.WriteAllText(Path.Combine(root,"expected.json"),b);}Check(a==b,"Incremental dashboard differs from a clean complete rebuild");}}
    static string Row(string day,string cell,string result="NG",string score="0.9000987654321"){return day+",12:00:01,"+cell+",WRONG,"+result+",C:\\Exact\\"+cell+".jpg,green,Workspace,ws,"+result+","+score+",OK,.1,OK,.2,OK,.3\r\n";}
    static void Main(string[] args){try{string root=args[0];Directory.CreateDirectory(root);Correctness(root);RenamedPosition(root);AuditTransitions(root);ImportReceipts(root);RepeatedTransitions(root);MissingWorkspaceMetadata(root);int cells=int.Parse(args[1]);if(cells>0)Benchmark(Path.Combine(root,"benchmark"),cells);}catch(Exception error){Console.WriteLine(error);Environment.Exit(1);}}
    static void Correctness(string root)
    {
        string a=Path.Combine(root,"a.csv"),b=Path.Combine(root,"b.csv"),replacement=Path.Combine(root,"replacement.csv"),empty=Path.Combine(root,"empty.csv"),broken=Path.Combine(root,"broken.csv");
        File.WriteAllText(a,Header+Row("2026-02-03","000001")+Row("2026-02-03","000001","OK",".99")+Row("2026-02-04","000001")+Row("2026-02-03","000002","OK",".01"));
        File.WriteAllText(b,Header+Row("2026-02-03","000001")+Row("2026-02-05","000003","OK",".2"));
        string exact=Path.Combine(root,"exact-original.jpg");File.WriteAllText(exact,"synthetic original bytes");
        File.WriteAllText(replacement,(Header+Row("2026-02-03","000001","NG",".9600123456789")+Row("2026-02-06","000004")).Replace("C:\\Exact\\000001.jpg",exact));File.WriteAllText(empty,Header);File.WriteAllText(broken,Header+Row("2026-02-03","bad")+"bad,row\r\n");
        string home=Path.Combine(root,"analysis"),historyPath=Path.Combine(root,"history.sqlite"),id;
        using(var service=new AnalysisService(home))using(var history=new SqliteRunStore(historyPath))
        {
            id=(string)Import(service,new[]{a},new[]{"TCA"})["analysisId"];Settings(service,id);
            Finish(service,service.SaveHistory(Json.Serialize(new{analysisId=id}),history));
            // An unavailable old original must not be reopened when adding another Position.
            File.Move(a,a+".held");var added=Import(service,new[]{b},new[]{"BCA"},id);Check(Convert.ToInt64(added["imported"])==2,"Unchanged files were imported again");File.Move(a+".held",a);
            Compare(service,id,root,new[]{a,b},new[]{"TCA","BCA"});
            var detail=Obj(service.Detail(Json.Serialize(new{analysisId=id,day="2026-02-03",cellId="000001",position="TCA"})));
            var record=Obj(((object[])Obj(detail["page"])["records"])[0]);Check(Convert.ToString(record["fullPath"])=="C:\\Exact\\000001.jpg","Original path changed");
            Check(Convert.ToDouble(Obj(((object[])record["tools"])[0])["score"])==.9000987654321,"Score precision changed");
            Finish(service,service.SaveHistory(Json.Serialize(new{analysisId=id}),history));Finish(service,service.SaveHistory(Json.Serialize(new{analysisId=id}),history));
            Check(Scalar(historyPath,"SELECT COUNT(*) FROM runs")==2&&Scalar(historyPath,"SELECT COUNT(*) FROM images")==10,"Saving revised analysis lost data or duplicated retries");
            // Stage validation fails before any committed Position changes.
            var failed=Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position="TCA",filePaths=new[]{broken},fileAssignments=Bindings(new[]{broken},new[]{"TCA"}),options=Options()})),false);
            Check(!(bool)failed["completed"]&&!string.IsNullOrEmpty((string)failed["error"]),"Malformed CSV accepted");Compare(service,id,root,new[]{a,b},new[]{"TCA","BCA"});
            // Cancel precisely after raw/fact writes and before commit, not just before starting.
            Projection(service).Timing=(phase,ms)=>{if(phase=="position-import-before-commit")service.Cancel(Json.Serialize(new{analysisId=id}));};
            failed=Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position="TCA",filePaths=new[]{replacement},fileAssignments=Bindings(new[]{replacement},new[]{"TCA"}),options=Options()})),false);Check(!(bool)failed["completed"],"Cancelled merge committed");
            Projection(service).Timing=null;Compare(service,id,root,new[]{a,b},new[]{"TCA","BCA"});
            File.Move(b,b+".held");Import(service,new[]{replacement},new[]{"TCA"},id);File.Move(b+".held",b);Compare(service,id,root,new[]{replacement,b},new[]{"TCA","BCA"});
            Directory.CreateDirectory(Path.Combine(root,"export"));var export=Obj(service.Export(Json.Serialize(new{analysisId=id,independent=true,kind="tool-ng",position="TCA",tool="Crack",outputDirectory=Path.Combine(root,"export")})));
            // Independent export has a separate status endpoint.
            while((bool)export["running"]){Thread.Sleep(10);export=Obj(service.ExportStatus(Json.Serialize(new{analysisId=id,exportId=export["exportId"]})));}Check((bool)export["completed"],"Replaced Tool NG export failed: "+Json.Serialize(export));
            Check(Convert.ToInt64(Obj(export["result"])["count"])==1,"Threshold-filtered export differs after replacement");
            // Copying has released its DB snapshot: another Position revision must proceed,
            // while the in-flight export retains its already selected exact source and bytes.
            using(var copying=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())
            {
                service.ExportCopyStarting=exportId=>{copying.Set();release.Wait();};
                export=Obj(service.Export(Json.Serialize(new{analysisId=id,independent=true,kind="tool-ng",position="TCA",tool="Crack",copyImages=true,outputDirectory=Path.Combine(root,"export")})));
                Check(copying.Wait(10000),"Copy stage did not begin");
                try{Import(service,new[]{empty},new[]{"TCA"},id);Compare(service,id,root,new[]{empty,b},new[]{"TCA","BCA"});}
                finally{release.Set();}
                while((bool)export["running"]){Thread.Sleep(10);export=Obj(service.ExportStatus(Json.Serialize(new{analysisId=id,exportId=export["exportId"]})));}
                Check((bool)export["completed"]&&Convert.ToInt64(Obj(export["result"])["count"])==1&&Convert.ToInt64(Obj(Obj(export["result"])["images"])["copied"])==1,"Position update changed an in-flight export");
                Check(File.ReadAllText(Path.Combine(root,"export","TCA","Crack","exact-original.jpg"))==File.ReadAllText(exact),"Copy changed source bytes");service.ExportCopyStarting=null;
            }
            Import(service,new[]{replacement},new[]{"TCA"},id);
            Import(service,new[]{b},new[]{"TAN"},id);Compare(service,id,root,new[]{replacement,b,b},new[]{"TCA","BCA","TAN"});
            Import(service,new[]{empty},new[]{"TCA"},id);Compare(service,id,root,new[]{empty,b,b},new[]{"TCA","BCA","TAN"});
            Check(!Directory.EnumerateDirectories(Path.Combine(home,id),"import-*").Any(),"Staging files were retained");
        }
        using(var restored=new AnalysisService(home)){Finish(restored,restored.Status(Json.Serialize(new{analysisId=id})));Compare(restored,id,root,new[]{empty,b,b},new[]{"TCA","BCA","TAN"});}
        // Simulate a split attached-WAL commit; reopening must discard inconsistent facts.
        string source=Path.Combine(home,id,"source.sqlite"),cache=Path.Combine(home,id,"projection.sqlite");Scalar(cache,"UPDATE analysis_revision SET revision='interrupted';SELECT 1");
        using(var restored=new AnalysisService(home)){Finish(restored,restored.Status(Json.Serialize(new{analysisId=id})));Compare(restored,id,root,new[]{empty,b,b},new[]{"TCA","BCA","TAN"});}
        Check(Scalar(source,"SELECT COUNT(*) FROM tool_results t LEFT JOIN images i ON i.image_id=t.image_id WHERE i.image_id IS NULL")==0,"Replacement left orphaned tool results");
        Check(Scalar(source,"SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check<>'ok'")==0&&Scalar(cache,"SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check<>'ok'")==0,"SQLite integrity check failed");
        Console.WriteLine("PASS incremental import: unavailable prior files, explicit Position/aliases, duplicates and dates, exact paths/scores, thresholds/actual NG, malformed/cancel rollback, empty replacement, same file in multiple Positions, independent export, revised history retry, restart and interrupted-WAL recovery.");
    }
    static void RenamedPosition(string root)
    {
        string home=Path.Combine(root,"renamed"),a=Path.Combine(root,"a.csv"),b=Path.Combine(root,"b.csv"),replacement=Path.Combine(root,"replacement.csv");
        var definitions=new List<PositionAliasDefinition>{new PositionAliasDefinition{key="CA_TOP",name="TopNew",aliases=new List<string>{"TCA","CA(TOP)"}},Defs[1]};
        using(var service=new AnalysisService(home))using(var history=new SqliteRunStore(Path.Combine(root,"renamed-history.sqlite")))
        {
            string id=(string)Import(service,new[]{a,b},new[]{"TCA","BCA"})["analysisId"];
            Finish(service,service.Dashboard(Json.Serialize(new{analysisId=id,positionDefinitions=definitions})));
            Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position="TopNew",filePaths=new[]{replacement},fileAssignments=Bindings(new[]{replacement},new[]{"TopNew"}),options=new AgentHistoryFileImportRequest{positionDefinitions=definitions}})));
            var model=Obj(Finish(service,service.Dashboard(Json.Serialize(new{analysisId=id,positionDefinitions=definitions})))["result"]);
            string source=Path.Combine(home,id,"source.sqlite");
            Check(Convert.ToInt64(model["rawRowCount"])==4&&Scalar(source,"SELECT COUNT(*) FROM images")==4&&Scalar(source,"SELECT COUNT(*) FROM images WHERE position_key='TCA'")==0,"Renamed Position retained stale raw rows");
            Finish(service,service.SaveHistory(Json.Serialize(new{analysisId=id}),history));Check(Scalar(history.DatabasePath,"SELECT COUNT(*) FROM images")==4,"Renamed Position history count differs");
        }
        Console.WriteLine("PASS renamed Position replacement removes old alias-bound raw rows and preserves other Position/history.");
    }
    static void AuditTransitions(string root)
    {
        var errors=new List<string>();
        string a=Path.Combine(root,"a.csv"),b=Path.Combine(root,"b.csv"),replacement=Path.Combine(root,"replacement.csv"),broken=Path.Combine(root,"broken.csv");
        string home=Path.Combine(root,"audit");
        using(var service=new AnalysisService(home))
        {
            string id=(string)Import(service,new[]{a,b},new[]{"TCA","BCA"})["analysisId"];
            string source=Path.Combine(home,id,"source.sqlite");
            Scalar(source,"DROP INDEX idx_images_position_alias;SELECT 1");
            var update=Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position="TCA",filePaths=new[]{replacement},fileAssignments=Bindings(new[]{replacement},new[]{"TCA"}),options=Options()})),false);
            if(!(bool)update["completed"])errors.Add("Older analysis without Position index cannot accept replacement: "+update["error"]);
            Dashboard(service,id);
            Finish(service,service.StartImport(Json.Serialize(new{analysisId=id,position="TCA",filePaths=new[]{broken},fileAssignments=Bindings(new[]{broken},new[]{"TCA"}),options=Options()})),false);
            var recovery=Finish(service,service.Status(Json.Serialize(new{analysisId=id,action="restore"})),false);
            if(!(bool)recovery["completed"])errors.Add("Refresh cannot recover committed data after a failed/cancelled update");
            var locked=new FileStream(Path.Combine(home,id,"import-request.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
            var release=new Thread(()=>{Thread.Sleep(60);locked.Dispose();});release.Start();
            try{Import(service,new[]{b},new[]{"BCA"},id);}finally{release.Join();locked.Dispose();}
        }
        using(var service=new AnalysisService(Path.Combine(root,"remove-audit")))
        {
            string id=(string)Import(service,new[]{a,b},new[]{"TCA","BCA"})["analysisId"];
            var definitions=new List<PositionAliasDefinition>{new PositionAliasDefinition{key="CA_TOP",name="TopNew",aliases=new List<string>{"TCA","CA(TOP)"}},Defs[1]};
            Finish(service,service.Dashboard(Json.Serialize(new{analysisId=id,positionDefinitions=definitions})));
            var removed=Finish(service,service.RemovePosition(Json.Serialize(new{analysisId=id,position="TopNew"})));
            string next=(string)removed["analysisId"];
            var model=Obj(Finish(service,service.Dashboard(Json.Serialize(new{analysisId=next,positionDefinitions=definitions})))["result"]);
            if(Convert.ToInt64(model["rawRowCount"])!=2)errors.Add("Removing renamed Position retains alias-bound observations");
            string source=Path.Combine(root,"remove-audit",next,"source.sqlite");
            if(Scalar(source,"SELECT COUNT(*) FROM runs WHERE source_type IN ('csv-file-stream','csv-import','simulation')")!=1)errors.Add("Result after Position removal is saved with a history-hidden source type");
        }
        using(var store=new SqliteRunStore(Path.Combine(root,"live-original.sqlite")))
        using(var service=new AnalysisService(Path.Combine(root,"live-removal")))
        {
            string run;new CsvHistoryFileImporter(store,Json).ImportFiles(new AgentHistoryFileImportRequest{positionDefinitions=Defs},new[]{a,b},null,CancellationToken.None,out run,null,null,(CsvHistoryFileImporter.FileAssignment[])Bindings(new[]{a,b},new[]{"TCA","BCA"}));
            string id=(string)Finish(service,service.BindSource(store.DatabasePath,run))["analysisId"];
            var definitions=new List<PositionAliasDefinition>{new PositionAliasDefinition{key="CA_TOP",name="TopNew",aliases=new List<string>{"TCA"}},Defs[1]};
            Finish(service,service.Dashboard(Json.Serialize(new{analysisId=id,positionDefinitions=definitions})));
            Finish(service,service.Thresholds(Json.Serialize(new{analysisId=id,thresholds=new[]{new AnalysisProjection.ThresholdValue{position="BCA",tool="Crack",value=.95}}})));
            string batch=Guid.NewGuid().ToString("N");foreach(string action in new[]{"begin","append","commit"})service.ActualNg(Json.Serialize(new{analysisId=id,batchId=batch,action,actualNg=action=="append"?new[]{new AnalysisProjection.ActualNgValue{position="BCA",cell="000001",day="2026-02-03",path="fixture"}}:null}));
            var removed=Finish(service,service.RemovePosition(Json.Serialize(new{analysisId=id,position="TopNew"})));
            var model=Obj(Finish(service,service.Dashboard(Json.Serialize(new{analysisId=removed["analysisId"],positionDefinitions=definitions})))["result"]);
            if(Convert.ToInt64(model["rawRowCount"])!=2||Convert.ToInt64(model["ngCellCount"])!=0||Convert.ToInt64(model["missCount"])!=1)errors.Add("Live subset removal lost aliases, thresholds or actual NG");
            if(Scalar(store.DatabasePath,"SELECT COUNT(*) FROM images")!=6)errors.Add("Live subset removal modified original run data");
        }
        foreach(string error in errors)Console.WriteLine("AUDIT FAILURE: "+error);
        Check(errors.Count==0,"Extended audit found "+errors.Count+" failures");
        Console.WriteLine("PASS older cache migration, failed-update restore, renamed Position removal and saved-history visibility.");
    }
    static void ImportReceipts(string root)
    {
        string home=Path.Combine(root,"receipts"),a=Path.Combine(root,"a.csv"),b=Path.Combine(root,"b.csv"),bad=Path.Combine(root,"broken.csv");
        string first=Guid.NewGuid().ToString("N"),second=Guid.NewGuid().ToString("N"),failed=Guid.NewGuid().ToString("N");
        string start=Json.Serialize(new{importRequestId=first,filePaths=new[]{a},fileAssignments=Bindings(new[]{a},new[]{"TCA"}),options=Options()});
        string add=Json.Serialize(new{importRequestId=second,analysisId=first,position="BCA",filePaths=new[]{b},fileAssignments=Bindings(new[]{b},new[]{"BCA"}),options=Options()});
        using(var service=new AnalysisService(home))
        {
            var done=Finish(service,service.StartImport(start));Check((string)done["analysisId"]==first&&(bool)done["importCommitted"],"Initial receipt is missing");
            File.Move(a,a+".held");try{done=Finish(service,service.StartImport(start));Check((bool)done["importCommitted"],"Same request reopened the CSV");}finally{File.Move(a+".held",a);}
            var pending=service.StartImport(add);var repeated=service.StartImport(add);Finish(service,pending);done=Finish(service,repeated);
            Check((string)done["importRequestId"]==second&&(bool)done["importCommitted"]&&Convert.ToInt64(Dashboard(service,first)["rawRowCount"])==6,"Repeated request duplicated or lost rows");
        }
        using(var restored=new AnalysisService(home))
        {
            var receipt=Finish(restored,restored.Status(Json.Serialize(new{analysisId=first})));
            Check((string)receipt["importRequestId"]==second&&(bool)receipt["importCommitted"]&&Convert.ToInt64(receipt["imported"])==2,"Receipt did not survive restart");
            File.Move(b,b+".held");try{Finish(restored,restored.StartImport(add));}finally{File.Move(b+".held",b);}
            Finish(restored,restored.StartImport(Json.Serialize(new{analysisId=first,importRequestId=failed,position="TCA",filePaths=new[]{bad},fileAssignments=Bindings(new[]{bad},new[]{"TCA"}),options=Options()})),false);
        }
        using(var restored=new AnalysisService(home))
        {
            var receipt=Finish(restored,restored.Status(Json.Serialize(new{analysisId=first,action="restore"})));
            Check((string)receipt["importRequestId"]==failed&&!(bool)receipt["importCommitted"]&&Convert.ToInt64(Dashboard(restored,first)["rawRowCount"])==6,"Failed request was mistaken for a committed import");
        }
        string incompleteHome=Path.Combine(root,"incomplete"),incompleteId=Guid.NewGuid().ToString("N");
        using(var service=new AnalysisService(incompleteHome))Finish(service,service.StartImport(Json.Serialize(new{importRequestId=incompleteId,filePaths=new[]{a},fileAssignments=Bindings(new[]{a},new[]{"TCA"}),options=Options()})));
        // Raw rows and a finished projection alone do not prove a cancelled initial input committed.
        File.Delete(Path.Combine(incompleteHome,incompleteId,"import-committed.txt"));
        using(var restored=new AnalysisService(incompleteHome)){var receipt=Finish(restored,restored.Status(Json.Serialize(new{analysisId=incompleteId,action="restore"})));Check(!(bool)receipt["importCommitted"],"Unconfirmed initial input was declared committed");}
        Console.WriteLine("PASS import receipt identity, repeated POST while running/completed, unavailable originals, restart recovery and failed-request distinction.");
    }
    static void RepeatedTransitions(string root)
    {
        var random=new Random(21821);var paths=new Dictionary<string,string>();string id=null;
        using(var service=new AnalysisService(Path.Combine(root,"repeated")))
        {
            for(int step=0;step<24;step++)
            {
                string position=Defs[random.Next(Defs.Count)].name,path=Path.Combine(root,"sequence-"+step+".csv");bool tools=step%3!=0;
                using(var writer=new StreamWriter(path))
                {
                    writer.Write(tools?Header:Header.Substring(0,Header.IndexOf(",Crack_result",StringComparison.Ordinal))+"\r\n");
                    int count=step%7==0?0:random.Next(1,45);
                    for(int i=0;i<count;i++)
                    {
                        string result=new[]{"NG","OK",""}[random.Next(3)],score=random.Next(5)==0?"":(random.Next(11)/10.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        string row=Row("2026-02-0"+random.Next(1,4),random.Next(1,13).ToString("D6"),result,score);
                        if(!tools)row=string.Join(",",row.TrimEnd('\r','\n').Split(',').Take(9))+"\r\n";
                        writer.Write(row);
                    }
                }
                id=(string)Import(service,new[]{path},new[]{position},id)["analysisId"];paths[position]=path;Settings(service,id);
                Compare(service,id,root,paths.Values.ToArray(),paths.Keys.ToArray());
                Check(!Directory.EnumerateDirectories(Path.Combine(root,"repeated",id),"import-*").Any(),"Repeated update retained staging DBs");
            }
            string source=Path.Combine(root,"repeated",id,"source.sqlite"),cache=Path.Combine(root,"repeated",id,"projection.sqlite");
            Check(Scalar(source,"SELECT COUNT(*) FROM tool_results t LEFT JOIN images i ON i.image_id=t.image_id WHERE i.image_id IS NULL")==0,"Repeated replacements left orphaned tools");
            Check(Scalar(cache,"SELECT COUNT(*) FROM position_stats WHERE total<0 OR ng<0 OR ng>total OR raw_rows<total")==0&&Scalar(cache,"SELECT COUNT(*) FROM cell_stats WHERE total<0 OR ng<0 OR ng>total")==0,"Repeated replacements corrupted counters");
        }
        Console.WriteLine("PASS 24 seeded replacements: four Positions, empty inputs, duplicate Cells/dates, NG/OK/unknown, absent Scores/Tools, thresholds and actual NG agree with full rebuild.");
    }
    static void MissingWorkspaceMetadata(string root)
    {
        string directory=Path.Combine(root,"metadata"),header="Date,Time,Cell ID,Position,Total_result,FullPath,Crack_result,Crack_score\r\n";Directory.CreateDirectory(directory);
        var paths=new List<string>();
        for(int i=0;i<80;i++){string path=Path.Combine(directory,"result-long-file-name-part-"+i.ToString("D3")+".csv");File.WriteAllText(path,header+"2026-02-03,12:00:00,000001,TCA,NG,C:\\Exact\\original.jpg,NG,.9\r\n");paths.Add(path);}
        foreach(string parent in new[]{"one","two"}){Directory.CreateDirectory(Path.Combine(directory,parent));string path=Path.Combine(directory,parent,"same.csv");File.WriteAllText(path,header+"2026-02-03,12:00:00,000001,TCA,OK,C:\\Exact\\original.jpg,OK,.1\r\n");paths.Add(path);}
        using(var store=new SqliteRunStore(Path.Combine(directory,"source.sqlite")))
        {
            string run;new CsvHistoryFileImporter(store,Json).ImportFiles(new AgentHistoryFileImportRequest{positionDefinitions=Defs},paths,null,CancellationToken.None,out run);
            Check(Scalar(store.DatabasePath,"SELECT MAX(length(workspace_name)) FROM images")<=64&&Scalar(store.DatabasePath,"SELECT MAX(length(workspace_key)) FROM images")<=80,"Missing Workspace metadata repeats the full selected file list per row");
            Check(Scalar(store.DatabasePath,"SELECT COUNT(DISTINCT workspace_key) FROM images")==paths.Count,"Different input paths with identical filenames share a fallback identity");
        }
        Console.WriteLine("PASS 82 files without Workspace metadata use bounded per-file identities, including identical filenames under different paths; explicit Workspace metadata remains covered separately.");
    }
    static void Benchmark(string root,int cells)
    {
        Directory.CreateDirectory(root);string[] names=Defs.Select(d=>d.name).ToArray(),paths=names.Select(n=>Path.Combine(root,n+".csv")).ToArray();
        for(int p=0;p<4;p++)using(var writer=new StreamWriter(paths[p])){writer.Write(Header);for(int i=0;i<cells;i++)writer.Write(Row("2026-02-"+(i%28+1).ToString("D2"),i.ToString("D12"),i%10==0?"NG":"OK",i%10==0?".9":".1"));}
        double oldSeconds=0,newSeconds=0;var timings=new List<object>();
        for(int n=1;n<=4;n++)
        {
            var clock=Stopwatch.StartNew();string db=Path.Combine(root,"old"+n+".sqlite"),cache=Path.Combine(root,"old-projection"+n+".sqlite"),run;
            using(var store=new SqliteRunStore(db))new CsvHistoryFileImporter(store,Json).ImportFiles(new AgentHistoryFileImportRequest{positionDefinitions=Defs},paths.Take(n),null,CancellationToken.None,out run,null,null,(CsvHistoryFileImporter.FileAssignment[])Bindings(paths.Take(n).ToArray(),names.Take(n).ToArray()));
            using(var projection=new AnalysisProjection(db,cache,run)){while(projection.Advance(CancellationToken.None,10000)){}projection.ConfigurePositions(Defs,CancellationToken.None);projection.SetThresholds(new AnalysisProjection.ThresholdValue[0],CancellationToken.None);projection.Dashboard("",.8,CancellationToken.None);}
            double seconds=clock.Elapsed.TotalSeconds;oldSeconds+=seconds;timings.Add(new{mode="previous",positions=n,seconds});Console.WriteLine("BENCH previous positions="+n+" seconds="+seconds.ToString("F3"));
        }
        using(var service=new AnalysisService(Path.Combine(root,"new")))
        {
            string id=null;
            for(int n=1;n<=4;n++){var clock=Stopwatch.StartNew();var imported=Import(service,new[]{paths[n-1]},new[]{names[n-1]},id);id=(string)imported["analysisId"];if(n==1)Finish(service,service.Thresholds(Json.Serialize(new{analysisId=id,thresholds=new object[0]})));var dashboard=Dashboard(service,id);Check(Convert.ToInt64(dashboard["rawRowCount"])==n*(long)cells&&Convert.ToInt64(dashboard["uniqueCellCount"])==cells,"Scale import lost or double-counted cells");double seconds=clock.Elapsed.TotalSeconds;newSeconds+=seconds;timings.Add(new{mode="incremental",positions=n,seconds,imported=imported["imported"]});Console.WriteLine("BENCH incremental positions="+n+" seconds="+seconds.ToString("F3")+" imported="+imported["imported"]);}
        }
        var result=new{cellsPerPosition=cells,positions=4,tools=4,oldSeconds,newSeconds,timings,peakWorkingMiB=Process.GetCurrentProcess().PeakWorkingSet64/1048576.0};File.WriteAllText(Path.Combine(root,"results.json"),Json.Serialize(result));Console.WriteLine(Json.Serialize(result));
    }
}
