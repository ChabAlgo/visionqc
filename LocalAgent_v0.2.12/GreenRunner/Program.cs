using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionQC.LocalAgent.Services;
using VpdlGreenHeatmapOverlay;

namespace VisionQC.GreenRunner
{
    internal static class Program
    {
        private static VpdlRuntimeCatalog.Installation _installation;
        private static readonly object SendLock = new object();
        private static StreamWriter _events;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string directory);

        [STAThread]
        private static int Main(string[] args)
        {
            // A fresh EXE/.NET process, not a replacement Control inside the HTTP worker.
            // Restore the original program's native search (server comparison case C).
            // Globally pinning the SDK bin can shadow the system NVIDIA driver DLL.
            // Managed Cognex assemblies still resolve from the selected installation.
            bool searchReset = SetDllDirectory(null);
            string originalPath = Environment.GetEnvironmentVariable("VISIONQC_ORIGINAL_PATH");
            if (originalPath != null) Environment.SetEnvironmentVariable("PATH", originalPath);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string home = Environment.GetEnvironmentVariable("VISIONQC_AGENT_HOME") ?? AppDomain.CurrentDomain.BaseDirectory;
            AgentDiagnostics.Initialize(home, "green-runner", Assembly.GetExecutingAssembly().GetName().Version.ToString());
            using (var cancellation = new CancellationTokenSource())
            {
                NamedPipeClientStream pipe = null;
                try
                {
                    if (!searchReset) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DLL search reset failed");
                    string pipeName = Argument(args, "--pipe");
                    if (pipeName != null)
                    {
                        if (!pipeName.StartsWith("VisionQC.Green.", StringComparison.Ordinal) || pipeName.Length > 100)
                            throw new ArgumentException("Invalid local pipe name");
                        pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        pipe.Connect(30000);
                        _events = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                        var commands = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                        Task.Run(() => {
                            try { while (commands.ReadLine() != null) cancellation.Cancel(); }
                            catch (IOException) { }
                            finally
                            {
                                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                                // Parent exit closes the pipe. A stuck native call must not outlive it forever.
                                Task.Delay(15000).ContinueWith(_ => Environment.Exit(3));
                            }
                        });
                    }
                    Send(new GreenProcessMessage { Type = "ready", Pid = Process.GetCurrentProcess().Id });
                    string requestPath = Path.GetFullPath(Argument(args, "--request") ?? throw new ArgumentException("--request is required"));
                    AgentDiagnostics.SetRunDirectory(Path.GetDirectoryName(requestPath));
                    string diagnosticSearch = Argument(args, "--diagnostic-search");
                    string searchMode = diagnosticSearch ?? "original";
                    if (searchMode != "pinned" && searchMode != "original") throw new ArgumentException("Unknown diagnostic search mode");
                    if (pipeName != null && searchMode != "original") throw new ArgumentException("Pinned search is diagnostic-only; not enabled for production runs");
                    var json = GreenProcessMessage.Serializer();
                    var config = json.Deserialize<AppConfig>(File.ReadAllText(requestPath, Encoding.UTF8));
                    if (config == null) throw new ArgumentException("Empty Green request");
                    string output = Argument(args, "--output");
                    if (output != null) config.OutputRoot = Path.GetFullPath(output);
                    config.OriginalExecution = true;
                    // Original engine policy: use the Workspace's stored settings unchanged.
                    config.DisableTensorRt = false;
                    config.DisableOptimizedGpuMemory = false;
                    string api = Environment.GetEnvironmentVariable("VISIONQC_VPDL_API_VERSION");
                    if (string.IsNullOrWhiteSpace(api)) api = VpdlRuntimeCatalog.ToApiVersion(Assembly.GetExecutingAssembly()
                        .GetReferencedAssemblies().First(x => x.Name == "ViDi.NET.Local").Version);
                    _installation = VpdlRuntimeCatalog.Discover(Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR"))
                        .FirstOrDefault(x => x.ApiVersion == api);
                    if (_installation == null) throw new InvalidOperationException("일치하는 VPDL 설치본을 찾지 못했습니다: API " + api);
                    string nativePrefix = string.Join(";", new[] { _installation.NativeDirectory,
                        _installation.StudioDirectory, Path.Combine(_installation.RootDirectory, "Service") }.Where(Directory.Exists));
                    // PATH is searched AFTER Windows system directories. Supply the selected SDK's
                    // dependencies there (side-by-side 4.0/4.2), never ahead of the system driver.
                    // Explicit A/B/C/D experiments keep their exact original/pinned environments.
                    if (diagnosticSearch == null || searchMode == "pinned")
                        Environment.SetEnvironmentVariable("PATH", nativePrefix + ";" + Environment.GetEnvironmentVariable("PATH"));
                    if (searchMode == "pinned" && !SetDllDirectory(_installation.NativeDirectory))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Selected SDK native search failed");
                    Environment.SetEnvironmentVariable("VISIONQC_VPDL_PRODUCT_VERSION", _installation.ProductVersion);
                    AgentDiagnostics.Write("ORIGINAL_PROCESS", "Fresh process | RequestSHA256=" + Hash(requestPath)
                        + " | ExeSHA256=" + Hash(Assembly.GetExecutingAssembly().Location)
                        + " | API=" + api + " | Studio=" + _installation.StudioDirectory
                        + " | Native=" + _installation.NativeLibraryPath + " | NativeSHA256=" + Hash(_installation.NativeLibraryPath)
                        + " | InputGpuDevices=" + string.Join(",", config.GpuDevices)
                        + " | Cwd=" + Environment.CurrentDirectory + " | NativeSearch=" + searchMode + " | Selected SDK native search pinned=" + (searchMode == "pinned" ? nativePrefix : "not applied (original search)")
                        + " | SelectedSdkPathFallback=" + (diagnosticSearch == null)
                        + " | TensorRT/memory overrides not applied; Workspace is not saved");
                    Send(new GreenProcessMessage { Type = "progress", Progress = new ProcessProgress {
                        Message = "[DLL] Green DLL 검색: " + (searchMode == "original" ? "원본 방식 (C)" : "고정 방식 (비교진단 전용)") + " | VPDL API=" + api } });
                    // Like the original WinForms program: one background MTA execution
                    // creates, loads, processes, and disposes its own Control synchronously.
                    var summary = Task.Run(() => GreenOverlayProcessor.Run(config,
                        new InlineProgress(p => Send(new GreenProcessMessage { Type = "progress", Progress = p })),
                        cancellation.Token)).GetAwaiter().GetResult();
                    var result = GreenProcessResult.From(summary);
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(requestPath), "result.json"), json.Serialize(result), Encoding.UTF8);
                    Send(new GreenProcessMessage { Type = "completed", Result = result });
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    TrySend(new GreenProcessMessage { Type = "cancelled" });
                    return 2;
                }
                catch (Exception ex)
                {
                    string detail = AgentDiagnostics.ExceptionDetails(ex);
                    AgentDiagnostics.Write("ORIGINAL_PROCESS_ERROR", detail);
                    AgentDiagnostics.SaveText("last-green-runner-failure.txt", DateTime.Now.ToString("O") + Environment.NewLine + detail);
                    TrySend(new GreenProcessMessage { Type = "failed", Error = detail });
                    Console.Error.WriteLine(detail);
                    return 1;
                }
                finally
                {
                    AgentDiagnostics.WriteLoadedLibraries();
                    AgentDiagnostics.WriteSdkLogLocations();
                    try { if (_events != null) _events.Dispose(); } catch { }
                    try { if (pipe != null) pipe.Dispose(); } catch { }
                }
            }
        }

        private static string Argument(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException(key + " value is required");
            return args[index + 1];
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name + ".dll";
            // Do not probe other VPDL versions. Preserve the selected installation.
            foreach (string dir in new[] { AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cognex"),
                _installation == null ? Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR") : _installation.StudioDirectory })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        }

        private static void Send(GreenProcessMessage message)
        {
            lock (SendLock)
            {
                if (_events == null) { Console.WriteLine(GreenProcessMessage.Serializer().Serialize(message)); return; }
                try { _events.WriteLine(GreenProcessMessage.Serializer().Serialize(message)); }
                catch (IOException) { throw new OperationCanceledException("Agent connection closed"); }
            }
        }

        private static void TrySend(GreenProcessMessage message)
        {
            try { Send(message); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        }

        private sealed class InlineProgress : IProgress<ProcessProgress>
        {
            private readonly Action<ProcessProgress> _action;
            internal InlineProgress(Action<ProcessProgress> action) { _action = action; }
            public void Report(ProcessProgress value) { _action(value); }
        }
    }
}
