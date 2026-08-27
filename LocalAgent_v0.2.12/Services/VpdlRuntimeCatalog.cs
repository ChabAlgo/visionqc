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
            try
            {
                if (Directory.Exists(root))
                    candidates.AddRange(Directory.GetDirectories(root).Select(path => Path.Combine(path, "Cognex Deep Learning Studio")));
            }
            catch { }

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(ReadHealthyInstallation)
                .Where(item => item != null)
                .GroupBy(item => item.StudioDirectory, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => ParseVersion(item.ProductVersion))
                .ThenByDescending(item => ParseVersion(item.ApiVersion))
                .ToList();
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

                string root = Directory.GetParent(studio).FullName;
                string nativeDirectory = Path.Combine(root, "bin");
                string nativeName = "vidi_" + apiVersion.Replace(".", "") + ".dll";
                string native = Path.Combine(nativeDirectory, nativeName);
                // VPDL 버전 폴더만 남고 native 런타임이 제거된 경우는 선택 대상에서 제외한다.
                if (!File.Exists(native)) return null;

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

        private static Version ParseVersion(string value)
        {
            Version parsed;
            return Version.TryParse(value ?? "", out parsed) ? parsed : new Version(0, 0);
        }
    }
}
