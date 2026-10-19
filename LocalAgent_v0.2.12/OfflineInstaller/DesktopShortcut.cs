using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace VisionQC.AgentInstaller
{
    internal static class DesktopShortcut
    {
        internal static void Create(string path, string target, string directory)
        {
            object instance = new ShellLink();
            try
            {
                var link = (IShellLink)instance;
                link.SetPath(target);
                link.SetArguments("--offline");
                link.SetWorkingDirectory(directory);
                link.SetIconLocation(target, 0);
                link.SetDescription("VisionQC 오프라인 실행");
                link.SetShowCmd(1);
                ((IPersistFile)instance).Save(path, true);
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr owner, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
    }
}
