using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent.Services;
using VisionQC.LocalAgent.Persistence;
using VisionQC.LocalAgent;
class IndependentExportTests
{
    static JavaScriptSerializer codec=new JavaScriptSerializer();
    static string Json(object value){return codec.Serialize(value);}
    static Dictionary<string,object> Obj(object value){return (Dictionary<string,object>)codec.DeserializeObject(Json(value));}
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static Dictionary<string,object> Finish(AnalysisService service,object started,bool export=false){var status=Obj(started);var clock=Stopwatch.StartNew();while((bool)status["running"]){if(clock.ElapsedMilliseconds>10000)throw new Exception("Fixture operation timed out");Thread.Sleep(5);status=Obj(export?service.ExportStatus(Json(new{analysisId=status["analysisId"],exportId=status["exportId"]})):service.Status(Json(new{analysisId=status["analysisId"]})));}Check((bool)status["ok"]&&(bool)status["completed"],Json(status));return status;}
    static void Main(string[] args){try{Run(args[0]);}catch(Exception error){Console.WriteLine(error);Environment.Exit(1);}}
    static void Run(string root){
        Directory.CreateDirectory(root);
        foreach(bool cancel in new[]{false,true}){
            string trial=Path.Combine(root,cancel?"cancel":"complete");Directory.CreateDirectory(trial);string output=Path.Combine(trial,"output");Directory.CreateDirectory(output);
            string source=Path.Combine(trial,"original.jpg");File.WriteAllText(source,"synthetic inspection original");
            string input=Path.Combine(trial,"input.csv");File.WriteAllText(input,"Date,Time,Cell ID,Position,Total_result,Crack_result,Crack_score,FullPath\n2026-02-03,12:00:00,P163GG22M2100001,AN(TOP),NG,NG,.9,"+source);
            using(var gate=new ManualResetEvent(false))using(var connected=new ManualResetEvent(false))using(var service=new AnalysisService(Path.Combine(trial,"analysis"))){
                // Hold the copy worker at the transfer boundary to model slow I/O without a real network share.
                service.ExportCopyStarting=_=>{connected.Set();gate.WaitOne();};
                try{
                    var imported=Finish(service,service.StartImport(Json(new{filePaths=new[]{input}})));string id=(string)imported["analysisId"];
                    var clock=Stopwatch.StartNew();var started=Obj(service.Export(Json(new{analysisId=id,independent=true,kind="tool-ng",position="AN(TOP)",tool="Crack",copyImages=true,outputDirectory=output,maxRows=1000})));
                    Check(clock.ElapsedMilliseconds<2000&&started.ContainsKey("exportId"),"Export did not return a separate task promptly");
                    Check(connected.WaitOne(5000),"Controlled transfer gate was not reached: "+Json(service.ExportStatus(Json(new{analysisId=id,exportId=started["exportId"]}))));
                    var export=Obj(service.ExportStatus(Json(new{analysisId=id,exportId=started["exportId"]})));Check((bool)export["running"]&&(string)export["phase"]=="copying","Expected a copying export");
                    var status=Obj(service.Status(Json(new{analysisId=id})));Check(!(bool)status["running"]&&(bool)status["completed"],"Export blocked the main analysis task");
                    clock.Restart();var dates=Obj(service.Dates(Json(new{analysisId=id,positions=new[]{"AN(TOP)"}})));Check(dates.ContainsKey("page")&&clock.ElapsedMilliseconds<1000,"Date query was blocked by slow copy");
                    var dashboard=Finish(service,service.Dashboard(Json(new{analysisId=id})));Check(dashboard["result"]!=null,"Dashboard unavailable during copy");
                    Finish(service,service.Thresholds(Json(new{analysisId=id,thresholds=new[]{new{position="AN(TOP)",tool="Crack",value=1.0}}})));
                    var duplicate=Obj(service.Export(Json(new{analysisId=id,independent=true,kind="all",outputDirectory=output,maxRows=1000})));Check(!(bool)duplicate["ok"]&&(bool)duplicate["busy"],"Overlapping exports allowed unbounded I/O");
                    if(cancel){service.ExportStatus(Json(new{analysisId=id,exportId=started["exportId"]}),true);Check(!(bool)Obj(service.Status(Json(new{analysisId=id})))["running"],"Export cancellation cancelled the dashboard");}
                    gate.Set();var completed=Finish(service,started,true);var data=(Dictionary<string,object>)completed["result"];var images=(Dictionary<string,object>)data["images"];
                    Check(Convert.ToInt64(data["count"])==1,"Changing live threshold changed the frozen CSV selection");
                    Check((bool)images["cancelled"]==cancel&&Convert.ToInt64(images["copied"])==(cancel?0:1),"Copy/cancel result mismatch");
                    Check(!Directory.GetFiles(output,"*.partial",SearchOption.AllDirectories).Any(),"Partial file leaked");
                    Check(!Directory.GetFiles(output,".copy-queue*",SearchOption.AllDirectories).Any(),"Queue was created on output storage");
                    Check(!Directory.GetFiles(Path.Combine(trial,"analysis"),".copy-queue*",SearchOption.AllDirectories).Any(),"Local queue leaked");
                    Check(File.Exists(input)&&File.ReadAllText(source)=="synthetic inspection original","Original input changed");
                    Console.WriteLine("PASS independent export "+(cancel?"cancel":"complete")+": isolated task, dashboard/date/threshold access during controlled slow I/O, frozen conditions, local queue and partial cleanup");
                }finally{gate.Set();}
            }
        }
        string snapshotRoot=Path.Combine(root,"snapshot");Directory.CreateDirectory(snapshotRoot);
        string csv=Path.Combine(snapshotRoot,"input.csv");File.WriteAllText(csv,"Date,Time,Cell ID,Position,Total_result,Crack_result,Crack_score,FullPath\n2026-02-03,12:00:00,P163GG22M2100001,AN(TOP),NG,NG,.9,C:\\synthetic-one.jpg\n2026-02-04,12:00:00,P163GG22M2100002,AN(TOP),NG,NG,.6,C:\\synthetic-two.jpg");
        using(var store=new SqliteRunStore(Path.Combine(snapshotRoot,"source.sqlite"))){
            string run;new CsvHistoryFileImporter(store,codec).ImportFiles(new AgentHistoryFileImportRequest(),new[]{csv},null,CancellationToken.None,out run);
            using(var live=new AnalysisProjection(store.DatabasePath,Path.Combine(snapshotRoot,"cache.sqlite"),run)){
                while(live.Advance(CancellationToken.None)){}
                live.ConfigurePositions(new List<PositionAliasDefinition>{new PositionAliasDefinition{name="TAN",aliases=new List<string>{"AN(TOP)"}}},CancellationToken.None);
                using(var snapshot=live.OpenExportReader()){
                    live.SetThresholds(new[]{new AnalysisProjection.ThresholdValue{position="TAN",tool="Crack",value=1.0}},CancellationToken.None);
                    var frozen=Obj(snapshot.ExportSelection(snapshotRoot,1,true,"",new[]{"TAN"},"Crack",CancellationToken.None,"frozen"));
                    Check(Convert.ToInt64(frozen["count"])==2&&((object[])frozen["files"]).Length==2,"Snapshot lost alias/threshold/date splitting consistency");
                    var updated=Obj(live.ExportSelection(snapshotRoot,100,false,"",new[]{"TAN"},"Crack",CancellationToken.None,"updated"));Check(Convert.ToInt64(updated["count"])==0,"Live threshold did not update independently");
                    Check(Convert.ToInt64(Obj(snapshot.ExportRaw(snapshotRoot,100,false,CancellationToken.None,null,"raw"))["count"])==2,"Read-only raw export failed");
                    Check(Convert.ToInt64(Obj(snapshot.ExportFiltered(snapshotRoot,100,false,"score","","TAN","Crack","TOOL_NG",false,.8,"gte",.8,CancellationToken.None,"score"))["count"])==1,"Read-only score export failed");
                    Check(Convert.ToInt64(Obj(snapshot.ExportSelection(snapshotRoot,100,false,"",new string[0],"Crack",CancellationToken.None,"empty"))["count"])==0,"Empty Position selection leaked rows");
                }
                Check(store.ReadSimulationResultPage(run,0,10).records.All(row=>row.Position=="AN(TOP)"),"Snapshot modified original Position data");
            }
        }
        Console.WriteLine("PASS read-only export snapshots: concurrent threshold writes, aliases, date splitting, raw/score/empty exports and original-data preservation");
    }
}
