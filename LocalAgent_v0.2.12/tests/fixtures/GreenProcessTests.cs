using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using VisionQC.LocalAgent.Services;
using VpdlGreenHeatmapOverlay;

internal static class GreenProcessTests
{
    private static int passed;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        passed++; Console.WriteLine("PASS " + message);
    }
    private sealed class Progress : IProgress<ProcessProgress>
    {
        internal Action<ProcessProgress> Action;
        public void Report(ProcessProgress value) { Action(value); }
    }
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--request") >= 0) return Child(args);
        string home = Path.Combine(Path.GetTempPath(), "VisionQC-pipe-test-" + Guid.NewGuid().ToString("N"));
        AgentDiagnostics.Initialize(home, "pipe-tests", "test");
        var json = GreenProcessMessage.Serializer();
        var summary = new ProcessSummary { TotalImages = 2, TotalOkCount = 1, TotalNgCount = 1,
            FilterCellIdCount = 7, SkippedByCellIdCount = 3, CsvPath = @"C:\출력\검사.csv",
            Elapsed = TimeSpan.FromTicks(1234567) };
        summary.NgCountByTool["Crack"] = 1; summary.CountByJudgement["Damage"] = 1; summary.SlotCsvPaths["CA(TOP)"] = "slot.csv";
        var round = json.Deserialize<GreenProcessResult>(json.Serialize(GreenProcessResult.From(summary))).ToSummary();
        Check(round.TotalImages == 2 && round.Elapsed.Ticks == 1234567 && round.CsvPath == summary.CsvPath, "summary totals paths time roundtrip");
        Check(round.FilterCellIdCount == 7 && round.SkippedByCellIdCount == 3 && round.NgCountByTool["crack"] == 1 && round.CountByJudgement["damage"] == 1 && round.SlotCsvPaths["ca(top)"] == "slot.csv", "all summary fields and case-insensitive lookup preserved");
        string exe = Assembly.GetExecutingAssembly().Location;
        foreach (string mode in new[] { "success", "failed", "crash", "error-crash", "invalid", "cancel", "stuck", "callback" })
        {
            using (var cancel = new CancellationTokenSource())
            {
                int events = 0, pid = 0;
                var progress = new Progress { Action = p => {
                    if (p.Message != null && p.Message.StartsWith("[ORIGINAL]")) return;
                    events++; pid = int.Parse(p.Message);
                    Check(p.LiveRecord.FullPath == @"C:\원본\A.jpg" && p.LiveRecord.ProcessingPath == @"C:\Crop\A.jpg"
                        && p.LiveRecord.Tools["Crack"].Score == null && p.LiveRecord.Tools["Crack"].OverlayPath == @"C:\Overlay\A.jpg", mode + " source/crop/overlay/null score");
                    if (mode == "cancel" || mode == "stuck") cancel.Cancel();
                    if (mode == "callback") throw new InvalidOperationException("callback failed");
                }};
                var clock = Stopwatch.StartNew();
                Exception failure = null; ProcessSummary result = null;
                try { result = GreenProcessHost.Run(new AppConfig { OutputRoot = mode }, progress, cancel.Token, exe, home, Environment.GetEnvironmentVariable("PATH")); }
                catch (Exception ex) { failure = ex; }
                if (mode == "success") Check(failure == null && result.TotalImages == 2 && events == 1, "pipe success and SDK stdout isolation");
                else if (mode == "cancel" || mode == "stuck") Check(failure is OperationCanceledException && clock.Elapsed.TotalSeconds < 22, mode + " bounded owned-child cancellation");
                else if (mode == "failed") Check(failure != null && failure.Message.Contains("6cc4a157"), "SDK error detail retained");
                else if (mode == "crash") Check(failure != null && failure.Message.Contains("0x00000017"), "native exit code retained");
                else if (mode == "error-crash") Check(failure != null && failure.Message.Contains("6cc4a157")
                    && failure.Message.Contains("0xC0000409") && failure.Message.Contains("BEGIN | Sample.Dispose"),
                    "first SDK error and cleanup boundary survive subsequent native crash");
                else if (mode == "invalid") Check(failure is System.IO.InvalidDataException, "duplicate terminal message rejected");
                else Check(failure != null && failure.Message == "callback failed", "callback failure retained");
                bool alive = false; try { using (var p = Process.GetProcessById(pid)) alive = !p.HasExited; } catch (ArgumentException) { }
                Check(!alive, mode + " leaves no owned process");
            }
        }
        Console.WriteLine("PASS " + passed + " assertions; logs=" + home); return 0;
    }
    private static int Child(string[] args)
    {
        var json = GreenProcessMessage.Serializer();
        string mode = json.Deserialize<AppConfig>(File.ReadAllText(args[Array.IndexOf(args, "--request") + 1])).OutputRoot;
        using (var pipe = new NamedPipeClientStream(".", args[Array.IndexOf(args, "--pipe") + 1], PipeDirection.InOut))
        {
            pipe.Connect(10000);
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
            using (var commands = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
            {
                int pid = Process.GetCurrentProcess().Id;
                Action<GreenProcessMessage> send = m => writer.WriteLine(json.Serialize(m));
                send(new GreenProcessMessage { Type = "ready", Pid = pid });
                Console.WriteLine("SDK native stdout is not JSON"); Console.Error.WriteLine("SDK native diagnostic");
                var live = new LiveAnalysisRecord { FullPath = @"C:\원본\A.jpg", ProcessingPath = @"C:\Crop\A.jpg" };
                live.Tools["Crack"] = new LiveToolResult { Tool = "Crack", Score = null, OverlayPath = @"C:\Overlay\A.jpg" };
                send(new GreenProcessMessage { Type = "progress", Progress = new ProcessProgress { Message = pid.ToString(), LiveRecord = live } });
                if (mode == "cancel") { commands.ReadLine(); send(new GreenProcessMessage { Type = "cancelled" }); return 2; }
                if (mode == "stuck" || mode == "callback") { Thread.Sleep(60000); return 3; }
                if (mode == "crash") return 23;
                if (mode == "error-crash") {
                    string directory = Path.GetDirectoryName(args[Array.IndexOf(args, "--request") + 1]);
                    File.WriteAllText(Path.Combine(directory, "last-sdk-failure.txt"), "Sample.Process Cognex internal error (6cc4a157)");
                    File.WriteAllText(Path.Combine(directory, "last-cleanup-stage.txt"), "BEGIN | Sample.Dispose");
                    return unchecked((int)0xC0000409);
                }
                if (mode == "failed") { send(new GreenProcessMessage { Type = "failed", Error = "Cognex internal error (6cc4a157)" }); return 1; }
                var completed = new GreenProcessMessage { Type = "completed", Result = GreenProcessResult.From(new ProcessSummary { TotalImages = 2 }) };
                send(completed); if (mode == "invalid") send(completed);
                return 0;
            }
        }
    }
}
