using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace VisionQC.LocalAgent.Services
{
    /// <summary>
    /// Cognex VPDL 설치본을 파일 존재 여부가 아니라 관리 DLL/네이티브 엔진 DLL의 API 버전 쌍으로 검증한다.
    /// 서로 다른 VPDL 버전의 DLL을 한 프로세스에 섞지 않는 것이 이 클래스의 가장 중요한 규칙이다.
    /// </summary>
    internal static class VpdlRuntimeCatalog
    {
        internal const string DefaultRoot = @"C:\Program Files\Cognex\VisionPro Deep Learning";

        internal sealed class Installation
        {
            internal string ProductVersion { get; set; }
            internal string ApiVersion { get; set; }
            internal string StudioDirectory { get; set; }
            internal string RootDirectory { get; set; }
            internal string NativeDirectory { get; set; }
            internal string NativeLibraryPath { get; set; }
            internal string ManagedAssemblyVersion { get; set; }

            internal string DisplayName
            {
                get { return string.IsNullOrWhiteSpace(ProductVersion) ? ApiVersion : ProductVersion + " (API " + ApiVersion + ")"; }
            }
        }

        internal static IReadOnlyList<Installation> Discover(string explicitStudioDirectory = null)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitStudioDirectory)) candidates.Add(explicitStudioDirectory.Trim());

            string root = Environment.GetEnvironmentVariable("COGNEX_VPDL_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = DefaultRoot;
            candidates.AddRange(EnumerateManagedAssemblyDirectories(root));

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(ReadHealthyInstallation)
                .Where(item => item != null)
                .GroupBy(item => item.RootDirectory + "|" + item.ApiVersion, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item =>
                    string.Equals(new DirectoryInfo(item.StudioDirectory).Name, "Cognex Deep Learning Studio", StringComparison.OrdinalIgnoreCase)).First())
                .OrderByDescending(item => ParseVersion(item.ProductVersion))
                .ThenByDescending(item => ParseVersion(item.ApiVersion))
                .ToList();
        }

        private static IEnumerable<string> EnumerateManagedAssemblyDirectories(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
            var pending = new Queue<Tuple<string, int>>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pending.Enqueue(Tuple.Create(Path.GetFullPath(root), 0));

            while (pending.Count > 0)
            {
                Tuple<string, int> current = pending.Dequeue();
                if (!visited.Add(current.Item1)) continue;
                if (File.Exists(Path.Combine(current.Item1, "ViDi.NET.Local.dll"))) yield return current.Item1;
                if (current.Item2 >= 4) continue;

                string[] children;
                try { children = Directory.GetDirectories(current.Item1); }
                catch { continue; }
                foreach (string child in children)
                {
                    try
                    {
                        if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) == 0)
                            pending.Enqueue(Tuple.Create(child, current.Item2 + 1));
                    }
                    catch { }
                }
            }
        }

        internal static Installation ResolveForManagedAssembly(Assembly managedAssembly, string explicitStudioDirectory = null)
        {
            if (managedAssembly == null) return null;
            string apiVersion = ToApiVersion(managedAssembly.GetName().Version);
            return Discover(explicitStudioDirectory).FirstOrDefault(item => string.Equals(item.ApiVersion, apiVersion, StringComparison.OrdinalIgnoreCase));
        }

        internal static Installation FindByVersion(string requestedVersion)
        {
            string value = (requestedVersion ?? "").Trim();
            if (value.Length == 0 || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase)) return null;
            return Discover().FirstOrDefault(item =>
                string.Equals(item.ProductVersion, value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ApiVersion, value, StringComparison.OrdinalIgnoreCase));
        }

        internal static string ToApiVersion(Version version)
        {
            return version == null ? "" : version.Major + "." + version.Minor;
        }

        private static Installation ReadHealthyInstallation(string studioDirectory)
        {
            try
            {
                string studio = Path.GetFullPath(studioDirectory);
                string managed = Path.Combine(studio, "ViDi.NET.Local.dll");
                if (!File.Exists(managed)) return null;

                var managedVersion = AssemblyName.GetAssemblyName(managed).Version;
                string apiVersion = ToApiVersion(managedVersion);
                if (string.IsNullOrWhiteSpace(apiVersion)) return null;

                string nativeName = "vidi_" + apiVersion.Replace(".", "") + ".dll";
                string root = FindVersionRoot(studio, nativeName);
                if (string.IsNullOrWhiteSpace(root)) return null;
                string nativeDirectory = Path.Combine(root, "bin");
                string native = Path.Combine(nativeDirectory, nativeName);

                return new Installation
                {
                    ProductVersion = new DirectoryInfo(root).Name,
                    ApiVersion = apiVersion,
                    StudioDirectory = studio,
                    RootDirectory = root,
                    NativeDirectory = nativeDirectory,
                    NativeLibraryPath = native,
                    ManagedAssemblyVersion = managedVersion.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        private static string FindVersionRoot(string managedDirectory, string nativeName)
        {
            DirectoryInfo cursor = new DirectoryInfo(managedDirectory);
            for (int depth = 0; cursor != null && depth < 6; depth++, cursor = cursor.Parent)
            {
                string native = Path.Combine(cursor.FullName, "bin", nativeName);
                if (File.Exists(native)) return cursor.FullName;
            }
            return "";
        }

        private static Version ParseVersion(string value)
        {
            Version parsed;
            return Version.TryParse(value ?? "", out parsed) ? parsed : new Version(0, 0);
        }
    }
}
