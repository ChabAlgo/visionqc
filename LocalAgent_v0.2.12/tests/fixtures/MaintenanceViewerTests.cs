using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using VisionQC.LocalAgent;
using VisionQC.LocalAgent.Services;
using VisionQC.LocalAgent.Persistence;

class MaintenanceViewerTests
{
    static JavaScriptSerializer json = new JavaScriptSerializer();
    static void Check(bool condition,string reason) { if(!condition)throw new Exception(reason); }
    static Dictionary<string,object> Obj(object value) { return (Dictionary<string,object>)json.DeserializeObject(json.Serialize(value)); }
    static string Write(string root,string relative,int days=40) {
        string path=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"synthetic");File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-days));return path;
    }
    static void Main(string[] args) { try { Run(args[0]); } catch(Exception e) {Console.WriteLine(e);Environment.Exit(1);} }
    static void Run(string root) {
        Directory.CreateDirectory(root);string home=Path.Combine(root,"agent");bool waiting=false;
        var service=new LogMaintenance(home,()=>waiting);
        Check(Convert.ToInt32(Obj(Obj(service.Status())["settings"])["retentionDays"])==30,"Default must be 30 days");
        string old=Write(home,"logs/agent-worker-9999999.log"),recent=Write(home,"logs/agent-worker-9999998.log",2);
        string active=Write(home,"logs/agent-worker-"+Process.GetCurrentProcess().Id+".log");
        string locked=Write(home,"logs/agent-locked.log"),readOnly=Write(home,"logs/agent-readonly.log");File.SetAttributes(readOnly,FileAttributes.ReadOnly);
        var protectedPaths=new[]{Write(home,"logs/results.csv"),Write(home,"logs/history.sqlite"),Write(home,"logs/image.png"),Write(home,"logs/workspace.vrws"),Write(home,"data/agent-old.log"),Write(home,"output/agent-old.log"),Write(home,"logs/unknown/agent-old.log"),Write(home,"logs/green-runs/unknown/request.json"),Write(home,"logs/green-runs/"+Guid.NewGuid().ToString("N")+"/user.json")};
        string run=Guid.NewGuid().ToString("N"),request=Write(home,"logs/green-runs/"+run+"/request.json"),failure=Write(home,"logs/runtime-failures/20260101-120000-1234567-"+Guid.NewGuid().ToString("N")+"/worker-agent-worker.log");
        Dictionary<string,object> plan;
        using(var stream=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None)) {
            plan=Obj(service.Preview());Check(Convert.ToInt32(plan["reclaimableFiles"])==3,"Scope/age/active/locked/readonly filter failed");
            File.AppendAllText(old,"changed after preview");
            var result=Obj(service.Clean(json.Serialize(new{planId=plan["planId"]})));
            Check(Convert.ToInt32(result["deletedFiles"])==2 && Convert.ToInt32(result["skippedFiles"])==1,"Changed-file protection failed");
        }
        Check(File.Exists(old)&&File.Exists(recent)&&File.Exists(active)&&File.Exists(locked)&&File.Exists(readOnly),"Protected logs deleted");
        Check(!File.Exists(request)&&!File.Exists(failure)&&protectedPaths.All(File.Exists),"Diagnostic cleanup modified user data");
        bool rejected=false;try{service.Clean(json.Serialize(new{planId=plan["planId"]}));}catch(InvalidOperationException){rejected=true;}Check(rejected,"Replayed plan accepted");
        rejected=false;try{service.Configure("{\"retentionDays\":0,\"maxMegabytes\":1}");}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid settings accepted");
        service.Configure("{\"automatic\":false,\"retentionDays\":14,\"maxMegabytes\":64}");
        Check(Convert.ToInt32(Obj(Obj(new LogMaintenance(home).Status())["settings"])["retentionDays"])==14,"Policy not persisted");
        typeof(LogMaintenance).GetField("_nextAutomatic",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(service,DateTime.UtcNow.AddSeconds(-1));
        Check(!service.AutomaticDue,"Disabled automatic policy ignored");
        waiting=true;rejected=false;try{service.Preview();}catch(OperationCanceledException){rejected=true;}Check(rejected,"Inspection did not preempt scan");waiting=false;
        plan=Obj(service.Preview());waiting=true;var paused=Obj(service.Clean(json.Serialize(new{planId=plan["planId"]})));Check(Convert.ToInt32(paused["deletedFiles"])==0,"Inspection did not preempt deletion");waiting=false;
        string outside=Path.Combine(root,"outside");Write(outside,"agent-old.log");
        var psi=new ProcessStartInfo("cmd.exe","/c mklink /J \""+Path.Combine(home,"logs/cognex")+"\" \""+outside+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
        using(var process=Process.Start(psi)){process.StandardOutput.ReadToEnd();process.WaitForExit();Check(process.ExitCode==0,"Junction fixture failed");}
        plan=Obj(service.Preview());service.Clean(json.Serialize(new{planId=plan["planId"]}));Check(File.Exists(Path.Combine(outside,"agent-old.log")),"Junction escaped managed logs");
        string capHome=Path.Combine(root,"capacity"),big=Write(capHome,"logs/agent-worker-9999999.log",1);
        using(var file=new FileStream(big,FileMode.Open,FileAccess.Write))file.SetLength(65L*1024*1024);
        var cap=new LogMaintenance(capHome);cap.Configure("{\"automatic\":true,\"retentionDays\":30,\"maxMegabytes\":64}");
        plan=Obj(cap.Preview());Check(Convert.ToInt32(plan["reclaimableFiles"])==1,"Capacity limit ignored");cap.Clean(json.Serialize(new{planId=plan["planId"]}));Check(!File.Exists(big),"Capacity cleanup failed");
        string autoOld=Write(capHome,"logs/agent-old.log");typeof(LogMaintenance).GetField("_nextAutomatic",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(cap,DateTime.UtcNow.AddSeconds(-1));
        Check(cap.AutomaticDue,"Automatic due date ignored");cap.Automatic();Check(!File.Exists(autoOld)&&!cap.AutomaticDue,"Automatic cleanup or daily scheduling failed");
        Write(capHome,"logs/agent-expiry.log");plan=Obj(cap.Preview());typeof(LogMaintenance).GetField("_expires",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(cap,DateTime.UtcNow.AddSeconds(-1));
        rejected=false;try{cap.Clean(json.Serialize(new{planId=plan["planId"]}));}catch(InvalidOperationException){rejected=true;}Check(rejected,"Expired deletion plan accepted");
        Console.WriteLine("PASS native maintenance: defaults, persistence, age/capacity, protected data, live PID, lock, readonly, changed/replayed plans, work preemption and junction safety");

        string input=Path.Combine(root,"input.csv");var lines=new List<string>{"Date,Time,Cell ID,Position,Total_result,FullPath,Crack_result,Crack_score,Other_result,Other_score"};
        for(int i=0;i<121;i++)lines.Add("2026-02-03,12:00:00,P163GG22M21"+i.ToString("00000")+",AN(TOP),NG,C:\\Exact\\"+i+".jpg,NG,.9,OK,.1");
        lines.Add("2026-02-03,12:00:00,P163GG22M2100000,AN(TOP),NG,C:\\Exact\\heatmap.jpg,OK,.99,NG,.99");
        lines.Add("2026-02-03,12:00:00,P163GG22M2100000,AN(TOP),NG,C:\\Exact\\below.jpg,NG,.6,OK,.1");
        lines.Add("2026-02-04,12:00:00,P163GG22M2100000,AN(TOP),NG,C:\\Exact\\other-day.jpg,NG,.9,OK,.1");
        lines.Add("2026-02-03,12:00:00,P163GG22M2100000,CA(TOP),NG,C:\\Exact\\other-position.jpg,NG,.9,OK,.1");
        File.WriteAllLines(input,lines);
        using(var store=new SqliteRunStore(Path.Combine(root,"source.sqlite"))) {
            string runId;new CsvHistoryFileImporter(store,json).ImportFiles(new AgentHistoryFileImportRequest(),new[]{input},null,CancellationToken.None,out runId);
            using(var projection=new AnalysisProjection(store.DatabasePath,Path.Combine(root,"cache.sqlite"),runId)) {
                while(projection.Advance(CancellationToken.None)){}
                projection.SetThresholds(new[]{new AnalysisProjection.ThresholdValue{position="AN(TOP)",tool="Crack",value=.8}},CancellationToken.None);
                var paths=new List<string>();long after=0;Dictionary<string,object> second=null;
                do {var page=Obj(projection.ToolImages("2026-02-03","AN(TOP)","Crack",after,50,false,CancellationToken.None));var rows=(object[])page["records"];Check(rows.Length<=50,"Unbounded viewer response");paths.AddRange(rows.Select(row=>Convert.ToString(((Dictionary<string,object>)row)["fullPath"])));if(after>0&&second==null)second=page;after=Convert.ToInt64(page["lastId"]);if(!(bool)page["hasMore"])break;}while(true);
                Check(paths.Count==121&&paths.Distinct().Count()==121&&!paths.Any(p=>p.Contains("heatmap")||p.Contains("below")||p.Contains("other-")),"Tool/date/Position/threshold path mismatch");
                var previous=Obj(projection.ToolImages("2026-02-03","AN(TOP)","Crack",Convert.ToInt64(second["firstId"]),50,true,CancellationToken.None));Check(((object[])previous["records"]).Length==50&&!(bool)previous["hasPrevious"],"Reverse cursor skipped first page");
                var exports=new List<string>();projection.ExportSelection(root,1000000,false,"2026-02-03",new[]{"AN(TOP)"},"Crack",CancellationToken.None,null,(path,pos)=>exports.Add(path));Check(exports.OrderBy(p=>p).SequenceEqual(paths.OrderBy(p=>p)),"Viewer differs from Tool image copy selection");
                var none=Obj(projection.ToolImages("2026-02-05","AN(TOP)","Crack",0,50,false,CancellationToken.None));Check(((object[])none["records"]).Length==0,"Empty date leaks images");
                Console.WriteLine("PASS native Tool viewer: 121 images, bounded forward/reverse cursors, date/Position/threshold isolation, exact inspection paths and image-copy parity");
            }
        }
    }
}
