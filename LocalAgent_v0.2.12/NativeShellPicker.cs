using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VisionQC.LocalAgent
{
    internal static class NativeShellPicker
    {
        private const int S_OK = 0;
        private const int ERROR_CANCELLED_HRESULT = unchecked((int)0x800704C7);
        private const uint WM_CLOSE = 0x0010;
        private const uint DRIVE_FIXED = 3;
        private static readonly object ActiveDialogSync = new object();
        private static IFileDialog _activeDialog;
        private static string _activeDialogTitle = "";
        private static bool _cancelRequested;
        // IFileDialog COM 객체는 생성한 STA에서만 안전하게 닫을 수 있습니다.
        // HTTP 처리 스레드에서 Close를 호출하면 Explorer 창은 남고 선택 작업이
        // 끝나지 않을 수 있으므로, 해당 STA의 메시지 루프로 취소를 전달합니다.
        private static Action<Action> _dialogThreadInvoker;
        private static readonly Guid FolderClientGuid = new Guid("8A2BD724-CE21-4E10-8215-9F2992218241");
        private static readonly Guid WorkspaceClientGuid = new Guid("6F6B6F4B-5726-4B7A-ADBD-AE265C8E29B1");
        private static readonly Guid ImageClientGuid = new Guid("E05F45A8-EA4B-4E74-9ACF-2CC1FC07B361");
        private static readonly Guid CsvClientGuid = new Guid("7DA7D030-543D-4F14-A765-AE5940EFB8EE");

        [Flags]
        private enum FileOpenOptions : uint
        {
            FOS_OVERWRITEPROMPT = 0x00000002,
            FOS_STRICTFILETYPES = 0x00000004,
            FOS_NOCHANGEDIR = 0x00000008,
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_ALLNONSTORAGEITEMS = 0x00000080,
            FOS_NOVALIDATE = 0x00000100,
            FOS_ALLOWMULTISELECT = 0x00000200,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_FILEMUSTEXIST = 0x00001000,
            FOS_CREATEPROMPT = 0x00002000,
            FOS_SHAREAWARE = 0x00004000,
            FOS_NOREADONLYRETURN = 0x00008000,
            FOS_NOTESTFILECREATE = 0x00010000,
            FOS_NODEREFERENCELINKS = 0x00100000,
            FOS_OKBUTTONNEEDSINTERACTION = 0x00200000,
            FOS_DONTADDTORECENT = 0x02000000,
            FOS_FORCESHOWHIDDEN = 0x10000000,
            FOS_DEFAULTNOMINIMODE = 0x20000000,
            FOS_FORCEPREVIEWPANEON = 0x40000000,
            FOS_SUPPORTSTREAMABLEITEMS = 0x80000000
        }

        private enum Sigdn : uint
        {
            FileSystemPath = 0x80058000
        }

        private enum Fdap
        {
            Bottom = 0,
            Top = 1
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ComDlgFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
        }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FileOpenOptions fos);
            void GetOptions(out FileOpenOptions pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, Fdap fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog : IFileDialog
        {
            // COM interop에서는 상속받은 IFileDialog 메서드도 이 인터페이스의
            // vtable에 다시 선언해야 합니다. 누락하면 창은 떠도 Show()가 닫힌
            // 뒤 반환되지 않을 수 있습니다.
            [PreserveSig] new int Show(IntPtr parent);
            new void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] rgFilterSpec);
            new void SetFileTypeIndex(uint iFileType);
            new void GetFileTypeIndex(out uint piFileType);
            new void Advise(IntPtr pfde, out uint pdwCookie);
            new void Unadvise(uint dwCookie);
            new void SetOptions(FileOpenOptions fos);
            new void GetOptions(out FileOpenOptions pfos);
            new void SetDefaultFolder(IShellItem psi);
            new void SetFolder(IShellItem psi);
            new void GetFolder(out IShellItem ppsi);
            new void GetCurrentSelection(out IShellItem ppsi);
            new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            new void GetResult(out IShellItem ppsi);
            new void AddPlace(IShellItem psi, Fdap fdap);
            new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            new void Close([MarshalAs(UnmanagedType.Error)] int hr);
            new void SetClientGuid(ref Guid guid);
            new void ClearClientData();
            new void SetFilter(IntPtr pFilter);
            void GetResults(out IShellItemArray ppenum);
            void GetSelectedItems(out IShellItemArray ppsai);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(Sigdn sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid fmtid;
            public uint pid;
        }

        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(ref PropertyKey keyType, ref Guid riid, out IntPtr ppv);
            void GetAttributes(uint dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
            void EnumItems(out IntPtr ppenumShellItems);
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCom
        {
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string lpRootPathName);

        internal static void PrepareDialogRequest()
        {
            lock (ActiveDialogSync)
            {
                _activeDialog = null;
                _activeDialogTitle = "";
                _cancelRequested = false;
                _dialogThreadInvoker = null;
            }
        }

        internal static bool CancelActiveDialog()
        {
            IFileDialog dialog;
            Action<Action> dialogThreadInvoker;
            string dialogTitle;
            lock (ActiveDialogSync)
            {
                _cancelRequested = true;
                dialog = _activeDialog;
                dialogThreadInvoker = _dialogThreadInvoker;
                dialogTitle = _activeDialogTitle;
            }
            if (dialog != null && dialogThreadInvoker != null)
            {
                try { dialogThreadInvoker(() => CloseActiveDialogOnOwnerThread(dialog)); }
                catch { }
            }
            if (dialog != null) ScheduleNativeWindowClose(dialog, dialogTitle);
            return true;
        }

        private static void SetActiveDialog(IFileDialog dialog, string dialogTitle)
        {
            lock (ActiveDialogSync)
            {
                _activeDialog = dialog;
                _activeDialogTitle = dialogTitle ?? "";
            }
        }

        private static bool IsCancelRequested(IFileDialog dialog)
        {
            lock (ActiveDialogSync) return _cancelRequested && ReferenceEquals(_activeDialog, dialog);
        }

        private static void CloseActiveDialogOnOwnerThread(IFileDialog dialog)
        {
            if (!IsCancelRequested(dialog)) return;
            try { dialog.Close(ERROR_CANCELLED_HRESULT); } catch { }
        }

        private static void ScheduleNativeWindowClose(IFileDialog dialog, string dialogTitle)
        {
            if (string.IsNullOrWhiteSpace(dialogTitle)) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // IFileDialog.Close는 다른 apartment에서 무시될 수 있습니다. Explorer의
                // 실제 모달 창에 WM_CLOSE를 보내면 사용자가 취소한 것과 동일하게 Show가 반환됩니다.
                // 첫 Explorer 대화상자는 Shell 초기화 때문에 수 초 뒤에 생성될 수 있습니다.
                // 취소 요청을 먼저 받은 경우에도 창이 준비되는 즉시 닫도록 최대 15초간 확인합니다.
                for (int attempt = 0; attempt < 150 && IsCancelRequested(dialog); attempt++)
                {
                    IntPtr window = FindWindow("#32770", dialogTitle);
                    if (window != IntPtr.Zero)
                    {
                        PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                    Thread.Sleep(100);
                }
            });
        }

        private static void RegisterDialogThread(Form owner)
        {
            lock (ActiveDialogSync)
            {
                _dialogThreadInvoker = action =>
                {
                    if (action == null || owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
                    owner.BeginInvoke((MethodInvoker)(() => action()));
                };
            }
        }

        private static void ClearActiveDialog(IFileDialog dialog)
        {
            lock (ActiveDialogSync)
            {
                if (ReferenceEquals(_activeDialog, dialog)) _activeDialog = null;
                _activeDialogTitle = "";
                _cancelRequested = false;
                _dialogThreadInvoker = null;
            }
        }

        internal static string PickFolder(string initialPath)
        {
            string[] paths = PickFolders(initialPath, false);
            return paths.Length == 0 ? null : paths[0];
        }

        internal static string[] PickFolders(string initialPath, bool allowMultiple)
        {
            return RunOwnedDialog("폴더 선택", ownerHandle =>
            {
                IFileOpenDialog dialog = null;
                IShellItem initialItem = null;
                IShellItem resultItem = null;
                IShellItemArray resultItems = null;
                try
                {
                    dialog = (IFileOpenDialog)new FileOpenDialogCom();
                    Guid clientGuid = FolderClientGuid;
                    dialog.SetClientGuid(ref clientGuid);
                    // Shell이 기억한 마지막 위치가 끊긴 H:/UNC 경로이면 Show 자체가 오래
                    // 멈출 수 있다. 매 요청마다 stale MRU를 버리고 안전한 로컬 폴더에서 연다.
                    try { dialog.ClearClientData(); } catch { }
                    dialog.SetTitle("VisionQC 폴더 선택");
                    dialog.SetOkButtonLabel("폴더 선택");
                    FileOpenOptions options = FileOpenOptions.FOS_PICKFOLDERS |
                                      FileOpenOptions.FOS_FORCEFILESYSTEM |
                                      FileOpenOptions.FOS_PATHMUSTEXIST |
                                      FileOpenOptions.FOS_NOCHANGEDIR |
                                      FileOpenOptions.FOS_DONTADDTORECENT;
                    if (allowMultiple) options |= FileOpenOptions.FOS_ALLOWMULTISELECT;
                    dialog.SetOptions(options);

                    string initialFolder = FastLocalInitialFolder(initialPath, false) ?? SafeLocalInitialFolder();
                    if (!string.IsNullOrWhiteSpace(initialFolder))
                    {
                        try
                        {
                            Guid shellItemGuid = typeof(IShellItem).GUID;
                            SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref shellItemGuid, out initialItem);
                            if (initialItem != null) dialog.SetFolder(initialItem);
                        }
                        catch { }
                    }

                    SetActiveDialog(dialog, "VisionQC 폴더 선택");
                    if (IsCancelRequested(dialog)) return new string[0];
                    SetForegroundWindow(ownerHandle);
                    int hr = dialog.Show(ownerHandle);
                    if (hr == ERROR_CANCELLED_HRESULT) return new string[0];
                    if (hr != S_OK) Marshal.ThrowExceptionForHR(hr);

                    if (allowMultiple)
                    {
                        dialog.GetResults(out resultItems);
                        return ShellItemPaths(resultItems);
                    }

                    dialog.GetResult(out resultItem);
                    string path = ShellItemPath(resultItem);
                    return string.IsNullOrWhiteSpace(path) ? new string[0] : new[] { path };
                }
                finally
                {
                    ClearActiveDialog(dialog);
                    if (resultItems != null) Marshal.FinalReleaseComObject(resultItems);
                    if (resultItem != null) Marshal.FinalReleaseComObject(resultItem);
                    if (initialItem != null) Marshal.FinalReleaseComObject(initialItem);
                    if (dialog != null) Marshal.FinalReleaseComObject(dialog);
                }
            });
        }

        internal static string PickFile(string initialPath, string fileType)
        {
            string kind = (fileType ?? "workspace").Trim().ToLowerInvariant();
            string caption = kind == "image" ? "이미지 선택" : kind == "csv" ? "CSV 선택" : "Workspace 선택";
            return RunOwnedDialog(caption, ownerHandle =>
            {
                IFileDialog dialog = null;
                IShellItem initialItem = null;
                IShellItem resultItem = null;
                try
                {
                    dialog = (IFileDialog)new FileOpenDialogCom();
                    Guid clientGuid = kind == "image" ? ImageClientGuid : kind == "csv" ? CsvClientGuid : WorkspaceClientGuid;
                    dialog.SetClientGuid(ref clientGuid);
                    try { dialog.ClearClientData(); } catch { }
                    dialog.SetOkButtonLabel("열기");
                    dialog.SetOptions(FileOpenOptions.FOS_FORCEFILESYSTEM |
                                      FileOpenOptions.FOS_PATHMUSTEXIST |
                                      FileOpenOptions.FOS_FILEMUSTEXIST |
                                      FileOpenOptions.FOS_NOCHANGEDIR |
                                      FileOpenOptions.FOS_DONTADDTORECENT);

                    ComDlgFilterSpec[] filters;
                    if (kind == "csv")
                    {
                        dialog.SetTitle("VisionQC Cell ID CSV 선택");
                        filters = new[]
                        {
                            new ComDlgFilterSpec { pszName = "CSV 파일", pszSpec = "*.csv" },
                            new ComDlgFilterSpec { pszName = "모든 파일", pszSpec = "*.*" }
                        };
                    }
                    else if (kind == "image")
                    {
                        dialog.SetTitle("VisionQC 이미지 선택");
                        filters = new[]
                        {
                            new ComDlgFilterSpec { pszName = "이미지 파일", pszSpec = "*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff" },
                            new ComDlgFilterSpec { pszName = "모든 파일", pszSpec = "*.*" }
                        };
                    }
                    else
                    {
                        dialog.SetTitle("VisionQC Runtime Workspace 선택");
                        filters = new[]
                        {
                            new ComDlgFilterSpec { pszName = "VPDL Runtime Workspace", pszSpec = "*.vrws;*.vws" },
                            new ComDlgFilterSpec { pszName = "모든 파일", pszSpec = "*.*" }
                        };
                    }
                    dialog.SetFileTypes((uint)filters.Length, filters);
                    dialog.SetFileTypeIndex(1);

                    string initialFolder = FastLocalInitialFolder(initialPath, true) ?? SafeLocalInitialFolder();
                    if (!string.IsNullOrWhiteSpace(initialPath) && Path.HasExtension(initialPath))
                    {
                        try { dialog.SetFileName(Path.GetFileName(initialPath)); } catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(initialFolder))
                    {
                        try
                        {
                            Guid shellItemGuid = typeof(IShellItem).GUID;
                            SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref shellItemGuid, out initialItem);
                            if (initialItem != null) dialog.SetFolder(initialItem);
                        }
                        catch { }
                    }

                    string dialogTitle = kind == "image" ? "VisionQC 이미지 선택" : kind == "csv" ? "VisionQC Cell ID CSV 선택" : "VisionQC Runtime Workspace 선택";
                    SetActiveDialog(dialog, dialogTitle);
                    if (IsCancelRequested(dialog)) return null;
                    SetForegroundWindow(ownerHandle);
                    int hr = dialog.Show(ownerHandle);
                    if (hr == ERROR_CANCELLED_HRESULT) return null;
                    if (hr != S_OK) Marshal.ThrowExceptionForHR(hr);

                    dialog.GetResult(out resultItem);
                    return ShellItemPath(resultItem);
                }
                finally
                {
                    ClearActiveDialog(dialog);
                    if (resultItem != null) Marshal.FinalReleaseComObject(resultItem);
                    if (initialItem != null) Marshal.FinalReleaseComObject(initialItem);
                    if (dialog != null) Marshal.FinalReleaseComObject(dialog);
                }
            });
        }

        internal static string PickFile(string initialPath)
        {
            return PickFile(initialPath, "workspace");
        }

        private static string ShellItemPath(IShellItem item)
        {
            if (item == null) return null;
            IntPtr ptr;
            item.GetDisplayName(Sigdn.FileSystemPath, out ptr);
            try { return Marshal.PtrToStringUni(ptr); }
            finally { if (ptr != IntPtr.Zero) CoTaskMemFree(ptr); }
        }

        private static string[] ShellItemPaths(IShellItemArray items)
        {
            if (items == null) return new string[0];
            var paths = new List<string>();
            uint count = 0;
            items.GetCount(out count);
            for (uint index = 0; index < count; index++)
            {
                IShellItem item = null;
                try
                {
                    items.GetItemAt(index, out item);
                    string path = ShellItemPath(item);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    bool alreadyAdded = false;
                    foreach (string existing in paths)
                    {
                        if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }
                    if (!alreadyAdded) paths.Add(path);
                }
                finally
                {
                    if (item != null) Marshal.FinalReleaseComObject(item);
                }
            }
            return paths.ToArray();
        }

        private static string FastLocalInitialFolder(string initialPath, bool filePicker)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(initialPath) || !Path.IsPathRooted(initialPath)) return null;
                string folder = filePicker && Path.HasExtension(initialPath) ? Path.GetDirectoryName(initialPath) : initialPath;
                if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal)) return null;
                string root = Path.GetPathRoot(folder);
                // H: 가상 드라이브·UNC·연결이 끊긴 네트워크 경로는 File.Exists/Directory.Exists
                // 자체가 수십 초 멈출 수 있으므로 초기 탐색에서 제외합니다. 이런 경로는
                // Shell의 저장 위치도 초기화하고 안전한 로컬 폴더에서 먼저 시작합니다.
                if (string.IsNullOrWhiteSpace(root) || GetDriveType(root) != DRIVE_FIXED) return null;
                return Directory.Exists(folder) ? folder : null;
            }
            catch { return null; }
        }

        private static string SafeLocalInitialFolder()
        {
            foreach (Environment.SpecialFolder specialFolder in new[]
            {
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.UserProfile
            })
            {
                try
                {
                    string folder = Environment.GetFolderPath(specialFolder);
                    if (string.IsNullOrWhiteSpace(folder)) continue;
                    string root = Path.GetPathRoot(folder);
                    if (!string.IsNullOrWhiteSpace(root) && GetDriveType(root) == DRIVE_FIXED && Directory.Exists(folder))
                        return folder;
                }
                catch { }
            }
            return null;
        }

        private static T RunOwnedDialog<T>(string caption, Func<IntPtr, T> showDialog)
        {
            T result = default(T);
            Exception error = null;
            using (var completed = new ManualResetEvent(false))
            {
            var thread = new Thread(() =>
            {
                Form owner = null;
                try
                {
                    owner = CreateDialogOwner(caption);
                    owner.Show();
                    owner.BringToFront();
                    owner.Activate();
                    SetForegroundWindow(owner.Handle);
                    RegisterDialogThread(owner);

                    // IFileDialog.Show 자체가 모달 메시지 루프를 실행합니다. Application.Run 안에서
                    // BeginInvoke로 한 번 더 중첩하면 Explorer가 닫혀도 외부 작업이 반환되지 않는
                    // 경우가 있으므로, 별도 STA에서 바로 호출하고 반환 즉시 owner를 정리합니다.
                    result = showDialog(owner.Handle);
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    if (owner != null)
                    {
                        try { owner.Close(); } catch { }
                        owner.Dispose();
                    }
                    completed.Set();
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            // COM apartment 종료를 Thread.Join으로 기다리면 Shell 내부 정리가 길게
            // 남아 선택 결과까지 멈출 수 있습니다. UI 처리 완료 신호만 기다린 뒤
            // HTTP 작업은 즉시 다음 단계로 진행합니다.
            completed.WaitOne();
            }

            if (error != null) throw error;
            return result;
        }

        private static Form CreateDialogOwner(string caption)
        {
            var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            var owner = new Form
            {
                Text = "VisionQC Local Agent - " + caption,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Width = 1,
                Height = 1,
                Left = workArea.Right + 16,
                Top = workArea.Bottom + 16,
                Opacity = 0,
                TopMost = false,
                MinimizeBox = false,
                MaximizeBox = false
            };
            return owner;
        }

    }
}
