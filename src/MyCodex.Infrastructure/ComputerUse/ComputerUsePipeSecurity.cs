using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal static class ComputerUsePipeSecurity
    {
        public static void Restrict(string pipePath)
        {
            if (pipePath == null || !pipePath.StartsWith(@"\\.\pipe\mycodex-cua-", StringComparison.Ordinal))
                throw new InvalidOperationException("CUA_INVALID_PIPE");
            var descriptor = new RawSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;" + WindowsIdentity.GetCurrent().User.Value + ")");
            var acl = new byte[descriptor.DiscretionaryAcl.BinaryLength];
            descriptor.DiscretionaryAcl.GetBinaryForm(acl, 0);
            using (var handle = CreateFile(pipePath, 0x60000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                var error = SetSecurityInfo(handle, 6, 4, IntPtr.Zero, IntPtr.Zero, acl, IntPtr.Zero);
                if (error != 0) throw new Win32Exception((int)error);
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr attributes, uint creation, uint flags, IntPtr template);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint SetSecurityInfo(SafeFileHandle handle, int type, uint info, IntPtr owner, IntPtr group, byte[] dacl, IntPtr sacl);
    }
}
