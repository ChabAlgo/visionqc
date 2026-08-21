using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using System.Windows.Forms;

namespace VisionQC.LocalAgent
{
    internal static class Program
    {
        internal const string AgentVersion = "0.2.12";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssemblyFromLocalOrVpdInstall;

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
                server.RunUntilExit();
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
                string envDir = Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR");
                string[] candidateDirs = new[]
                {
                    baseDir,
                    Path.Combine(baseDir, "Cognex"),
                    envDir,
                    @"C:\Program Files\Cognex\VisionPro Deep Learning\4.2\Cognex Deep Learning Studio",
                    @"C:\Program Files\Cognex\VisionPro Deep Learning\4.1\Cognex Deep Learning Studio",
                    @"C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio",
                    @"C:\Program Files\Cognex\VisionPro Deep Learning\5.0\Cognex Deep Learning Studio"
                };
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
    }
}
