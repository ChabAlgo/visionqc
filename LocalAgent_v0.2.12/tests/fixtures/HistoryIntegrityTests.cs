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
    static void Main()
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
        Console.WriteLine("PASS: isolated SQLite precision, workspace/live metadata, failed import rollback after 401 rows; original history preserved. " + root);
    }
}
