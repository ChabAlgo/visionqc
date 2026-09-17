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
            using(var connection=new System.Data.SQLite.SQLiteConnection("Data Source="+store.DatabasePath+";Version=3;"))
            { connection.Open(); using(var command=connection.CreateCommand()) { command.CommandText="SELECT COUNT(*) FROM images"; Check(Convert.ToInt64(command.ExecuteScalar())==1,"Committed partial batches retained"); } }
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
        Console.WriteLine("PASS: isolated SQLite precision, workspace/live metadata, failed import rollback after 401 rows; original history preserved. " + root);
    }
}
