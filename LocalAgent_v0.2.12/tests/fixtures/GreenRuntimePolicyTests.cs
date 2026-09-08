using System;
using VisionQC.LocalAgent.Services;

internal static class GreenRuntimePolicyTests
{
    public enum Mode { None, Basic, Int8 }
    public sealed class Current { public Current() { TensorRTMode = Mode.Basic; } public Mode TensorRTMode { get; set; } }
    public sealed class Legacy { public Legacy() { ProcessWithTrt = true; } public bool ProcessWithTrt { get; set; } }
    public interface IExplicit { Mode TensorRTMode { get; set; } }
    public sealed class Explicit : IExplicit { public Explicit() { ((IExplicit)this).TensorRTMode = Mode.Int8; } Mode IExplicit.TensorRTMode { get; set; } }
    public sealed class ReadOnly { public Mode TensorRTMode { get { return Mode.Basic; } } }
    public sealed class AlreadyOff { public Mode TensorRTMode { get { return Mode.None; } } }
    public sealed class IgnoresWrite { public Mode TensorRTMode { get { return Mode.Basic; } set { } } }
    private static int count;
    private static void Check(bool condition) { if (!condition) throw new Exception("Policy assertion failed"); count++; }
    private static void Reject(object value) { try { GreenRuntimePolicy.DisableTensorRt(value); } catch (InvalidOperationException) { count++; return; } throw new Exception("Expected rejection"); }
    public static int Main()
    {
        var current = new Current();
        Check(GreenRuntimePolicy.Describe(current).Contains("TensorRTMode=Basic"));
        Check(current.TensorRTMode == Mode.Basic);
        Check(GreenRuntimePolicy.DisableTensorRt(current).Contains("Basic -> None"));
        Check(current.TensorRTMode == Mode.None);
        var legacy = new Legacy(); GreenRuntimePolicy.DisableTensorRt(legacy); Check(!legacy.ProcessWithTrt);
        var explicitImpl = new Explicit(); GreenRuntimePolicy.DisableTensorRt(explicitImpl); Check(((IExplicit)explicitImpl).TensorRTMode == Mode.None);
        Check(GreenRuntimePolicy.DisableTensorRt(new AlreadyOff()).Contains("None -> None"));
        Reject(new ReadOnly()); Reject(new IgnoresWrite()); Reject(new object()); Reject(null);
        Console.WriteLine("PASS: " + count + " Green runtime policy assertions");
        return 0;
    }
}
