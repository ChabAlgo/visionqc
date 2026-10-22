using System;
using System.IO;
using System.Data.SQLite;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent.Services;
class ExportScaleTests
{
    static void Main(string[] args) {try{Run(args[0]);}catch(Exception e){Console.WriteLine(e);Environment.Exit(1);}}
    static void Run(string root) {
        Directory.CreateDirectory(root);string source=Path.Combine(root,"source.sqlite"),cache=Path.Combine(root,"projection.sqlite"),run;
        var watch=Stopwatch.StartNew();
        using(var store=new SqliteRunStore(source)){var session=store.Start(new SqliteRunStore.RunStoreStart{SourceType="synthetic-export-scale"});run=session.RunId;store.Complete(session,"completed","");}
        using(var c=new SQLiteConnection("Data Source="+source+";Version=3;")){
            c.Open();using(var tx=c.BeginTransaction())using(var cmd=c.CreateCommand()){
                cmd.Transaction=tx;cmd.Parameters.AddWithValue("@run",run);
                cmd.CommandText=@"WITH RECURSIVE n(v) AS(SELECT 1 UNION ALL SELECT v+1 FROM n WHERE v<100000),t(v) AS(SELECT 0 UNION ALL SELECT v+1 FROM t WHERE v<3)
INSERT INTO images(run_id,sequence_no,source_file_name,cell_id,position_key,total_result,capture_timestamp,inspected_at_utc,full_path)
SELECT @run,n.v*4+t.v,printf('image_%d_%d.jpg',n.v,t.v),printf('%018d',n.v),'AN(TOP)',CASE WHEN n.v%25=0 AND t.v=0 THEN 'NG' ELSE 'OK' END,'2026-02-03T12:00:00','2026-02-04T12:00:00Z',printf('C:\Synthetic\image_%d_%d.jpg',n.v,t.v) FROM n CROSS JOIN t;
INSERT INTO tool_results(run_id,image_id,tool_name,result,score) SELECT run_id,image_id,'Tool'||(sequence_no%4),total_result,.9 FROM images WHERE run_id=@run;";
                cmd.ExecuteNonQuery();tx.Commit();
            }
        }
        using(var p=new AnalysisProjection(source,cache,run)){}
        using(var c=new SQLiteConnection("Data Source="+cache+";Version=3;")){
            c.Open();using(var cmd=c.CreateCommand()){
                cmd.Parameters.AddWithValue("@run",run);cmd.CommandText=@"WITH RECURSIVE n(v) AS(SELECT 1 UNION ALL SELECT v+1 FROM n WHERE v<100000)
INSERT INTO cells SELECT '2026-02-03',printf('%018d',v),'AN(TOP)',4,CASE WHEN v%25=0 THEN 1 ELSE 0 END,1,CASE WHEN v%25=0 THEN 1 ELSE 0 END FROM n;
WITH RECURSIVE n(v) AS(SELECT 1 UNION ALL SELECT v+1 FROM n WHERE v<100000),t(v) AS(SELECT 0 UNION ALL SELECT v+1 FROM t WHERE v<3)
INSERT INTO tools SELECT '2026-02-03',printf('%018d',n.v),'AN(TOP)','Tool'||t.v,CASE WHEN n.v%25=0 AND t.v=0 THEN 1 ELSE 0 END,CASE WHEN n.v%25=0 AND t.v=0 THEN 0 ELSE 1 END,.9,CASE WHEN n.v%25=0 AND t.v=0 THEN .9 ELSE NULL END,CASE WHEN n.v%25=0 AND t.v=0 THEN .9 ELSE NULL END,CASE WHEN n.v%25=0 AND t.v=0 THEN NULL ELSE .9 END,CASE WHEN n.v%25=0 AND t.v=0 THEN 1 ELSE 0 END FROM n CROSS JOIN t;
UPDATE meta SET cursor=400000 WHERE run_id=@run;";cmd.ExecuteNonQuery();
            }
        }
        Console.WriteLine("SETUP ms="+watch.ElapsedMilliseconds);
        using(var p=new AnalysisProjection(source,cache,run)){
            foreach(bool copy in new[]{false,true}){
                var paths=new HashSet<string>();watch.Restart();var json=new JavaScriptSerializer();
                using(var reader=p.OpenExportReader())using(var queue=copy?new ImageCopyPlan(root,"scale","Tool0",Path.Combine(root,"local-queue")):null){
                var result=(Dictionary<string,object>)json.DeserializeObject(json.Serialize(reader.ExportSelection(root,1000000,false,"",new[]{"AN(TOP)"},"Tool0",CancellationToken.None,"scale-"+copy,copy?(Action<string,string>)((path,pos)=>{paths.Add(path);queue.Add(path,pos);}):null)));
                if(Convert.ToInt64(result["count"])!=4000 || (copy&&paths.Count!=4000))throw new Exception("Export integrity mismatch");
                if(copy&&queue.Prepared!=4000)throw new Exception("Local queue count differs");
                Console.WriteLine("EXPORT PASS rawImages=400000 cells=100000 ng=4000 copyListAndLocalQueue="+copy+" ms="+watch.ElapsedMilliseconds);
                }
            }
            watch.Restart();var codec=new JavaScriptSerializer();var page=(Dictionary<string,object>)codec.DeserializeObject(codec.Serialize(p.ToolImages("","AN(TOP)","Tool0",0,50,false,CancellationToken.None)));
            if(((object[])page["records"]).Length!=50)throw new Exception("Viewer page differs");Console.WriteLine("VIEWER PASS boundedImages=50 ms="+watch.ElapsedMilliseconds);
        }
    }
}
