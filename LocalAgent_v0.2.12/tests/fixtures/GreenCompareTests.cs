using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using VisionQC.GreenCompare;

internal static class GreenCompareTests
{
    private static int passed;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static int Main(string[] args)
    {
        if (args.Length > 0) {
            string req = args[0] == "--request" ? args[1] : args[0];
            string dir = Path.GetDirectoryName(req);
            File.WriteAllText(Path.Combine(dir, "pid.txt"), Process.GetCurrentProcess().Id.ToString());
            Thread.Sleep(400);
            var cfg = Comparison.Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(req));
            if (Convert.ToString(cfg["TestMode"]) == "crash" && dir.EndsWith("D-current-pinned")) {
                File.WriteAllText(Path.Combine(dir, "last-sdk-failure.txt"), "ViDi2.RuntimeException: 6cc4a157");
                File.WriteAllText(Path.Combine(dir, "last-cleanup-stage.txt"), "BEGIN | Sample.Dispose");
                return unchecked((int)0xc0000409);
            }
            File.WriteAllText(Path.Combine(dir, "result.json"), "{\"TotalImages\":1}");
            return 0;
        }
        string root = Path.Combine(Path.GetTempPath(), "VisionQC-compare-tests-" + Guid.NewGuid().ToString("N"));
        string bins = Path.Combine(root, "Workers", "8.0"), studio = Path.Combine(root, "studio"), run = Path.Combine(root, "logs", "green-runs", "run");
        Directory.CreateDirectory(bins); Directory.CreateDirectory(studio); Directory.CreateDirectory(run);
        string me = Assembly.GetExecutingAssembly().Location;
        File.Copy(me, Path.Combine(bins, "VisionQC.GreenBaseline.exe")); File.Copy(me, Path.Combine(bins, "VisionQC.GreenRunner.exe"));
        File.WriteAllText(Path.Combine(studio, "ViDi.NET.Local.dll"), "test marker; not a real SDK");
        string image = Path.Combine(root, "image.jpg"), workspace = Path.Combine(root, "model.vrws");
        File.WriteAllText(image, "image fixture"); File.WriteAllText(workspace, "workspace fixture");
        var tool = new Dictionary<string, object> { ["Name"]="Crack", ["Enabled"]=true, ["Threshold"]=0.51, ["PositionKeys"]=new[]{"CUSTOM"}, ["UseCaBot"]=true };
        var slot = new Dictionary<string, object> { ["Key"]="CUSTOM", ["DisplayName"]="CA(BOT)", ["WorkspacePath"]=workspace, ["Enabled"]=true, ["StreamName"]="s1" };
        var config = new Dictionary<string, object> { ["WorkspaceSlots"]=new[]{slot}, ["Tools"]=new[]{tool}, ["GpuDevices"]=new[]{1},
            ["UseGpu"]=true, ["OutputRoot"]="unused", ["TestMode"]="ok" };
        var seed = new Dictionary<string, object> { ["ImagePath"]=image, ["SlotKey"]="CUSTOM", ["ApiVersion"]="8.0", ["Studio"]=studio };
        string seedFile = Path.Combine(run, "last-green-input.json"), reqFile=Path.Combine(run,"request.json");
        Action write = () => { File.WriteAllText(seedFile, Comparison.Json().Serialize(seed)); File.WriteAllText(reqFile, Comparison.Json().Serialize(config)); };
        write();
        Check(Comparison.LatestSeed(root)==seedFile, "latest run seed");
        string summary = Comparison.Run(seedFile, root, bins, _=>{}, CancellationToken.None);
        string text=File.ReadAllText(summary);
        Check(text.Split(new[]{"PASS"},StringSplitOptions.None).Length-1==4, "four cases complete");
        string comparisonRoot=Path.GetDirectoryName(summary);
        Check(Directory.GetFiles(Path.Combine(comparisonRoot,"input")).Length==1, "one local image copy");
        Check(File.ReadAllText(image)=="image fixture" && File.ReadAllText(workspace)=="workspace fixture", "source files unchanged");
        string caseRequest=File.ReadAllText(Path.Combine(comparisonRoot,"D-current-pinned","request.json"));
        Check(caseRequest.Contains("\"CA_TOP\"") && caseRequest.Contains("\"PositionKeys\":[\"CA_TOP\"]"), "custom slot mapped identically");
        Check(caseRequest.Contains("\"GpuDevices\":[1]") && caseRequest.Contains("\"Threshold\":0.51") && caseRequest.Contains("\"StreamName\":\"s1\""), "GPU threshold stream preserved");
        Check(caseRequest.Contains("\"CellIdCsvPath\":null") && caseRequest.Contains("\"KeywordMode\":false"), "only diagnostic filters bypassed");
        Check(!Directory.GetFiles(root,"*.db",SearchOption.AllDirectories).Any(), "no history DB write");
        config["TestMode"]="crash"; write();
        summary=Comparison.Run(seedFile, root, bins, _=>{}, CancellationToken.None);
        text=File.ReadAllText(summary);
        Check(text.Contains("FAIL") && text.Contains("0xC0000409") && text.Contains("6cc4a157") && text.Contains("BEGIN | Sample.Dispose"), "native crash retains first SDK error and cleanup");
        using (var cancel=new CancellationTokenSource()) {
            cancel.CancelAfter(100);
            bool stopped=false;
            try { Comparison.Run(seedFile,root,bins,_=>{},cancel.Token); } catch(OperationCanceledException){stopped=true;}
            Check(stopped,"cooperative cancellation");
        }
        bool alive=false;
        foreach(string pidFile in Directory.GetFiles(root,"pid.txt",SearchOption.AllDirectories)) {
            try { using(var child=Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)))) alive |= !child.HasExited; } catch(ArgumentException) {}
        }
        Check(!alive,"no owned child left running");
        seed["ApiVersion"]="../8.0"; write(); bool rejected=false;
        try {Comparison.Run(seedFile,root,bins,_=>{},CancellationToken.None);} catch(InvalidDataException){rejected=true;}
        Check(rejected,"invalid API rejected before executable lookup");
        Console.WriteLine("PASS " + passed + " | " + root);
        return 0;
    }
}
