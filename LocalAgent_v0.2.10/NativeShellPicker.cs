using System;
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
        private const uint DRIVE_FIXED = 3;
        private static readonly object ActiveDialogSync = new object();
        private static IFileDialog _activeDialog;
        private static bool _cancelRequested;
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
            FOS_HIDEMRUPLACES = 0x00020000,
            FOS_HIDEPINNEDPLACES = 0x00040000,
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
            void GetResults(out IShellItemArray ppsi);
            void AddPlace(IShellItem psi, Fdap fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
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

        [ComImport]
        [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
            void GetPropertyDescriptionList(ref Guid keyType, ref Guid riid, out IntPtr ppv);
            void GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
            void GetCount(out uint pdwNumItems);
            void GetItemAt(uint dwIndex, out IShellItem ppsi);
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCom
        {
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

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
                _cancelRequested = false;
            }
        }

        internal static bool CancelActiveDialog()
        {
            IFileDialog dialog;
            lock (ActiveDialogSync)
            {
                _cancelRequested = true;
                dialog = _activeDialog;
            }
            // Dialog COM 객체가 만들어지기 전의 취소도 예약해 SetActiveDialog에서 처리합니다.
            if (dialog == null) return true;
            try
            {
                // IFileDialog.Close is marshalled back to the dialog's STA thread.
                // The owner thread keeps a real Application.Run message pump alive.
                dialog.Close(ERROR_CANCELLED_HRESULT);
                return true;
            }
            catch { return false; }
        }

        private static void SetActiveDialog(IFileDialog dialog)
        {
            bool cancel;
            lock (ActiveDialogSync)
            {
                _activeDialog = dialog;
                cancel = _cancelRequested;
            }
            if (cancel)
            {
                try { dialog.Close(ERROR_CANCELLED_HRESULT); } catch { }
            }
        }

        private static void ClearActiveDialog(IFileDialog dialog)
        {
            lock (ActiveDialogSync)
            {
                if (ReferenceEquals(_activeDialog, dialog)) _activeDialog = null;
                _cancelRequested = false;
            }
        }

        internal static string[] PickFolder(string initialPath)
        {
            return RunOwnedDialog("폴더 선택", ownerHandle =>
            {
                IFileDialog dialog = null;
                IShellItem initialItem = null;
                IShellItemArray resultItems = null;
                try
                {
                    dialog = (IFileDialog)new FileOpenDialogCom();
                    Guid clientGuid = FolderClientGuid;
                    dialog.SetClientGuid(ref clientGuid);
                    // Shell이 기억한 마지막 위치가 끊긴 H:/UNC 경로이면 Show 자체가 오래
                    // 멈출 수 있다. 매 요청마다 stale MRU를 버리고 안전한 로컬 폴더에서 연다.
                    try { dialog.ClearClientData(); } catch { }
                    dialog.SetTitle("VisionQC 폴더 선택");
                    dialog.SetOkButtonLabel("폴더 선택");
                    dialog.SetOptions(FileOpenOptions.FOS_PICKFOLDERS |
                                      FileOpenOptions.FOS_FORCEFILESYSTEM |
                                      FileOpenOptions.FOS_PATHMUSTEXIST |
                                      FileOpenOptions.FOS_NOCHANGEDIR |
                                      FileOpenOptions.FOS_ALLOWMULTISELECT);

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

                    SetActiveDialog(dialog);
                    SetForegroundWindow(ownerHandle);
                    int hr = dialog.Show(ownerHandle);
                    if (hr == ERROR_CANCELLED_HRESULT) return Array.Empty<string>();
                    if (hr != S_OK) Marshal.ThrowExceptionForHR(hr);

                    dialog.GetResults(out resultItems);
                    return ShellItemPaths(resultItems);
                }
                finally
                {
                    ClearActiveDialog(dialog);
                    if (resultItems != null) Marshal.FinalReleaseComObject(resultItems);
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
                                      FileOpenOptions.FOS_NOCHANGEDIR);

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

                    SetActiveDialog(dialog);
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

        private static string[] ShellItemPaths(IShellItemArray items)
        {
            var paths = new System.Collections.Generic.List<string>();
            if (items == null) return paths.ToArray();
            uint count = 0;
            items.GetCount(out count);
            for (uint i = 0; i < count; i++)
            {
                IShellItem item = null;
                try
                {
                    items.GetItemAt(i, out item);
                    string path = ShellItemPath(item);
                    if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
                }
                finally
                {
                    if (item != null) Marshal.FinalReleaseComObject(item);
                }
            }
            return paths.ToArray();
        }

        private static string ShellItemPath(IShellItem item)
        {
            if (item == null) return null;
            IntPtr ptr;
            item.GetDisplayName(Sigdn.FileSystemPath, out ptr);
            try { return Marshal.PtrToStringUni(ptr); }
            finally { if (ptr != IntPtr.Zero) CoTaskMemFree(ptr); }
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
            var thread = new Thread(() =>
            {
                Form owner = null;
                try
                {
                    owner = CreateDialogOwner(caption);
                    bool started = false;
                    owner.Shown += (sender, args) =>
                    {
                        if (started) return;
                        started = true;
                        owner.BeginInvoke((MethodInvoker)(() =>
                        {
                            try
                            {
                                owner.BringToFront();
                                owner.Activate();
                                SetForegroundWindow(owner.Handle);
                                result = showDialog(owner.Handle);
                            }
                            catch (Exception ex) { error = ex; }
                            finally { try { owner.Close(); } catch { } }
                        }));
                    };
                    // Application.Run으로 실제 STA 메시지 루프를 유지합니다.
                    // 이전의 투명 8x8 Form + Thread.Join 방식은 Windows가 전경 활성화를
                    // 거부할 때 Shell Dialog가 뒤에 숨고 HTTP 요청이 끝나지 않았습니다.
                    Application.Run(owner);
                }
                catch (Exception ex) { error = ex; }
                finally { if (owner != null) owner.Dispose(); }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (error != null) throw error;
            return result;
        }

        private static Form CreateDialogOwner(string caption)
        {
            var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            const int width = 520;
            const int height = 150;
            var owner = new Form
            {
                Text = "VisionQC Local Agent - " + caption,
                ShowInTaskbar = true,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.Manual,
                Width = width,
                Height = height,
                Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
                Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2),
                TopMost = true,
                MinimizeBox = false,
                MaximizeBox = false
            };
            owner.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Text = "Windows " + caption + " 창을 여는 중입니다.\r\n창이 보이지 않으면 작업 표시줄의 VisionQC 창을 선택하세요."
            });
            return owner;
        }

    }
}
