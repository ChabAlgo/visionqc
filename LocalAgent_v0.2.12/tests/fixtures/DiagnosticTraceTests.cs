using System;
using System.IO;
using VisionQC.LocalAgent.Services;

internal static class DiagnosticTraceTests
{
    private sealed class Disposable : IDisposable {
        internal Action Action;
        public void Dispose() { Action(); }
    }
    private static int checks;
    private static void Check(bool value) { if (!value) throw new Exception("Check " + (checks + 1) + " failed"); checks++; }
    public static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "VisionQC-trace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AgentDiagnostics.Initialize(root, "test", "test");
        int calls = 0;
        Check(AgentDiagnostics.Measure("Test.Success", "test", true, () => { calls++; return 42; }) == 42);
        Check(calls == 1);
        Check(File.ReadAllText(Path.Combine(root, "logs", "last-sdk-stage.txt")).Contains("OK"));
        var original = new InvalidOperationException("simulated SDK failure");
        original.Data["NativeCode"] = "test-code";
        try { AgentDiagnostics.Measure<int>("Sample.Process", "GPU=0", true, () => { calls++; throw original; }); }
        catch (InvalidOperationException caught) { Check(object.ReferenceEquals(caught, original)); }
        Check(calls == 2); // No automatic retry.
        string failure = File.ReadAllText(Path.Combine(root, "logs", "last-sdk-failure.txt"));
        Check(failure.Contains("Sample.Process"));
        Check(failure.Contains("HResult=0x"));
        Check(failure.Contains("Data.NativeCode=test-code"));
        Check(failure.Contains("PID=") && failure.Contains("Thread="));
        Check(File.ReadAllText(Path.Combine(root, "logs", "last-sdk-stage.txt")).Contains("FAIL"));
        Check(AgentDiagnostics.Measure("Test.Disabled", "", false, () => 7) == 7);
        Check(File.ReadAllText(Path.Combine(root, "logs", "last-sdk-stage.txt")).Contains("Sample.Process"));
        string run = Path.Combine(root, "run-one");
        AgentDiagnostics.SetRunDirectory(run);
        AgentDiagnostics.SaveText("scope.txt", "scoped");
        Check(File.ReadAllText(Path.Combine(run, "scope.txt")) == "scoped");
        int disposed = 0;
        using (var tracked = AgentDiagnostics.Track(new Disposable { Action = () => disposed++ }, "Sample.Dispose", true))
            Check(tracked.Value != null);
        Check(disposed == 1);
        Check(File.ReadAllText(Path.Combine(run, "last-cleanup-stage.txt")).Contains("OK"));
        try { AgentDiagnostics.Cleanup("Control.Dispose", true, () => { throw new Exception("cleanup error"); }); }
        catch (Exception ex) { Check(ex.Message == "cleanup error"); }
        Check(File.ReadAllText(Path.Combine(run, "last-cleanup-stage.txt")).Contains("FAIL"));
        Check(File.ReadAllText(Path.Combine(root, "logs", "last-sdk-failure.txt")) == failure);
        Console.WriteLine("PASS " + checks + " | " + root);
    }
}
