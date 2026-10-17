using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MyCodex.Infrastructure.ComputerUse
{
    // The job also closes children if MyCodex itself crashes; a PID-only kill cannot do that.
    internal sealed class ComputerUseProcessJob : IDisposable
    {
        private readonly SafeFileHandle _handle;

        public ComputerUseProcessJob(Process process)
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
                if (!SetInformationJobObject(_handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))) ||
                    !AssignProcessToJobObject(_handle, process.Handle))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch { _handle.Dispose(); throw; }
        }

        public bool TerminateAndWait()
        {
            if (_handle.IsClosed) return true;
            if (!TerminateJobObject(_handle, 1)) return false;
            var information = Marshal.AllocHGlobal(48);
            try
            {
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    uint returned;
                    if (!QueryInformationJobObject(_handle, 1, information, 48, out returned)) return false;
                    if (Marshal.ReadInt32(information, 40) == 0) return true;
                    Thread.Sleep(20);
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(information); }
        }

        public void Dispose() { _handle.Dispose(); }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime;
            public long JobTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet;
            public UIntPtr MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits limits, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr information, uint length, out uint returned);
    }
}
