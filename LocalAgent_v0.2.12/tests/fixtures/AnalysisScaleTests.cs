using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Persistence;

class AnalysisScaleTests
{
    static void Main(string[] args)
    {
        try { Run(args.Length == 0 ? 20000 : int.Parse(args[0]),args.Length>1 ? args[1] : null,args.Length>2 ? args[2] : null); }
        catch(Exception e) { Console.WriteLine(e.GetType().FullName+": "+e.Message); Console.WriteLine(e.StackTrace); Environment.Exit(1); }
    }
    static void Check(bool condition,string reason) {if(!condition)throw new Exception(reason);}
    static void Run(int cells,string reuseRoot,string reuseCache)
    {
        string root=reuseRoot ?? Path.Combine(Path.GetTempPath(),"VisionQC-analysis-scale-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string source=Path.Combine(root,"source.sqlite"),cache=reuseCache??Path.Combine(root,"projection-"+Guid.NewGuid().ToString("N")+".sqlite"),run;
        var clock=Stopwatch.StartNew();
        if(reuseRoot==null)
        {
        using(var store=new SqliteRunStore(source)) {var session=store.Start(new SqliteRunStore.RunStoreStart {SourceType="scale-test"});run=session.RunId;store.Complete(session,"completed","");}
        using(var connection=new SQLiteConnection("Data Source="+source+";Version=3;"))
        {
            connection.Open();
            for(int offset=0;offset<cells;offset+=10000)
            using(var tx=connection.BeginTransaction()) using(var command=connection.CreateCommand())
            {
                command.Transaction=tx;
                command.Parameters.AddWithValue("@run",run);command.Parameters.AddWithValue("@offset",offset);command.Parameters.AddWithValue("@end",Math.Min(cells,offset+10000));
                command.CommandText=@"WITH RECURSIVE n(v) AS (SELECT @offset+1 UNION ALL SELECT v+1 FROM n WHERE v<@end),p(v) AS (SELECT 0 UNION ALL SELECT 1)
INSERT INTO images(run_id,sequence_no,source_file_name,cell_id,position_key,total_result,capture_timestamp,inspected_at_utc,workspace_key,workspace_name)
SELECT @run,n.v*2+p.v,printf('image_%d.jpg',n.v),printf('%016d',n.v),CASE p.v WHEN 0 THEN 'AN(TOP)' ELSE 'AN(BOT)' END,
CASE WHEN n.v%10=0 OR n.v%7=0 THEN 'NG' ELSE 'OK' END,date('2026-01-01',printf('+%d days',n.v%300))||'T12:00:00','2026-09-21T00:00:00Z',printf('ws%d',p.v),printf('Workspace %d',p.v) FROM n CROSS JOIN p;
WITH RECURSIVE t(v) AS (SELECT 0 UNION ALL SELECT v+1 FROM t WHERE v<3)
INSERT INTO tool_results(run_id,image_id,tool_name,result,score)
SELECT i.run_id,i.image_id,printf('Tool%d',t.v),CASE WHEN (t.v=0 AND CAST(i.cell_id AS INTEGER)%10=0) OR (t.v=1 AND CAST(i.cell_id AS INTEGER)%7=0) THEN 'NG' ELSE 'OK' END,0.80000000000001
FROM images i CROSS JOIN t WHERE i.run_id=@run AND i.sequence_no>(@offset+1)*2-1 AND i.sequence_no<=@end*2+1;";
                command.ExecuteNonQuery();tx.Commit();
            }
        }
        }
        else using(var connection=new SQLiteConnection("Data Source="+source+";Version=3;"))
        {connection.Open();using(var c=connection.CreateCommand()){c.CommandText="SELECT run_id FROM runs LIMIT 1";run=(string)c.ExecuteScalar();}}
        double buildSeconds=clock.Elapsed.TotalSeconds;clock.Restart();
        long batches=0,peak=0;
        using(var projection=new AnalysisProjection(source,cache,run))
        {
            var phaseMs=new Dictionary<string,double>();
            projection.Timing=(name,ms)=>{double prior;phaseMs.TryGetValue(name,out prior);phaseMs[name]=prior+ms;};
            while(projection.Advance(CancellationToken.None,10000))
            {
                peak=Math.Max(peak,Process.GetCurrentProcess().WorkingSet64);
                if(++batches%100==0) Console.WriteLine("Projected cursor="+projection.Cursor+"; workingMiB="+(peak/1048576));
            }
            double projectSeconds=clock.Elapsed.TotalSeconds;clock.Restart();
            var summary=projection.Summary();
            double summaryMs=clock.Elapsed.TotalMilliseconds;
            var totals=(List<Dictionary<string,object>>)summary["totals"];
            var unique=(List<Dictionary<string,object>>)summary["cells"];
            long ng=cells/10+cells/7-cells/70;
            Check(Convert.ToInt64(totals[0]["total"])==cells*2L,"Position total mismatch");
            Check(Convert.ToInt64(totals[0]["ng"])==ng*2L,"Position NG mismatch");
            Check(Convert.ToInt64(unique[0]["total"])==cells && Convert.ToInt64(unique[0]["ng"])==ng,"Unique Cell mismatch");
            Check(!projection.Advance(CancellationToken.None),"Replay changed cursor");
            clock.Restart();
            var jsonCodec=new JavaScriptSerializer();
            var dashboard=(Dictionary<string,object>)jsonCodec.DeserializeObject(jsonCodec.Serialize(projection.Dashboard("",.8,CancellationToken.None)));
            double dashboardMs=clock.Elapsed.TotalMilliseconds;
            Check(Convert.ToInt64(dashboard["recordCount"])==cells*2L,"Dashboard total mismatch");
            clock.Restart();
            var stats=(Dictionary<string,object>)jsonCodec.DeserializeObject(jsonCodec.Serialize(projection.ScoreStatistics("Tool0","ALL","TOOL_NG",false,.8,CancellationToken.None)));
            double scoreStatsMs=clock.Elapsed.TotalMilliseconds;
            Check(Convert.ToInt64(((Dictionary<string,object>)stats["total"])["count"])==cells/10*2L,"Score total mismatch");
            var page=(Dictionary<string,object>)jsonCodec.DeserializeObject(jsonCodec.Serialize(projection.ScoreWindow("Tool0","ALL","TOOL_NG",false,.8,-1,0,300,false,CancellationToken.None)));
            Check(((object[])page["records"]).Length==Math.Min(300,cells/10*2),"Bounded score page mismatch");
            Console.WriteLine("VIEW PASS dashboardMs="+dashboardMs+" scoreStatsMs="+scoreStatsMs+" dashboardBytes="+jsonCodec.Serialize(dashboard).Length);
            var actual=new List<AnalysisProjection.ActualNgValue>();
            for(int i=1;i<=Math.Min(1000,cells);i++)actual.Add(new AnalysisProjection.ActualNgValue{day=new DateTime(2026,1,1).AddDays(i%300).ToString("yyyy-MM-dd"),cell=i.ToString("D16"),position="AN(TOP)",path="actual-"+i+".jpg"});
            projection.ReplaceActualNg(actual,CancellationToken.None);clock.Restart();
            var actualDashboard=(Dictionary<string,object>)jsonCodec.DeserializeObject(jsonCodec.Serialize(projection.Dashboard("",.8,CancellationToken.None)));
            Check(Convert.ToInt64(actualDashboard["matchedActualCount"])==actual.Count,"Large actual-NG match count differs");
            double actualDashboardMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
            var actualStats=(Dictionary<string,object>)jsonCodec.DeserializeObject(jsonCodec.Serialize(projection.ScoreStatistics("Tool0","ALL","ACTUAL_NG_TOOL_NG",false,.8,CancellationToken.None)));
            Check(Convert.ToInt64(((Dictionary<string,object>)actualStats["total"])["count"])==actual.Count/10,"Large actual score scope differs");
            Console.WriteLine("ACTUAL VIEW PASS dashboardMs="+actualDashboardMs+" scoreStatsMs="+clock.Elapsed.TotalMilliseconds);
            var report=new {phaseMs,reusedSource=reuseRoot!=null,cache,cells,positions=2,tools=4,rawRows=cells*2L,toolRows=cells*8L,buildSeconds,projectSeconds,summaryMs,peakWorkingMiB=peak/1048576.0,managedMiB=GC.GetTotalMemory(true)/1048576.0,summaryBytes=new JavaScriptSerializer().Serialize(summary).Length,root};
            string json=new JavaScriptSerializer().Serialize(report);File.WriteAllText(Path.Combine(root,"result.json"),json);Console.WriteLine("PASS "+json);
        }
    }
}
