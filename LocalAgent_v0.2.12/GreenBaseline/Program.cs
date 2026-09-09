using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VisionQC.LocalAgent.Services;
using VpdlGreenHeatmapOverlay;

// Only this harness is new. Original/ contains the unchanged user-provided engine.
internal static class BaselineProgram
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string directory);
    private static string folder;
    [ThreadStatic] private static bool recording;
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cognex"),
                Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR") }) {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length != 2) return 2;
        folder = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        Directory.CreateDirectory(folder);
        AppDomain.CurrentDomain.FirstChanceException += FirstChance;
        try {
            string mode = args[1];
            if (mode != "original" && mode != "pinned") throw new ArgumentException("Unknown search mode");
            string studio = Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR");
            string api = Environment.GetEnvironmentVariable("VISIONQC_VPDL_API_VERSION");
            if (mode == "pinned") {
                var installation = VpdlRuntimeCatalog.Discover(studio).First(x => x.ApiVersion == api);
                string prefix = string.Join(";", new[] { installation.NativeDirectory, installation.StudioDirectory,
                    Path.Combine(installation.RootDirectory, "Service") }.Where(Directory.Exists));
                Environment.SetEnvironmentVariable("PATH", prefix + ";" + Environment.GetEnvironmentVariable("PATH"));
                if (!SetDllDirectory(installation.NativeDirectory)) throw new System.ComponentModel.Win32Exception();
            }
            File.WriteAllText(Path.Combine(folder, "engine.txt"),
                "UNMODIFIED DL_Simulation v1.13 engine; harness is not the original full GUI binary."
                + Environment.NewLine + "NativeSearch=" + mode + " | PID=" + System.Diagnostics.Process.GetCurrentProcess().Id
                + Environment.NewLine + "PATH=" + Environment.GetEnvironmentVariable("PATH"));
            return Run(args[0]);
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(folder, "failure.txt"), ex.ToString());
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string request)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };
        var config = json.Deserialize<AppConfig>(File.ReadAllText(request));
        var summary = Task.Run(() => GreenOverlayProcessor.Run(config, new InlineProgress(), CancellationToken.None)).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(folder, "result.json"), json.Serialize(summary));
        return 0;
    }

    private static void FirstChance(object sender, FirstChanceExceptionEventArgs args)
    {
        if (recording || !args.Exception.GetType().FullName.StartsWith("ViDi", StringComparison.Ordinal)) return;
        try {
            recording = true;
            string file = Path.Combine(folder, "first-sdk-failure.txt");
            if (!File.Exists(file)) File.WriteAllText(file, DateTime.Now.ToString("O")
                + " | Thread=" + Thread.CurrentThread.ManagedThreadId + Environment.NewLine + args.Exception);
        } catch { } finally { recording = false; }
    }

    private sealed class InlineProgress : IProgress<ProcessProgress>
    {
        public void Report(ProcessProgress value)
        {
            File.AppendAllText(Path.Combine(folder, "progress.txt"), DateTime.Now.ToString("O") + " | " + value.Message + Environment.NewLine);
        }
    }
}
