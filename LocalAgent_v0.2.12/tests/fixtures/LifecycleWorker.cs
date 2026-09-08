using System;
using System.IO;

internal static class LifecycleWorker
{
    private static int Main(string[] args)
    {
        string root = Environment.GetEnvironmentVariable("VISIONQC_AGENT_HOME");
        string record = Path.Combine(root, "worker-starts.txt");
        int starts = File.Exists(record) ? File.ReadAllLines(record).Length : 0;
        File.AppendAllText(record, string.Join(" ", args) + Environment.NewLine);
        Console.WriteLine("SDK_LIFECYCLE_PROBE_" + starts);
        // Exercise a requested version restart and an unexpected native-style exit.
        return starts == 0 ? 74 : starts == 1 ? 42 : 0;
    }
}
