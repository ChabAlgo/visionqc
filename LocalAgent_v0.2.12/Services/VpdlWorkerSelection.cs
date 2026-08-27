using System;
using System.IO;

namespace VisionQC.LocalAgent.Services
{
    /// <summary>
    /// Launcher와 VPDL Worker가 공유하는 선택 상태다.
    /// 버전 전환은 현재 Worker가 종료되고, Launcher가 다른 API Worker를 새 프로세스로 시작하는 방식으로만 수행한다.
    /// </summary>
    internal static class VpdlWorkerSelection
    {
        internal const int RestartExitCode = 74;

        internal static string SelectionPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VisionQC", "LocalAgent", "vpdl-worker.version");
            }
        }

        internal static string Read()
        {
            try { return File.Exists(SelectionPath) ? (File.ReadAllText(SelectionPath) ?? "").Trim() : ""; }
            catch { return ""; }
        }

        internal static void Write(string apiVersion)
        {
            string value = (apiVersion ?? "").Trim();
            if (value.Length == 0) throw new InvalidOperationException("선택할 VPDL API 버전이 없습니다.");
            string directory = Path.GetDirectoryName(SelectionPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(SelectionPath, value);
        }
    }
}
