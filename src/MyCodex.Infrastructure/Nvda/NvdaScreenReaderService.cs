using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.Tts;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Nvda
{
    public sealed class NvdaScreenReaderService : IScreenReaderService
    {
        private const string LibraryRelativePath = @"tts\nvdaControllerClient\nvdaControllerClient.dll";
        private readonly object _syncRoot = new object();
        private readonly TtsNativeLibrary _nativeLibrary;
        private TestIfRunningDelegate _testIfRunning;
        private SpeakTextDelegate _speakText;
        private CancelSpeechDelegate _cancelSpeech;
        private bool _isInitialized;
        private bool _isDisposed;
        private string _lastError;

        public NvdaScreenReaderService()
        {
            _nativeLibrary = new TtsNativeLibrary(GetLibraryFullPath());
        }

        public bool IsAvailable
        {
            get
            {
                return Environment.Is64BitProcess && File.Exists(GetLibraryFullPath());
            }
        }

        public bool IsInitialized
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isInitialized;
                }
            }
        }

        public string LastError
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lastError;
                }
            }
        }

        public bool EnsureInitialized()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_isInitialized)
                {
                    return true;
                }

                if (!IsAvailable)
                {
                    _lastError = string.Format(
                        L("Nvda.Error.LibraryNotFound.Template", "{0} was not found."),
                        Path.GetFileName(GetLibraryFullPath()));
                    return false;
                }

                if (!EnsureNativeMethodsLoaded())
                {
                    return false;
                }

                _isInitialized = true;
                _lastError = null;
                return true;
            }
        }

        public bool IsReaderRunning()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (!EnsureInitialized())
                {
                    return false;
                }

                try
                {
                    var result = _testIfRunning();
                    if (result == 0)
                    {
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildWin32ErrorMessage(
                        result,
                        L("Nvda.Error.CommunicationFailed", "Failed to communicate with NVDA"));
                    return false;
                }
                catch (Exception ex)
                {
                    _lastError = L("Nvda.Error.ReadStateFailed.Prefix", "Failed to detect NVDA state: ") + ex.Message;
                    return false;
                }
            }
        }

        public int GetReaderState()
        {
            return IsReaderRunning() ? 4 : 0;
        }

        public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(text))
            {
                lock (_syncRoot)
                {
                    _lastError = L("Nvda.Error.SpeakTextRequired", "Text to read cannot be empty.");
                }

                return false;
            }

            lock (_syncRoot)
            {
                if (!EnsureInitialized())
                {
                    return false;
                }

                try
                {
                    if (!append)
                    {
                        var cancelResult = _cancelSpeech();
                        if (cancelResult != 0)
                        {
                            _lastError = BuildWin32ErrorMessage(
                                cancelResult,
                                L("Nvda.Error.StopExistingSpeechFailed", "Failed to stop current NVDA speech"));
                            return false;
                        }
                    }

                    var result = _speakText(text);
                    if (result == 0)
                    {
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildWin32ErrorMessage(
                        result,
                        L("Nvda.Error.SpeakInvokeFailed", "Failed to invoke NVDA speech"));
                    return false;
                }
                catch (Exception ex)
                {
                    _lastError = L("Nvda.Error.SpeakInvokeFailed.Prefix", "Failed to invoke NVDA speech: ") + ex.Message;
                    return false;
                }
            }
        }

        public bool Stop(bool useSlave)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (!EnsureInitialized())
                {
                    return false;
                }

                try
                {
                    var result = _cancelSpeech();
                    if (result == 0)
                    {
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildWin32ErrorMessage(
                        result,
                        L("Nvda.Error.StopFailed", "Failed to stop NVDA speech"));
                    return false;
                }
                catch (Exception ex)
                {
                    _lastError = L("Nvda.Error.StopFailed.Prefix", "Failed to stop NVDA speech: ") + ex.Message;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _nativeLibrary.Dispose();
                _isDisposed = true;
            }
        }

        private static string GetLibraryFullPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibraryRelativePath);
        }

        private bool EnsureNativeMethodsLoaded()
        {
            if (_testIfRunning != null)
            {
                return true;
            }

            string loadError;
            if (!_nativeLibrary.EnsureLoaded(out loadError))
            {
                _lastError = L("Nvda.Error.LoadFailed.Prefix", "Failed to load NVDA API: ") + loadError;
                return false;
            }

            try
            {
                var testIfRunning = _nativeLibrary.GetDelegate<TestIfRunningDelegate>("nvdaController_testIfRunning");
                var speakText = _nativeLibrary.GetDelegate<SpeakTextDelegate>("nvdaController_speakText");
                var cancelSpeech = _nativeLibrary.GetDelegate<CancelSpeechDelegate>("nvdaController_cancelSpeech");
                _testIfRunning = testIfRunning;
                _speakText = speakText;
                _cancelSpeech = cancelSpeech;
                return true;
            }
            catch (EntryPointNotFoundException ex)
            {
                _lastError = L("Nvda.Error.VersionMismatch.Prefix", "NVDA API version mismatch: ") + ex.Message;
                return false;
            }
            catch (BadImageFormatException ex)
            {
                _lastError = L("Nvda.Error.BitnessMismatch.Prefix", "NVDA API bitness mismatch: ") + ex.Message;
                return false;
            }
        }

        private static string BuildWin32ErrorMessage(int code, string prefix)
        {
            try
            {
                return string.Format(
                    L("Nvda.Error.WithSystemMessage.Template", "{0}: {1}"),
                    prefix,
                    new Win32Exception(code).Message);
            }
            catch
            {
                return string.Format(
                    L("Nvda.Error.WithCode.Template", "{0}: error code {1}"),
                    prefix,
                    code);
            }
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int TestIfRunningDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        private delegate int SpeakTextDelegate([MarshalAs(UnmanagedType.LPWStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CancelSpeechDelegate();
    }
}
