using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent.Launcher
{
    internal static class Program
    {
        private const string LauncherVersion = "1.3.10";

        [STAThread]
        private static void Main(string[] args)
        {
            bool ownsMutex;
            using (var mutex = new Mutex(true, "Local\\VisionQC.LocalAgent.Launcher", out ownsMutex))
            {
                if (!ownsMutex) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (args != null && args.Any(arg => string.Equals(arg, "--register", StringComparison.OrdinalIgnoreCase)))
                {
                    RegisterProtocol();
                    return;
                }
                if (args != null && args.Any(arg => string.Equals(arg, "--unregister", StringComparison.OrdinalIgnoreCase)))
                {
                    UnregisterProtocol();
                    return;
                }

                string selected = ValueAfter(args, "--vpdl") ?? VpdlWorkerSelection.Read();
                bool offline = args != null && args.Any(arg => string.Equals(arg, "--offline", StringComparison.OrdinalIgnoreCase));
                int delayMilliseconds;
                int.TryParse(ValueAfter(args, "--delay"), out delayMilliseconds);
                if (delayMilliseconds > 0) Thread.Sleep(Math.Min(5000, delayMilliseconds));
                RunWorker(selected, offline);
            }
        }

        private static void RunWorker(string selected, bool offline)
        {
            AgentDiagnostics.Initialize(AppDomain.CurrentDomain.BaseDirectory, "launcher", LauncherVersion);
            string nativeLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "cognex");
            Directory.CreateDirectory(nativeLogDirectory);
            int crashRestarts = 0;
            while (true)
            {
                var installation = ResolveInstallation(selected);
                bool universalWorker;
                bool vpdlAvailable = installation != null;
                string worker = vpdlAvailable
                    ? VpdlWorkerLocator.Resolve(AppDomain.CurrentDomain.BaseDirectory, installation.ApiVersion, out universalWorker)
                    : VpdlWorkerLocator.ResolveWithoutVpdl(AppDomain.CurrentDomain.BaseDirectory, out universalWorker);
                if (string.IsNullOrWhiteSpace(worker))
                {
                    string message = vpdlAvailable
                        ? "VPDL " + installation.ProductVersion + " (API " + installation.ApiVersion + ")용 Worker가 설치되어 있지 않습니다.\r\n\r\n정확 버전 Worker 또는 Universal Worker가 포함된 VisionQC 설치 프로그램을 사용하세요."
                        : "VPDL 미설치 모드로 실행할 Core Worker가 없습니다.\r\n\r\n최신 VisionQC 설치 프로그램을 다시 설치하세요.";
                    MessageBox.Show(message,
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (vpdlAvailable) VpdlWorkerSelection.Write(installation.ApiVersion);
                var startInfo = new ProcessStartInfo
                {
                    FileName = worker,
                    Arguments = offline ? "--worker --offline" : "--worker",
                    WorkingDirectory = nativeLogDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.EnvironmentVariables["VISIONQC_AGENT_HOME"] = AppDomain.CurrentDomain.BaseDirectory;
                startInfo.EnvironmentVariables["VISIONQC_VPDL_AVAILABLE"] = vpdlAvailable ? "true" : "false";
                startInfo.EnvironmentVariables["VISIONQC_VPDL_API_VERSION"] = vpdlAvailable ? installation.ApiVersion : "";
                startInfo.EnvironmentVariables["VISIONQC_VPDL_PRODUCT_VERSION"] = vpdlAvailable ? installation.ProductVersion : "";
                startInfo.EnvironmentVariables["COGNEX_VPDL_DLL_DIR"] = vpdlAvailable ? installation.StudioDirectory : "";
                startInfo.EnvironmentVariables["VISIONQC_VPDL_WORKER_MODE"] = vpdlAvailable ? (universalWorker ? "universal" : "exact") : "none";
                AgentDiagnostics.Write("WORKER", worker + " | Selected=" + (vpdlAvailable ? installation.DisplayName : "none") + " | Mode=" + startInfo.EnvironmentVariables["VISIONQC_VPDL_WORKER_MODE"]);
                int exitCode;
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (sender, item) => { if (item.Data != null) AgentDiagnostics.Write("SDK", item.Data); };
                    process.ErrorDataReceived += (sender, item) => { if (item.Data != null) AgentDiagnostics.Write("SDK_ERROR", item.Data); };
                    process.Start();
                    // Opening the offline browser is a launch action, not a recovery action.
                    offline = false;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                AgentDiagnostics.Write("EXIT", "Worker exit=" + exitCode + " (0x" + unchecked((uint)exitCode).ToString("X8") + ")");

                if (exitCode == VpdlWorkerSelection.RestartExitCode)
                {
                    selected = VpdlWorkerSelection.Read();
                    crashRestarts = 0;
                    continue;
                }
                if (exitCode == VpdlWorkerSelection.StartupFailureExitCode)
                {
                    MessageBox.Show("VisionQC Worker 시작에 실패했습니다.\r\n\r\n" +
                        "선택된 VPDL: " + (vpdlAvailable ? installation.DisplayName : "미설치 모드") + "\r\n" +
                        "Worker 방식: " + (vpdlAvailable ? (universalWorker ? "Universal" : "Exact") : "No VPDL") + "\r\n" +
                        "로그: " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "agent-startup.log"),
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (exitCode == 0) return;

                crashRestarts++;
                if (crashRestarts >= 3)
                {
                    MessageBox.Show("VisionQC Worker가 반복 종료되었습니다.\r\n\r\n" +
                        "선택된 VPDL: " + (vpdlAvailable ? installation.DisplayName : "미설치 모드") + "\r\n" +
                        "VisionQC LocalAgent 로그와 Windows 응용 프로그램 오류를 확인하세요.",
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Thread.Sleep(1200);
            }
        }

        private static VpdlRuntimeCatalog.Installation ResolveInstallation(string selected)
        {
            var all = VpdlRuntimeCatalog.Discover();
            string requested = (selected ?? "").Trim();
            if (requested.Length > 0)
            {
                var exact = all.FirstOrDefault(item => string.Equals(item.ApiVersion, requested, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ProductVersion, requested, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }
            return all.FirstOrDefault(item => VpdlWorkerLocator.IsAvailable(AppDomain.CurrentDomain.BaseDirectory, item.ApiVersion));
        }

        private static string ValueAfter(string[] args, string option)
        {
            if (args == null) return null;
            for (int index = 0; index + 1 < args.Length; index++)
                if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return null;
        }

        private static void RegisterProtocol()
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\visionqc-agent"))
            {
                key.SetValue("", "URL:VisionQC Local Agent");
                key.SetValue("URL Protocol", "");
                using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", "\"" + exe + "\",0");
                using (var command = key.CreateSubKey(@"shell\open\command")) command.SetValue("", "\"" + exe + "\" \"%1\"");
            }
        }

        private static void UnregisterProtocol()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\visionqc-agent", false); } catch { }
        }
    }
}
