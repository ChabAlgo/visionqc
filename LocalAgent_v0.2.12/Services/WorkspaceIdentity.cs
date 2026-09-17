using System;
using System.IO;
using System.Security.Cryptography;

namespace VisionQC.LocalAgent.Services
{
    internal static class WorkspaceIdentity
    {
        internal static string Fingerprint(string path)
        {
            string normalized;
            try { normalized = Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
            catch { normalized = (path ?? "").Trim().ToUpperInvariant(); }
            // Only at runtime load/reuse boundaries. Content identity also detects same-size/same-time overwrites.
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                    return normalized + "#" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch (Exception) { return normalized + "#UNREADABLE"; }
        }
    }
}
