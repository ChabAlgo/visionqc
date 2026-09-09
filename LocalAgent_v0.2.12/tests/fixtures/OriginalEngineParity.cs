using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using VpdlGreenHeatmapOverlay;

// Compiled with the two UNMODIFIED source files extracted from the user's original ZIP.
internal static class OriginalEngineParity
{
    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
            string file = Path.Combine(Environment.GetEnvironmentVariable("COGNEX_VPDL_DLL_DIR"),
                new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        return Run(args);
    }
    private static int Run(string[] args)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 20 * 1024 * 1024 };
        try
        {
            var config = json.Deserialize<AppConfig>(File.ReadAllText(args[0]));
            var result = Task.Run(() => GreenOverlayProcessor.Run(config, null, CancellationToken.None)).GetAwaiter().GetResult();
            File.WriteAllText(args[1], json.Serialize(result));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
