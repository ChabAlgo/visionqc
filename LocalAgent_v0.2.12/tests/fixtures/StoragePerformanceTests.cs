using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Data.SQLite;
using VisionQC.LocalAgent;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent.Services;

class StoragePerformanceTests
{
    static long Scalar(string path,string sql){using(var c=new SQLiteConnection("Data Source="+path+";Version=3;")){c.Open();using(var command=c.CreateCommand()){command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}}}
    static void Main(string[] args)
    {
        try
        {
            string root=args[0];int rows=int.Parse(args[1]);Directory.CreateDirectory(root);
            string csv=Path.Combine(root,"input.csv"),db=Path.Combine(root,"source.sqlite"),cache=Path.Combine(root,"projection.sqlite");
            using(var writer=new StreamWriter(csv)){writer.WriteLine("Date,Time,Cell ID,Position,Total_result,FullPath,Crack_result,Crack_score,Edge_result,Edge_score,Foil_result,Foil_score,Weld_result,Weld_score");for(int i=0;i<rows;i++)writer.WriteLine("2026-02-"+(i%28+1).ToString("D2")+",12:00:00,"+i.ToString("D12")+",TCA,NG,C:\\Exact\\"+i+".jpg,NG,"+(i%10==0?"":".91234567890123")+",OK,.1,NG,.8,OK,");}
            int gen2=GC.CollectionCount(2);string run;var clock=Stopwatch.StartNew();
            using(var store=new SqliteRunStore(db))new CsvHistoryFileImporter(store,new JavaScriptSerializer()).ImportFiles(new AgentHistoryFileImportRequest(),new[]{csv},null,CancellationToken.None,out run);
            double importSeconds=clock.Elapsed.TotalSeconds;clock.Restart();double firstDashboardMs,warmDashboardMs;
            using(var projection=new AnalysisProjection(db,cache,run))
            {
                while(projection.Advance(CancellationToken.None,10000)){}
                double projectionSeconds=clock.Elapsed.TotalSeconds;clock.Restart();projection.Dashboard("",.8,CancellationToken.None);firstDashboardMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();for(int i=0;i<5;i++)projection.Dashboard("",.8,CancellationToken.None);warmDashboardMs=clock.Elapsed.TotalMilliseconds/5;
                if(Scalar(db,"SELECT count(*) FROM images")!=rows||Scalar(db,"SELECT count(*) FROM tool_results")!=rows*4L||Scalar(db,"SELECT count(*) FROM tool_results WHERE score IS NULL")!=rows+(rows+9)/10)throw new Exception("Row/Tool/NULL Score mismatch");
                if(Scalar(db,"SELECT count(*) FROM images WHERE length(cell_id)=12")!=rows||Scalar(db,"SELECT count(*) FROM tool_results WHERE tool_name='Crack' AND score=.91234567890123")!=rows-(rows+9)/10)throw new Exception("Cell ID/Score precision changed");
                var report=new{rows,tools=4,importSeconds,projectionSeconds,firstDashboardMs,warmDashboardMs,gen2Collections=GC.CollectionCount(2)-gen2,peakWorkingMiB=Process.GetCurrentProcess().PeakWorkingSet64/1048576.0};
                string json=new JavaScriptSerializer().Serialize(report);File.WriteAllText(Path.Combine(root,"results.json"),json);Console.WriteLine(json);
            }
        }
        catch(Exception error){Console.WriteLine(error);Environment.Exit(1);}
    }
}
