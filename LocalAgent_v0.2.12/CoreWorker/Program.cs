using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using VisionQC.LocalAgent.Services;

namespace VisionQC.LocalAgent
{
    internal static class Program
    {
        internal const string AgentVersion = "1.3.12";
        private static int _requestedExitCode;

        internal static string AgentHomeDirectory
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("VISIONQC_AGENT_HOME");
                return string.IsNullOrWhiteSpace(configured) ? AppDomain.CurrentDomain.BaseDirectory : configured.Trim();
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AgentDiagnostics.Initialize(AgentHomeDirectory, "core", AgentVersion);
            bool openOfflinePage = args != null && Array.Exists(args, value => string.Equals(value, "--offline", StringComparison.OrdinalIgnoreCase));
            try
            {
                using (var server = new CoreAgentServer()) server.RunUntilExit(openOfflinePage);
            }
            catch (Exception ex)
            {
                WriteStartupFailure(ex);
                _requestedExitCode = VpdlWorkerSelection.StartupFailureExitCode;
            }
            if (_requestedExitCode != 0) Environment.ExitCode = _requestedExitCode;
        }

        internal static void RequestWorkerRestart()
        {
            _requestedExitCode = VpdlWorkerSelection.RestartExitCode;
        }

        internal static void UnregisterProtocol()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\visionqc-agent", false); } catch { }
        }

        private static void WriteStartupFailure(Exception ex)
        {
            try
            {
                string logDirectory = Path.Combine(AgentHomeDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | Core Worker | VPDL 미설치 모드" + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(Path.Combine(logDirectory, "agent-startup.log"), text);
            }
            catch { }
        }
    }
}
