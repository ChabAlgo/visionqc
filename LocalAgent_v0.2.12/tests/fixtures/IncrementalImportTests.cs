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
    static void Main(string[] args){try{string root=args[0];Directory.CreateDirectory(root);Correctness(root);RenamedPosition(root);int cells=int.Parse(args[1]);if(cells>0)Benchmark(Path.Combine(root,"benchmark"),cells);}catch(Exception error){Console.WriteLine(error);Environment.Exit(1);}}
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
