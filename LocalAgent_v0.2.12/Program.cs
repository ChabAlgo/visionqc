using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent
{
    internal static class Program
    {
        internal const string AgentVersion = "1.3.17";
        internal static VpdlRuntimeCatalog.Installation ActiveVpdlInstallation { get; private set; }
        private static int _requestedExitCode;
        internal static string OriginalProcessPath { get; private set; }

        internal static string AgentHomeDirectory
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VISIONQC_AGENT_HOME");
                return string.IsNullOrWhiteSpace(configured) ? AppDomain.CurrentDomain.BaseDirectory : configured.Trim();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AgentDiagnostics.Initialize(AgentHomeDirectory, "worker", AgentVersion);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromLocalOrVpdInstall;
            OriginalProcessPath = Environment.GetEnvironmentVariable("PATH");
            ConfigureVpdlNativeSearchPath();
            AgentDiagnostics.Write("RUNTIME", "Requested API=" + Environment.GetEnvironmentVariable("VISIONQC_VPDL_API_VERSION") + " | Mode=" + Environment.GetEnvironmentVariable("VISIONQC_VPDL_WORKER_MODE") + " | Studio=" + (ActiveVpdlInstallation == null ? "none" : ActiveVpdlInstallation.StudioDirectory));

            if (ActiveVpdlInstallation == null)
            {
                MessageBox.Show(
                    "현재 Agent 빌드와 호환되는 Cognex VPDL Runtime을 찾지 못했습니다.\r\n\r\n" +
                    "설치된 VPDL의 ViDi.NET.Local.dll 및 bin\\vidi_*.dll 쌍을 확인한 뒤, " +
                    "해당 VPDL 버전용 VisionQC Agent Worker를 사용하세요.",
                    "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

            try
            {
                using (var server = new AgentServer())
                {
                    server.RunUntilExit(openOfflinePage);
                }
            }
            catch (Exception ex)
            {
                WriteStartupFailure(ex);
                _requestedExitCode = VpdlWorkerSelection.StartupFailureExitCode;
            }
            if (_requestedExitCode != 0) Environment.ExitCode = _requestedExitCode;
        }

        private static void WriteStartupFailure(Exception ex)
        {
            try
            {
                string logDirectory = Path.Combine(AgentHomeDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                string active = ActiveVpdlInstallation == null ? "미확인" : ActiveVpdlInstallation.DisplayName;
                string mode = Environment.GetEnvironmentVariable("VISIONQC_VPDL_WORKER_MODE") ?? "exact";
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | VPDL " + active + " | Worker " + mode + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(Path.Combine(logDirectory, "agent-startup.log"), text);
            }
            catch { }
        }

        internal static void RequestWorkerRestart()
        {
            _requestedExitCode = VpdlWorkerSelection.RestartExitCode;
        }

        internal static void RegisterProtocol()
        {
            string launcher = Path.Combine(AgentHomeDirectory, "VisionQC.LocalAgent.exe");
            string exe = File.Exists(launcher) ? launcher : Assembly.GetExecutingAssembly().Location;
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
                string[] candidateDirs = new[] { baseDir, Path.Combine(baseDir, "Cognex"), ActiveVpdlInstallation == null ? "" : ActiveVpdlInstallation.StudioDirectory };
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

        // ViDi.NET은 관리 DLL 외에 같은 API 버전의 VPDL 설치 루트 bin\vidi_*.dll을 동적으로 로드한다.
        // 폴더 존재 순서가 아니라 Agent가 참조한 ViDi.NET.Local API 버전과 일치하는 정상 설치본만 선택한다.
        private static void ConfigureVpdlNativeSearchPath()
        {
            try
            {
                string explicitStudio = Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR");
                string requestedApiVersion = (Environment.GetEnvironmentVariable("VISIONQC_VPDL_API_VERSION") ?? "").Trim();
                Version referencedVersion = Assembly.GetExecutingAssembly()
                    .GetReferencedAssemblies()
                    .Where(item => string.Equals(item.Name, "ViDi.NET.Local", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Version)
                    .FirstOrDefault();
                string apiVersion = requestedApiVersion.Length > 0 ? requestedApiVersion : VpdlRuntimeCatalog.ToApiVersion(referencedVersion);
                ActiveVpdlInstallation = VpdlRuntimeCatalog.Discover(explicitStudio)
                    .FirstOrDefault(item => string.Equals(item.ApiVersion, apiVersion, StringComparison.OrdinalIgnoreCase));
                if (ActiveVpdlInstallation == null) return;

                string nativeBin = ActiveVpdlInstallation.NativeDirectory;
                string service = Path.Combine(ActiveVpdlInstallation.RootDirectory, "Service");
                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                string prefix = string.Join(";", new[] { nativeBin, ActiveVpdlInstallation.StudioDirectory, service }.Where(Directory.Exists));
                // Always put the selected installation first, even if it was already
                // present after another Cognex version in the machine PATH.
                if (!string.IsNullOrWhiteSpace(prefix))
                    Environment.SetEnvironmentVariable("PATH", prefix + ";" + currentPath, EnvironmentVariableTarget.Process);
                // Shared by preload, AI Suggest and Blue/Red/Green/Integrated inference.
                // SDK dependencies stay on PATH, AFTER System32. Never shadow the driver.
                if (!SetDllDirectory(null))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DLL search reset failed");
                AgentDiagnostics.Write("NATIVE_SEARCH", "System-first | Selected SDK PATH=" + prefix);
            }
            catch (Exception ex)
            {
                AgentDiagnostics.Write("NATIVE_SEARCH_ERROR", ex.ToString());
                throw;
            }
        }
    }
}
