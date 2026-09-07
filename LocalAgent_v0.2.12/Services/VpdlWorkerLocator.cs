using System;
using System.IO;

namespace VisionQC.LocalAgent.Services
{
    internal static class VpdlWorkerLocator
    {
        internal const string WorkerFileName = "VisionQC.VpdlWorker.exe";
        internal const string UniversalDirectoryName = "Universal";

        internal static string ExactWorkerPath(string agentHome, string apiVersion)
        {
            return Path.Combine(agentHome ?? "", "Workers", (apiVersion ?? "").Trim(), WorkerFileName);
        }

        internal static string UniversalWorkerPath(string agentHome)
        {
            return Path.Combine(agentHome ?? "", "Workers", UniversalDirectoryName, WorkerFileName);
        }

        internal static string Resolve(string agentHome, string apiVersion, out bool universal)
        {
            string exact = ExactWorkerPath(agentHome, apiVersion);
            if (File.Exists(exact))
            {
                universal = false;
                return exact;
            }

            string fallback = UniversalWorkerPath(agentHome);
            universal = File.Exists(fallback);
            return universal ? fallback : "";
        }

        internal static bool IsAvailable(string agentHome, string apiVersion)
        {
            bool universal;
            return !string.IsNullOrWhiteSpace(Resolve(agentHome, apiVersion, out universal));
        }
    }
}
