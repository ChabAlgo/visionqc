using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionQC.LocalAgent.Services
{
    // Diagnostics remain local. A native crash cannot run catch/finally, so record the
    // active operation before entering the SDK, independently of browser event delivery.
    internal static partial class AgentDiagnostics
    {
        private static readonly object Sync = new object();
        private static string _directory;
        private static string _logPath;
        private static string _operation;
        private static string _runDirectory;

        internal static void SetRunDirectory(string directory)
        {
            _runDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(_runDirectory);
        }
        private const long MaxLogBytes = 8 * 1024 * 1024;

        internal static void Initialize(string agentHome, string role, string version)
        {
            try
            {
                _directory = Path.Combine(agentHome, "logs");
                Directory.CreateDirectory(_directory);
                _logPath = Path.Combine(_directory, "agent-" + role + "-" + Process.GetCurrentProcess().Id + ".log");
                Write("START", "Agent " + version + " | " + role + " | OS=" + Environment.OSVersion + " | x64=" + Environment.Is64BitProcess);
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                    Write("FATAL", "Terminating=" + args.IsTerminating + " | Last operation=" + _operation + Environment.NewLine + args.ExceptionObject);
            }
            catch { }
        }

        internal static void Write(string level, string message)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                lock (Sync)
                {
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > MaxLogBytes)
                    {
                        File.Copy(_logPath, _logPath + ".previous", true);
                        File.WriteAllText(_logPath, "", Encoding.UTF8);
                    }
                    File.AppendAllText(_logPath, DateTime.Now.ToString("O") + " | PID=" + Process.GetCurrentProcess().Id + " | Thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId + " | " + level + " | " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* Diagnostics must not stop an inspection. */ }
        }

        internal static void Operation(string operation)
        {
            _operation = operation;
            SaveText("last-operation.txt", DateTime.Now.ToString("O") + " | PID=" + Process.GetCurrentProcess().Id + " | " + operation);
        }

        internal static void PreserveWorkerFailure(string workerHome, string context, Exception error)
        {
            if (string.IsNullOrEmpty(_directory)) return;
            try
            {
                string root = Path.Combine(_directory, "runtime-failures");
                Directory.CreateDirectory(root);
                string target = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "failure.txt"), context + Environment.NewLine + error, Encoding.UTF8);
                foreach (string sourceRoot in new[] { workerHome, Path.Combine(workerHome, "logs") })
                {
                    if (!Directory.Exists(sourceRoot)) continue;
                    foreach (string source in Directory.GetFiles(sourceRoot))
                    {
                        string extension = Path.GetExtension(source);
                        if (!extension.Equals(".log", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                        // Bound diagnostics independently of model/image size; retain the end of each log.
                        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var output = File.Create(Path.Combine(target, (sourceRoot == workerHome ? "native-" : "worker-") + Path.GetFileName(source))))
                        { if (input.Length > MaxLogBytes) input.Position = input.Length - MaxLogBytes; input.CopyTo(output); }
                    }
                }
                // This dedicated folder contains only archives created above; never prune user logs/data.
                foreach (var old in new DirectoryInfo(root).GetDirectories().OrderByDescending(item => item.Name).Skip(12)) old.Delete(true);
                Write("PRELOAD_DIAGNOSTICS", context + " | Saved=" + target);
            }
            catch (Exception ex) { Write("PRELOAD_DIAGNOSTICS_ERROR", ex.ToString()); }
        }

        internal static void SaveText(string fileName, string text)
        {
            if (string.IsNullOrEmpty(_directory)) return;
            try { lock (Sync) {
                File.WriteAllText(Path.Combine(_directory, Path.GetFileName(fileName)), text ?? "", Encoding.UTF8);
                if (!string.IsNullOrEmpty(_runDirectory))
                    File.WriteAllText(Path.Combine(_runDirectory, Path.GetFileName(fileName)), text ?? "", Encoding.UTF8);
            } }
            catch { }
        }

        internal static void WriteLoadedLibraries()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("ViDi", StringComparison.OrdinalIgnoreCase)))
                    Write("DLL", assembly.FullName + " | " + assembly.Location);
                using (var process = Process.GetCurrentProcess())
                    foreach (ProcessModule module in process.Modules)
                        if (new[] { "vidi_", "nvcuda", "cudart", "cublas", "cudnn", "nvinfer", "nvrtc", "mkl", "libiomp" }
                            .Any(prefix => module.ModuleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                            Write("NATIVE", module.ModuleName + " | " + module.FileVersionInfo.FileVersion + " | " + module.FileName);
            }
            catch (Exception ex) { Write("DIAGNOSTIC", ex.Message); }
        }

        internal static void WriteNvidiaEnvironment()
        {
            try
            {
                string executable = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (!File.Exists(executable)) executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
                if (!File.Exists(executable)) { Write("GPU_DRIVER", "nvidia-smi unavailable; see Windows driver record"); return; }
                using (var process = new Process { StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--query-gpu=name,driver_version,memory.total,index,memory.used,memory.free,driver_model.current --format=csv",
                    UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true
                } })
                {
                    process.Start();
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(3000)) { process.Kill(); Write("GPU_DRIVER", "nvidia-smi timeout"); return; }
                    Write("NVIDIA", output.Result.Trim() + " | " + error.Result.Trim());
                }
            }
            catch (Exception ex) { Write("GPU_DRIVER", ex.Message); }
        }
    }
}
