using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent { internal static class Program { internal const string AgentVersion = "test"; } }
class HistoryIntegrityTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main() { try { Run(); } catch(Exception e) { Console.WriteLine(e.GetType().FullName + ": " + e.Message); Console.WriteLine(e.StackTrace); Environment.Exit(1); } }
    static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "VisionQC-history-integrity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Combine(root,"input.csv");
        string header = "CaptureTimestamp,Cell ID,Position,Total_result,FullPath,WorkspaceType,WorkspaceName,WorkspaceKey,Crack_result,Crack_score\r\n";
        string row = "2026-02-03T08:00:00,P163GG22M2100001,AN(TOP),NG,C:\\original.jpg,green,Workspace A,green-a,NG,0.9000987654321\r\n";
        using (var store = new SqliteRunStore(Path.Combine(root,"test.sqlite")))
        {
            var importer = new CsvHistoryFileImporter(store,new JavaScriptSerializer());
            File.WriteAllText(path,header+row);
            Check(importer.Import(new AgentHistoryFileImportRequest(),path,null)==1,"Import count");
            var result=store.Search(new AgentHistorySearchRequest());
            Check(result.totalCount==1 && result.items[0].workspaceKey=="green-a","Workspace identity lost");
            Check(result.items[0].tools[0].score==.9000987654321,"Precision lost");
            var page=store.ReadSimulationResultPage(result.items[0].runId,0,100);
            Check(page.records[0].WorkspaceKey=="green-a","Live metadata lost");
            File.WriteAllText(path,header+string.Concat(Enumerable.Repeat(row,401))+"bad,row\r\n");
            bool rejected=false;
            try { importer.Import(new AgentHistoryFileImportRequest(),path,null); } catch (InvalidDataException) {rejected=true;}
            Check(rejected && store.Search(new AgentHistorySearchRequest()).totalCount==1,"Failed import polluted existing history");
            string secondPath = Path.Combine(root,"second.csv"), multiRun;
            File.WriteAllText(path,header+row);
            File.WriteAllText(secondPath,header+row.Replace("AN(TOP)","CA(TOP)").Replace("green-a","green-b"));
            Check(importer.ImportFiles(new AgentHistoryFileImportRequest(),new[]{path,secondPath},null,System.Threading.CancellationToken.None,out multiRun)==2,"Multiple CSV count");
            var multi=store.ReadSimulationResultPage(multiRun,0,100);
            Check(multi.records.Count==2 && multi.records[0].Position=="AN(TOP)" && multi.records[1].Position=="CA(TOP)" && multi.records[1].WorkspaceKey=="green-b","Multiple CSV metadata changed");
            string cache=Path.Combine(root,"projection.sqlite");
            using(var projection=new AnalysisProjection(store.DatabasePath,cache,multiRun))
            {
                bool concurrentWrite=false;
                projection.Timing=(phase,ms)=>{
                    if(phase!="source-cursor")return;
                    using(var c=new System.Data.SQLite.SQLiteConnection("Data Source="+store.DatabasePath+";Version=3;Default Timeout=1;"))
                    {c.Open();using(var cmd=c.CreateCommand()){cmd.CommandText="UPDATE runs SET message='concurrent writer' WHERE run_id=@run";cmd.Parameters.AddWithValue("@run",multiRun);cmd.ExecuteNonQuery();}}
                    concurrentWrite=true;
                };
                Check(projection.Advance(System.Threading.CancellationToken.None,1),"Projection first page missing");
                Check(concurrentWrite,"Source writes were blocked by analysis read snapshot");
                projection.Timing=null;
                long cursor=projection.Cursor;
                Check(projection.Advance(System.Threading.CancellationToken.None,1) && projection.Cursor>cursor,"Projection cursor did not advance");
                Check(!projection.Advance(System.Threading.CancellationToken.None),"Projection replayed observations");
                var summary=projection.Summary();
                var totals=(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)summary["totals"];
                Check(Convert.ToInt64(totals[0]["total"])==2 && Convert.ToInt64(totals[0]["ng"])==2,"Projection counts differ");
                projection.SetThresholds(new[]{new AnalysisProjection.ThresholdValue {position="AN(TOP)",tool="Crack",value=.95}},System.Threading.CancellationToken.None);
                totals=(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)projection.Summary()["totals"];
                Check(Convert.ToInt64(totals[0]["ng"])==1,"Projection threshold changed another Position");
                projection.ReplaceActualNg(new[]{new AnalysisProjection.ActualNgValue {day="2026-02-03",cell="P163GG22M2100001",position="AN(TOP)",path="actual.jpg"}},System.Threading.CancellationToken.None);
                var scoreJson=new JavaScriptSerializer();
                var pointPage=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ScorePage("Crack","ALL","ACTUAL_NG_TOOL_OK",-1,0,500)));
                Check(((object[])pointPage["records"]).Length==1,"Missed-cell score did not follow final Position threshold");
                var actualStats=(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)projection.ActualSummary("",System.Threading.CancellationToken.None);
                Check(actualStats.Count==1 && Convert.ToInt64(actualStats[0]["actualNg"])==1 && Convert.ToInt64(actualStats[0]["misses"])==1,"Actual summary disagrees with final threshold");
                var misses=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.MissPage("","AN(TOP)","","",1,System.Threading.CancellationToken.None)));
                Check(((object[])misses["records"]).Length==1 && !(bool)misses["hasMore"],"Miss page lost final-threshold miss");
                var detail=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.CellDetail("2026-02-03","P163GG22M2100001","AN(TOP)",0,1,System.Threading.CancellationToken.None)));
                Check(((object[])detail["records"]).Length==1 && ((object[])detail["actualImages"]).Length==1,"Cell detail omitted original observation or actual image");
                Check(((System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)projection.ActualSummary("2026-02-04",System.Threading.CancellationToken.None)).Count==0,"Actual summary leaked another date");
                var firstPage=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ScorePage("Crack","ALL","TOOL_NG",-1,0,1)));
                Check((bool)firstPage["hasMore"],"Score paging dropped equal-score observation");
                var nextPage=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ScorePage("Crack","ALL","TOOL_NG",Convert.ToDouble(firstPage["nextScore"]),Convert.ToInt64(firstPage["nextId"]),1)));
                Check(((object[])nextPage["records"]).Length==1 && !(bool)nextPage["hasMore"],"Equal-score navigation duplicated or skipped record");
                var stats=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ScoreStatistics("Crack","ALL","TOOL_NG",false,.8,System.Threading.CancellationToken.None)));
                var scoreTotal=(System.Collections.Generic.Dictionary<string,object>)stats["total"];
                Check(Convert.ToInt64(scoreTotal["count"])==2 && Convert.ToDouble(scoreTotal["min"])==Convert.ToDouble(scoreTotal["median"]),"Server graph totals or median differ from observations");
                var backwards=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ScoreWindow("Crack","ALL","TOOL_NG",false,.8,Convert.ToDouble(nextPage["nextScore"]),Convert.ToInt64(nextPage["nextId"]),1,true,System.Threading.CancellationToken.None)));
                Check(Convert.ToInt64(backwards["nextId"])==Convert.ToInt64(firstPage["nextId"]),"Reverse score cursor skipped equal-score neighbor");
                var dashboard=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.Dashboard("",.8,System.Threading.CancellationToken.None)));
                Check(Convert.ToInt64(dashboard["recordCount"])==2 && Convert.ToInt64(dashboard["missCount"])==1,"Remote dashboard count differs from native facts");
                string actualBatch=Guid.NewGuid().ToString("N");
                projection.StageActualNg(actualBatch,"begin",null,System.Threading.CancellationToken.None);
                projection.StageActualNg(actualBatch,"append",new[]{new AnalysisProjection.ActualNgValue{cell="P163GG22M2100001",position="AN(TOP)",wildcard=true}},System.Threading.CancellationToken.None);
                Check(((System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)projection.ActualSummary("",System.Threading.CancellationToken.None)).Count==1,"Staging replaced committed actual-NG input early");
                projection.StageActualNg(actualBatch,"commit",null,System.Threading.CancellationToken.None);
                actualStats=(System.Collections.Generic.List<System.Collections.Generic.Dictionary<string,object>>)projection.ActualSummary("",System.Threading.CancellationToken.None);
                Check(Convert.ToInt64(actualStats[0]["misses"])==1,"CSV-only actual ID did not expand to captured day");
                string exportRoot=Path.Combine(root,"export");Directory.CreateDirectory(exportRoot);
                var exported=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ExportRaw(exportRoot,1,true,System.Threading.CancellationToken.None,null)));
                Check(Convert.ToInt64(exported["count"])==2 && ((object[])exported["files"]).Length==2,"Native export split count differs");
                var missExport=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ExportFiltered(exportRoot,1,true,"misses","","AN(TOP)","Crack","TOOL_NG",false,.8,"lte",1,System.Threading.CancellationToken.None)));
                Check(Convert.ToInt64(missExport["count"])==1,"Miss export must use final Cell judgement");
                var scoreExport=(System.Collections.Generic.Dictionary<string,object>)scoreJson.DeserializeObject(scoreJson.Serialize(projection.ExportFiltered(exportRoot,1,true,"score","","ALL","Crack","TOOL_NG",false,.8,"lte",1,System.Threading.CancellationToken.None)));
                Check(Convert.ToInt64(scoreExport["count"])==2 && ((object[])scoreExport["files"]).Length==2,"Score export must preserve all equal-score observations");
                using(var subset=new SqliteRunStore(Path.Combine(root,"subset.sqlite")))
                {
                    var session=subset.Start(new SqliteRunStore.RunStoreStart{SourceType="test-subset"});
                    subset.CopyAnalysisRows(session,store.DatabasePath,multiRun,"AN(TOP)");subset.Complete(session,"completed","");
                    var subsetRows=subset.ReadSimulationResultPage(session.RunId,0,100);
                    Check(subsetRows.records.Count==1 && subsetRows.records[0].Position!="AN(TOP)" && subsetRows.records[0].Tools["Crack"].Score==.9000987654321,"Position removal changed retained observation");
                    Check(store.ReadSimulationResultPage(multiRun,0,100).records.Count==2,"Position removal modified source");
                }
                using(var roundtrip=new SqliteRunStore(Path.Combine(root,"roundtrip.sqlite")))
                {
                    string restoredRun;new CsvHistoryFileImporter(roundtrip,scoreJson).ImportFiles(new AgentHistoryFileImportRequest(),((object[])exported["files"]).Cast<string>(),null,System.Threading.CancellationToken.None,out restoredRun);
                    var restored=roundtrip.ReadSimulationResultPage(restoredRun,0,100);
                    Check(restored.records.Count==2 && restored.records[0].Tools["Crack"].Score==.9000987654321 && restored.records[0].CaptureTimestamp=="2026-02-03T08:00:00","Native export roundtrip changed scores or capture time");
                }
            }
            using(var projection=new AnalysisProjection(store.DatabasePath,cache,multiRun)) Check(!projection.Advance(System.Threading.CancellationToken.None),"Restart duplicated projection");
            File.WriteAllText(secondPath,header+string.Concat(Enumerable.Repeat(row,401))+"bad,row\r\n");
            rejected=false;
            try { importer.ImportFiles(new AgentHistoryFileImportRequest(),new[]{path,secondPath},null,System.Threading.CancellationToken.None,out multiRun); } catch(InvalidDataException){rejected=true;}
            Check(rejected,"Malformed second file accepted");
            var cancelled=new System.Threading.CancellationTokenSource(); cancelled.Cancel();
            rejected=false;
            try { importer.ImportFiles(new AgentHistoryFileImportRequest(),new[]{path},null,cancelled.Token,out multiRun); } catch(OperationCanceledException){rejected=true;}
            Check(rejected,"Cancelled import completed");
            using(var connection=new System.Data.SQLite.SQLiteConnection("Data Source="+store.DatabasePath+";Version=3;"))
            { connection.Open(); using(var command=connection.CreateCommand()) { command.CommandText="SELECT COUNT(*) FROM images"; Check(Convert.ToInt64(command.ExecuteScalar())==3,"Committed partial batches retained"); } }
        }
        string serviceRoot=Path.Combine(root,"analysis-service"), analysisId;
        File.WriteAllText(path,header+row);
        var serializer=new JavaScriptSerializer();
        using(var service=new AnalysisService(serviceRoot))
        {
            var started=serializer.DeserializeObject(serializer.Serialize(service.StartImport(serializer.Serialize(new {filePaths=new[]{path}})))) as System.Collections.Generic.Dictionary<string,object>;
            analysisId=(string)started["analysisId"];
            var request=serializer.Serialize(new {analysisId});
            System.Collections.Generic.Dictionary<string,object> status;
            do { System.Threading.Thread.Sleep(10); status=(System.Collections.Generic.Dictionary<string,object>)serializer.DeserializeObject(serializer.Serialize(service.Status(request))); } while((bool)status["running"]);
            Check((bool)status["completed"],"Async analysis did not finish");
            Check(serializer.Serialize(service.Summary(request)).Contains("rawRows"),"Async summary missing");
        }
        using(var service=new AnalysisService(serviceRoot))
        {
            var request=serializer.Serialize(new {analysisId});
            System.Collections.Generic.Dictionary<string,object> status;
            do { System.Threading.Thread.Sleep(10); status=(System.Collections.Generic.Dictionary<string,object>)serializer.DeserializeObject(serializer.Serialize(service.Status(request))); } while((bool)status["running"]);
            Check((bool)status["completed"],"Analysis reference did not restore after restart");
        }
        using(var splitStore=new SqliteRunStore(Path.Combine(root,"date-time.sqlite")))
        {
            string datePath=Path.Combine(root,"date-time.csv");
            File.WriteAllText(datePath,"Date,Time,InspectionDate,InspectionTime,Cell ID,Position,total_result,FullPath,Crack_result,Crack_score\r\n2026-08-07,01:20:30,2026-09-21,12:30:45,P163GG22M2100001,AN(TOP),NG,C:\\image.jpg,NG,0.81234567890123\r\n");
            var importer=new CsvHistoryFileImporter(splitStore,new JavaScriptSerializer()); importer.Import(new AgentHistoryFileImportRequest(),datePath,null);
            var item=splitStore.Search(new AgentHistorySearchRequest()).items.Single();
            Check(item.captureTimestamp=="2026-08-07T01:20:30" && item.tools.Single().score==.81234567890123,"Split capture date/time changed on import");
        }
        using (var pending = new SqliteRunStore(Path.Combine(root,"pending.sqlite")))
        using (var saved = new SqliteRunStore(Path.Combine(root,"saved.sqlite")))
        {
            var first=saved.Start(new SqliteRunStore.RunStoreStart {SourceType="csv-analysis"});
            saved.AppendImportedRecord(first,new AgentHistoryRecordRequest {cellId="existing",position="CA(TOP)",workspaceType="blue",workspaceKey="blue-only",workspaceName="Blue",totalResult="OK",tools=new System.Collections.Generic.List<AgentHistoryToolResultRequest>{new AgentHistoryToolResultRequest {tool="Edge",result="OK",score=.7}}});
            saved.Complete(first,"completed","");
            var run=pending.Start(new SqliteRunStore.RunStoreStart {SourceType="simulation"});
            for(int i=0;i<401;i++) pending.AppendImportedRecord(run,new AgentHistoryRecordRequest {cellId="cell"+i,position="AN(TOP)",workspaceType="green",workspaceKey="green-only",workspaceName="Green",captureTimestamp="2026-09-16T08:34:26.5892578Z",totalResult="NG",tools=new System.Collections.Generic.List<AgentHistoryToolResultRequest>{new AgentHistoryToolResultRequest {tool="Crack",result="NG",score=.9000987654321}}});
            Check(saved.Search(new AgentHistorySearchRequest()).totalCount==1,"Unapproved simulation leaked into history");
            bool rejected=false;try{saved.CopyCompletedRunFrom(pending.DatabasePath,run.RunId);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Running run was saved");
            pending.Complete(run,"completed","");
            saved.CopyCompletedRunFrom(pending.DatabasePath,run.RunId);
            saved.CopyCompletedRunFrom(pending.DatabasePath,run.RunId);
            Check(saved.Search(new AgentHistorySearchRequest()).totalCount==402,"Approval retry duplicated or lost records");
            var selected=saved.Search(new AgentHistorySearchRequest {position="AN(TOP)"});
            Check(selected.filterOptions.positions.Count==2 && selected.filterOptions.tools.SequenceEqual(new[]{"Crack"}) && selected.filterOptions.workspaceTypes.SequenceEqual(new[]{"green"}) && selected.filterOptions.workspaces.Single().value=="green-only","Dependent filter leaked another Position");
            Check(selected.items[0].captureTimestamp=="2026-09-16T08:34:26.5892578Z" && selected.items[0].tools.Single().score==.9000987654321,"Approved copy lost precision or timestamp");
            Check(pending.ReadSimulationResultPage(run.RunId,0,1000).records.Count==401,"Approval removed dashboard recovery data");
            var declined=pending.Start(new SqliteRunStore.RunStoreStart {SourceType="simulation"});
            pending.AppendImportedRecord(declined,new AgentHistoryRecordRequest {cellId="declined",position="AN(TOP)"});pending.Complete(declined,"completed","");
            Check(saved.Search(new AgentHistorySearchRequest()).totalCount==402,"No decision persisted records");
        }
        using(var selectedStore=new SqliteRunStore(Path.Combine(root,"selected.sqlite")))
        {
            var run=selectedStore.Start(new SqliteRunStore.RunStoreStart {SourceType="simulation"});
            foreach(string pos in new[]{"AN(TOP)","CA(TOP)"})for(int day=1;day<=2;day++)for(int cell=0;cell<20;cell++)for(int duplicate=0;duplicate<2;duplicate++)
                selectedStore.AppendImportedRecord(run,new AgentHistoryRecordRequest {cellId="CELL"+cell,position=pos,workspaceKey=pos,captureTimestamp="2026-02-0"+day+"T12:34:56",totalResult=cell<17?"NG":"OK",tools=new System.Collections.Generic.List<AgentHistoryToolResultRequest>{new AgentHistoryToolResultRequest {tool="FoilDamage",result=cell<17?"NG":"OK",score=cell<10?.9:.7}}});
            selectedStore.Complete(run,"completed","");
            var selection=new AgentHistorySearchRequest {positions=new System.Collections.Generic.List<string>{"AN(TOP)"},fromDate="2026-02-01",toDate="2026-02-01"};
            Check(selectedStore.Search(selection).totalCount==20,"History Position/date dedup mismatch");
            Check(selectedStore.Search(new AgentHistorySearchRequest{positions=new System.Collections.Generic.List<string>()}).totalCount==0,"Empty positions exported all history");
            var json=new JavaScriptSerializer();var cancel=System.Threading.CancellationToken.None;
            string output=Path.Combine(root,"selected-export");Directory.CreateDirectory(output);
            var history=(System.Collections.Generic.Dictionary<string,object>)json.DeserializeObject(json.Serialize(selectedStore.ExportSearch(selection,output,7,false,cancel)));
            Check(Convert.ToInt64(history["count"])==20&&((object[])history["files"]).Length==3,"History export used page/raw count");
            string firstFile=Convert.ToString(((object[])history["files"])[0]);
            Check(File.ReadAllText(firstFile).Contains("2026-02-01,12:34:56,CELL"),"History CSV capture time lost");
            using(var projection=new AnalysisProjection(selectedStore.DatabasePath,Path.Combine(root,"selected-projection.sqlite"),run.RunId))
            {
                while(projection.Advance(cancel)){}
                Func<string[],string,string,System.Collections.Generic.Dictionary<string,object>> export=(positions,date,tool)=>(System.Collections.Generic.Dictionary<string,object>)json.DeserializeObject(json.Serialize(projection.ExportSelection(output,1000000,false,date,positions,tool,cancel)));
                Check(Convert.ToInt64(export(new[]{"AN(TOP)"},"2026-02-01","FoilDamage")["count"])==17,"17 NG chart must export exactly 17 deduplicated Cells");
                Check(Convert.ToInt64(export(null,"","FoilDamage")["count"])==68,"All date/Position tool export mismatch");
                Check(Convert.ToInt64(export(new string[0],"","")["count"])==0,"Empty selection exported all rows");
                Check(Convert.ToInt64(export(new[]{"CA(TOP)"},"2026-02-02","")["count"])==20,"Date export lost OK Cells");
                projection.SetThresholds(new[]{new AnalysisProjection.ThresholdValue{position="AN(TOP)",tool="FoilDamage",value=.8}},cancel);
                Check(Convert.ToInt64(export(new[]{"AN(TOP)"},"2026-02-01","FoilDamage")["count"])==10,"Tool export ignored threshold");
                var window=(System.Collections.Generic.Dictionary<string,object>)json.DeserializeObject(json.Serialize(projection.DateWindow(-1,"",cancel,new[]{"AN(TOP)"},"2026-02-01")));
                var summary=(System.Collections.Generic.Dictionary<string,object>)window["summary"];
                Check(Convert.ToInt64(summary["totalCount"])==20&&Convert.ToInt64(summary["ngCount"])==10&&((object[])window["rows"]).Length==2,"Graph selection summary diverged from exports");
            }
        }
        Console.WriteLine("PASS: isolated SQLite precision, workspace/live metadata, failed import rollback after 401 rows; original history preserved. " + root);
    }
}
