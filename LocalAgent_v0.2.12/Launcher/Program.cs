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
        private const string LauncherVersion = "1.3.6";

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
            int crashRestarts = 0;
            while (true)
            {
                var installation = ResolveInstallation(selected);
                if (installation == null)
                {
                    MessageBox.Show("정상 설치된 Cognex VPDL Runtime을 찾지 못했습니다.\r\n\r\n" +
                        "VPDL 설치 폴더의 ViDi.NET.Local.dll 및 bin\\vidi_*.dll 쌍을 확인하세요.\r\n" +
                        "기본 탐색 루트: " + VpdlRuntimeCatalog.DefaultRoot,
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                bool universalWorker;
                string worker = VpdlWorkerLocator.Resolve(AppDomain.CurrentDomain.BaseDirectory, installation.ApiVersion, out universalWorker);
                if (string.IsNullOrWhiteSpace(worker))
                {
                    MessageBox.Show("VPDL " + installation.ProductVersion + " (API " + installation.ApiVersion + ")용 Worker가 설치되어 있지 않습니다.\r\n\r\n" +
                        "정확 버전 Worker 또는 Universal Worker가 포함된 VisionQC 설치 프로그램을 사용하세요.",
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                VpdlWorkerSelection.Write(installation.ApiVersion);
                var startInfo = new ProcessStartInfo
                {
                    FileName = worker,
                    Arguments = offline ? "--worker --offline" : "--worker",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.EnvironmentVariables["VISIONQC_AGENT_HOME"] = AppDomain.CurrentDomain.BaseDirectory;
                startInfo.EnvironmentVariables["VISIONQC_VPDL_API_VERSION"] = installation.ApiVersion;
                startInfo.EnvironmentVariables["VISIONQC_VPDL_PRODUCT_VERSION"] = installation.ProductVersion;
                startInfo.EnvironmentVariables["COGNEX_VPDL_DLL_DIR"] = installation.StudioDirectory;
                startInfo.EnvironmentVariables["VISIONQC_VPDL_WORKER_MODE"] = universalWorker ? "universal" : "exact";
                var process = Process.Start(startInfo);
                process.WaitForExit();

                if (process.ExitCode == VpdlWorkerSelection.RestartExitCode)
                {
                    selected = VpdlWorkerSelection.Read();
                    crashRestarts = 0;
                    continue;
                }
                if (process.ExitCode == VpdlWorkerSelection.StartupFailureExitCode)
                {
                    MessageBox.Show("VPDL Worker 시작에 실패했습니다.\r\n\r\n" +
                        "선택된 VPDL: " + installation.DisplayName + "\r\n" +
                        "Worker 방식: " + (universalWorker ? "Universal" : "Exact") + "\r\n" +
                        "로그: " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "agent-startup.log"),
                        "VisionQC Local Agent", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (process.ExitCode == 0) return;

                crashRestarts++;
                if (crashRestarts >= 3)
                {
                    MessageBox.Show("VPDL Worker가 반복 종료되었습니다.\r\n\r\n" +
                        "선택된 VPDL: " + installation.DisplayName + "\r\n" +
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
