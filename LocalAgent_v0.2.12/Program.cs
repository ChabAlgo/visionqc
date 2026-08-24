using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace VisionQC.LocalAgent
{
    internal static class Program
    {
        internal const string AgentVersion = "1.2.3";
        private static readonly string VpdlStudioDirectory = FindVpdlStudioDirectory();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ConfigureVpdlNativeSearchPath();
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromLocalOrVpdInstall;

            bool openOfflinePage = args != null && args.Any(arg => string.Equals(arg, "--offline", StringComparison.OrdinalIgnoreCase));
            if (args != null && args.Length > 0)
            {
                if (string.Equals(args[0], "--register", StringComparison.OrdinalIgnoreCase))
                {
                    RegisterProtocol();
                    Console.WriteLine("visionqc-agent:// protocol registered.");
                    return;
                }
                if (string.Equals(args[0], "--unregister", StringComparison.OrdinalIgnoreCase))
                {
                    UnregisterProtocol();
                    Console.WriteLine("visionqc-agent:// protocol unregistered.");
                    return;
                }
            }

            using (var server = new AgentServer())
            {
                server.RunUntilExit(openOfflinePage);
            }
        }

        internal static void RegisterProtocol()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\visionqc-agent"))
            {
                key.SetValue("", "URL:VisionQC Local Agent");
                key.SetValue("URL Protocol", "");
                using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                using (var cmd = key.CreateSubKey(@"shell\open\command")) cmd.SetValue("", "\"" + exe + "\" \"%1\"");
            }
        }

        internal static void UnregisterProtocol()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\visionqc-agent", false); } catch { }
        }

        private static Assembly ResolveAssemblyFromLocalOrVpdInstall(object sender, ResolveEventArgs args)
        {
            try
            {
                string dllName = new AssemblyName(args.Name).Name + ".dll";
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidateDirs = new[] { baseDir, Path.Combine(baseDir, "Cognex"), VpdlStudioDirectory };
                foreach (string dir in candidateDirs)
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string path = Path.Combine(dir, dllName);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
            }
            catch { }
            return null;
        }

        // ViDi.NET은 관리 DLL 외에 VPDL 설치 루트의 bin\vidi_*.dll을 동적으로 로드한다.
        // 설치 EXE의 AppData 경로에서는 Cognex Studio의 PATH를 상속하지 않을 수 있으므로 시작 전에 명시한다.
        private static void ConfigureVpdlNativeSearchPath()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(VpdlStudioDirectory)) return;
                string root = Directory.GetParent(VpdlStudioDirectory).FullName;
                string nativeBin = Path.Combine(root, "bin");
                string service = Path.Combine(root, "Service");
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                string prefix = string.Join(";", new[] { nativeBin, VpdlStudioDirectory, service }.Where(Directory.Exists));
                if (!string.IsNullOrWhiteSpace(prefix) && currentPath.IndexOf(nativeBin, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", prefix + ";" + currentPath, EnvironmentVariableTarget.Process);
                if (Directory.Exists(nativeBin)) SetDllDirectory(nativeBin);
            }
            catch { }
        }

        private static string FindVpdlStudioDirectory()
        {
            string environmentDirectory = Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR");
            string[] candidates = new[]
            {
                environmentDirectory,
                @"C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio",
                @"C:\Program Files\Cognex\VisionPro Deep Learning\4.1\Cognex Deep Learning Studio",
                @"C:\Program Files\Cognex\VisionPro Deep Learning\4.2\Cognex Deep Learning Studio",
                @"C:\Program Files\Cognex\VisionPro Deep Learning\5.0\Cognex Deep Learning Studio"
            };
            return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "ViDi.NET.Local.dll"))) ?? "";
        }
    }
}
