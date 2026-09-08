using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace VisionQC.LocalAgent.Services
{
    internal static partial class AgentDiagnostics
    {
        private static long _stageSequence;
        internal static string Identity(object value)
        {
            return value == null ? "none" : value.GetType().Name + "#" + RuntimeHelpers.GetHashCode(value);
        }

        internal static T Measure<T>(string stage, string context, bool enabled, Func<T> action)
        {
            if (!enabled) return action();
            long id = Interlocked.Increment(ref _stageSequence);
            string header = "Stage=" + stage + " | Step=" + id + " | PID=" + Process.GetCurrentProcess().Id
                + " | Thread=" + Thread.CurrentThread.ManagedThreadId + " | " + context;
            var watch = Stopwatch.StartNew();
            Write("SDK_BEGIN", header);
            SaveText("last-sdk-stage.txt", DateTime.Now.ToString("O") + " | BEGIN | " + header);
            try
            {
                T result = action();
                string end = header + " | elapsedMs=" + watch.ElapsedMilliseconds;
                Write("SDK_OK", end);
                SaveText("last-sdk-stage.txt", DateTime.Now.ToString("O") + " | OK | " + end);
                return result;
            }
            catch (Exception ex)
            {
                string failure = header + " | elapsedMs=" + watch.ElapsedMilliseconds + Environment.NewLine
                    + ExceptionDetails(ex);
                Write("SDK_FAIL", failure);
                SaveText("last-sdk-stage.txt", DateTime.Now.ToString("O") + " | FAIL | " + failure);
                SaveText("last-sdk-failure.txt", DateTime.Now.ToString("O") + Environment.NewLine + failure
                    + Environment.NewLine + "SDK log directories: " + string.Join("; ", SdkLogDirectories()));
                // Preserve the original exception and original stack. Never retry an inference.
                throw;
            }
        }

        internal static void Measure(string stage, string context, bool enabled, Action action)
        {
            Measure(stage, context, enabled, () => { action(); return true; });
        }

        internal static string ExceptionDetails(Exception error)
        {
            var text = new System.Text.StringBuilder(error.ToString());
            int depth = 0;
            for (Exception item = error; item != null && depth < 10; item = item.InnerException, depth++)
            {
                text.AppendLine().Append("Exception[").Append(depth).Append("] Type=").Append(item.GetType().FullName)
                    .Append(" | HResult=0x").Append(unchecked((uint)item.HResult).ToString("X8"));
                foreach (DictionaryEntry data in item.Data)
                    text.AppendLine().Append("Data.").Append(data.Key).Append('=').Append(data.Value);
            }
            return text.ToString();
        }

        internal static void CaptureEnvironment(string stage)
        {
            // Read-only, bounded diagnostics. No GPU settings, driver modes or files outside logs are changed.
            try
            {
                using (var process = Process.GetCurrentProcess())
                    Write("PROCESS_MEMORY", stage + " | PID=" + process.Id + " | PrivateBytes=" + process.PrivateMemorySize64
                        + " | WorkingSet=" + process.WorkingSet64 + " | ManagedBytes=" + GC.GetTotalMemory(false));
                string environment = string.Join(" | ", new[] { "CUDA_VISIBLE_DEVICES", "CUDA_DEVICE_ORDER",
                    "COGNEX_VPDL_DLL_DIR", "VISIONQC_VPDL_API_VERSION", "VISIONQC_VPDL_WORKER_MODE" }
                    .Select(name => name + "=" + (Environment.GetEnvironmentVariable(name) ?? "<unset>")));
                string inventory = QueryNvidia("--query-gpu=index,uuid,pci.bus_id,name,driver_version,driver_model.current,compute_mode,memory.total,memory.used,memory.free --format=csv");
                string processes = QueryNvidia("--query-compute-apps=gpu_uuid,pid,used_gpu_memory --format=csv");
                string report = DateTime.Now.ToString("O") + " | Stage=" + stage + " | Agent PID=" + Process.GetCurrentProcess().Id
                    + Environment.NewLine + environment + Environment.NewLine + inventory + Environment.NewLine + processes
                    + Environment.NewLine + "GPU inventory/process residency is NOT proof of which GPU executed a particular kernel.";
                Write("GPU_SNAPSHOT", report);
                SaveText("last-gpu-" + (stage == "inference-failure" ? "failure" : "snapshot") + ".txt", report);
                WriteLoadedLibraries();
                WriteSdkLogLocations();
            }
            catch (Exception ex) { Write("DIAGNOSTIC", "Snapshot unavailable: " + ex.Message); }
        }

        internal static string[] SdkLogDirectories()
        {
            string product = Environment.GetEnvironmentVariable("VISIONQC_VPDL_PRODUCT_VERSION");
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cognex Corporation");
            // Cognex writes native/managed logs under Roaming, NOT the worker's current directory.
            var directories = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(product) && product.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                directories.Add(Path.Combine(root, "Cognex VisionPro Deep Learning " + product, "logs"));
            else if (Directory.Exists(root))
                directories.AddRange(Directory.GetDirectories(root, "Cognex VisionPro Deep Learning *").Select(p => Path.Combine(p, "logs")));
            directories.Add(Environment.CurrentDirectory);
            return directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static void WriteSdkLogLocations()
        {
            try
            {
                var text = new System.Text.StringBuilder("Cognex SDK logs remain local. Native .debug.log may be encoded/binary; do not edit it.");
                foreach (string directory in SdkLogDirectories())
                {
                    Write("SDK_LOG_DIRECTORY", directory);
                    text.AppendLine().Append("Directory: ").Append(directory);
                    if (!Directory.Exists(directory)) continue;
                    string prefix = Process.GetCurrentProcess().ProcessName + "_";
                    foreach (string path in Directory.GetFiles(directory, "*.log")
                        .Where(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(File.GetLastWriteTimeUtc).Take(12))
                    {
                        string line = path + " | bytes=" + new FileInfo(path).Length;
                        Write("SDK_LOG_FILE", line);
                        text.AppendLine().Append(line);
                    }
                }
                SaveText("cognex-sdk-log-locations.txt", text.ToString());
            }
            catch (Exception ex) { Write("DIAGNOSTIC", "SDK log location unavailable: " + ex.Message); }
        }

        private static string QueryNvidia(string arguments)
        {
            string executable = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (!File.Exists(executable)) executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (!File.Exists(executable)) return "nvidia-smi unavailable";
            try
            {
                using (var process = new Process { StartInfo = new ProcessStartInfo {
                    FileName = executable, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
                } })
                {
                    process.Start();
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(3000)) { process.Kill(); return "nvidia-smi timeout (3 seconds)"; }
                    return "Query=" + arguments + " | Exit=" + process.ExitCode + Environment.NewLine + output.Result.Trim()
                        + Environment.NewLine + error.Result.Trim();
                }
            }
            catch (Exception ex) { return "nvidia-smi unavailable: " + ex.Message; }
        }
    }
}
