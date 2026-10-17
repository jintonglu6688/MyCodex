using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Tts
{
    internal sealed class TtsNativeLibrary : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly string _libraryPath;
        private IntPtr _moduleHandle;

        public TtsNativeLibrary(string libraryPath)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("TtsNativeLibrary.Error.DllPathRequired", "DLL path cannot be empty."),
                    nameof(libraryPath));
            }

            _libraryPath = libraryPath;
        }

        public bool IsLoaded
        {
            get
            {
                lock (_syncRoot)
                {
                    return _moduleHandle != IntPtr.Zero;
                }
            }
        }

        public string LibraryPath
        {
            get { return _libraryPath; }
        }

        public bool EnsureLoaded(out string error)
        {
            lock (_syncRoot)
            {
                if (_moduleHandle != IntPtr.Zero)
                {
                    error = null;
                    return true;
                }

                _moduleHandle = NativeMethods.LoadLibrary(_libraryPath);
                if (_moduleHandle != IntPtr.Zero)
                {
                    error = null;
                    return true;
                }

                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
        }

        public TDelegate GetDelegate<TDelegate>(string entryPoint) where TDelegate : class
        {
            if (string.IsNullOrWhiteSpace(entryPoint))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("TtsNativeLibrary.Error.EntryPointRequired", "Entry point name cannot be empty."),
                    nameof(entryPoint));
            }

            lock (_syncRoot)
            {
                if (_moduleHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        SharedLocalization.Text("TtsNativeLibrary.Error.LibraryNotLoaded", "DLL has not been loaded."));
                }

                var address = NativeMethods.GetProcAddress(_moduleHandle, entryPoint);
                if (address == IntPtr.Zero)
                {
                    throw new EntryPointNotFoundException(entryPoint);
                }

                return Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate)) as TDelegate;
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_moduleHandle == IntPtr.Zero)
                {
                    return;
                }

                NativeMethods.FreeLibrary(_moduleHandle);
                _moduleHandle = IntPtr.Zero;
            }
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FreeLibrary(IntPtr hModule);
        }
    }
}
