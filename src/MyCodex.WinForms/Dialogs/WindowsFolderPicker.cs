using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Dialogs
{
    internal static class WindowsFolderPicker
    {
        private const int HResultCanceled = unchecked((int)0x800704C7);

        public static bool TryPickFolder(IWin32Window owner, string title, string initialPath, out string selectedPath)
        {
            selectedPath = null;

            IFileOpenDialog dialog = null;
            IShellItem initialFolder = null;
            IShellItem result = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
                ConfigureDialog(dialog, title, initialPath, out initialFolder);

                var ownerHandle = owner == null ? IntPtr.Zero : owner.Handle;
                var showResult = dialog.Show(ownerHandle);
                if (showResult == HResultCanceled)
                {
                    return false;
                }

                if (showResult < 0)
                {
                    Marshal.ThrowExceptionForHR(showResult);
                }

                dialog.GetResult(out result);
                selectedPath = GetFileSystemPath(result);
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            finally
            {
                ReleaseComObject(result);
                ReleaseComObject(initialFolder);
                ReleaseComObject(dialog);
            }
        }

        private static void ConfigureDialog(IFileOpenDialog dialog, string title, string initialPath, out IShellItem initialFolder)
        {
            initialFolder = null;

            FOS options;
            dialog.GetOptions(out options);
            dialog.SetOptions(options |
                FOS.FOS_PICKFOLDERS |
                FOS.FOS_FORCEFILESYSTEM |
                FOS.FOS_PATHMUSTEXIST |
                FOS.FOS_NOCHANGEDIR);

            if (!string.IsNullOrWhiteSpace(title))
            {
                dialog.SetTitle(title);
            }

            var normalizedInitialPath = NormalizeExistingDirectory(initialPath);
            if (!string.IsNullOrWhiteSpace(normalizedInitialPath) &&
                TryCreateShellItem(normalizedInitialPath, out initialFolder))
            {
                dialog.SetFolder(initialFolder);
            }
        }

        private static string NormalizeExistingDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var fullPath = Path.GetFullPath(path.Trim());
                return Directory.Exists(fullPath) ? fullPath : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool TryCreateShellItem(string path, out IShellItem shellItem)
        {
            shellItem = null;
            try
            {
                var shellItemId = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref shellItemId, out shellItem);
                return shellItem != null;
            }
            catch (COMException)
            {
                shellItem = null;
                return false;
            }
        }

        private static string GetFileSystemPath(IShellItem shellItem)
        {
            if (shellItem == null)
            {
                return null;
            }

            IntPtr displayNamePointer = IntPtr.Zero;
            try
            {
                shellItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out displayNamePointer);
                return displayNamePointer == IntPtr.Zero
                    ? null
                    : Marshal.PtrToStringUni(displayNamePointer);
            }
            finally
            {
                if (displayNamePointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(displayNamePointer);
                }
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
            FOS_PATHMUSTEXIST = 0x00000800,
            FOS_NOCHANGEDIR = 0x00000008
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000
        }

        private enum FDAP
        {
            FDAP_BOTTOM = 0,
            FDAP_TOP = 1
        }

        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
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
            void AddPlace(IShellItem psi, FDAP fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
