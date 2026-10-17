using System;
using System.IO;
using System.Runtime.InteropServices;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.Tts;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Zdsr
{
    public sealed class ZdsrScreenReaderService : IScreenReaderService
    {
        private const int ReaderMainChannelType = 0;
        private const int IndependentChannelType = 1;
        private const string DefaultIndependentChannelName = "MyCodex";
        private const string LibraryRelativePath = @"tts\ZDSRAPI\ZDSRAPI_x64.dll";
        private readonly object _syncRoot = new object();
        private readonly IZdsrNativeApi _nativeApi;
        private bool _isInitialized;
        private bool _isDisposed;
        private int? _activeChannelType;
        private string _activeChannelName;
        private bool? _activeKeyDownInterrupt;
        private string _independentChannelName;
        private string _lastError;

        public ZdsrScreenReaderService()
            : this(new ZdsrNativeApi(GetLibraryFullPath()))
        {
        }

        internal ZdsrScreenReaderService(IZdsrNativeApi nativeApi)
        {
            _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));
            _independentChannelName = DefaultIndependentChannelName;
        }

        public bool IsAvailable
        {
            get
            {
                return Environment.Is64BitProcess &&
                       _nativeApi.IsAvailable;
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

                return EnsureChannelInitialized(true, true);
            }
        }

        public void ApplyIndependentChannelName(string channelName)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                _independentChannelName = NormalizeChannelName(channelName) ?? DefaultIndependentChannelName;
                _lastError = null;
            }
        }

        public bool IsReaderRunning()
        {
            var state = GetReaderState();
            return state == (int)ZdsrSpeakState.Speaking || state == (int)ZdsrSpeakState.Idle;
        }

        public int GetReaderState()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (!EnsureInitialized())
                {
                    return 0;
                }

                try
                {
                    var result = _nativeApi.GetSpeakState();
                    if (result == (int)ZdsrSpeakState.NotRunningOrUnauthorized)
                    {
                        _lastError = L("Zdsr.Error.NotRunningOrUnauthorized", "ZDSR is not running or not authorized.");
                    }
                    else if (result == (int)ZdsrSpeakState.VersionMismatch)
                    {
                        _lastError = L("Zdsr.Error.VersionMismatch", "ZDSR API version mismatch.");
                    }
                    else
                    {
                        _lastError = null;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    _lastError = L("Zdsr.Error.ReadStateFailed.Prefix", "Failed to read ZDSR state: ") + ex.Message;
                    return 0;
                }
            }
        }

        public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(text))
            {
                lock (_syncRoot)
                {
                    _lastError = L("Zdsr.Error.SpeakTextRequired", "Text to read cannot be empty.");
                }

                return false;
            }

            lock (_syncRoot)
            {
                if (!EnsureChannelInitialized(useSlave, allowBreak))
                {
                    return false;
                }

                try
                {
                    var interrupt = !append;
                    var result = _nativeApi.Speak(text, interrupt);
                    if (result == ZdsrResult.Success)
                    {
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildSpeakErrorMessage(result);
                    return false;
                }
                catch (Exception ex)
                {
                    _lastError = L("Zdsr.Error.SpeakInvokeFailed.Prefix", "Failed to invoke ZDSR speech: ") + ex.Message;
                    return false;
                }
            }
        }

        public bool Stop(bool useSlave)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (!EnsureChannelInitialized(useSlave, true))
                {
                    return false;
                }

                try
                {
                    _nativeApi.StopSpeak();
                    _lastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    _lastError = L("Zdsr.Error.StopFailed.Prefix", "Failed to stop ZDSR speech: ") + ex.Message;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            _nativeApi.Dispose();
            _isDisposed = true;
        }

        private static string GetLibraryFullPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibraryRelativePath);
        }

        private bool EnsureChannelInitialized(bool useSlave, bool keyDownInterrupt)
        {
            if (!EnsureNativeApiLoaded())
            {
                return false;
            }

            var channelType = useSlave ? IndependentChannelType : ReaderMainChannelType;
            var channelName = useSlave ? GetIndependentChannelName() : null;
            if (_isInitialized &&
                _activeChannelType == channelType &&
                string.Equals(_activeChannelName, channelName, StringComparison.Ordinal) &&
                _activeKeyDownInterrupt == keyDownInterrupt)
            {
                return true;
            }

            try
            {
                var result = _nativeApi.InitTts(channelType, channelName, keyDownInterrupt);
                if (result == ZdsrResult.Success)
                {
                    _isInitialized = true;
                    _activeChannelType = channelType;
                    _activeChannelName = channelName;
                    _activeKeyDownInterrupt = keyDownInterrupt;
                    _lastError = null;
                    return true;
                }

                _lastError = BuildInitErrorMessage(result);
                return false;
            }
            catch (DllNotFoundException ex)
            {
                _lastError = L("Zdsr.Error.LoadFailed.Prefix", "Failed to load ZDSR API: ") + ex.Message;
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                _lastError = L("Zdsr.Error.VersionMismatch.Prefix", "ZDSR API version mismatch: ") + ex.Message;
                return false;
            }
            catch (BadImageFormatException ex)
            {
                _lastError = L("Zdsr.Error.BitnessMismatch.Prefix", "ZDSR API bitness mismatch: ") + ex.Message;
                return false;
            }
        }

        private bool EnsureNativeApiLoaded()
        {
            if (!IsAvailable)
            {
                _lastError = string.Format(
                    L("Zdsr.Error.LibraryNotFound.Template", "{0} was not found."),
                    Path.GetFileName(LibraryRelativePath));
                return false;
            }

            string loadError;
            if (_nativeApi.EnsureLoaded(out loadError))
            {
                return true;
            }

            _lastError = L("Zdsr.Error.LoadFailed.Prefix", "Failed to load ZDSR API: ") + loadError;
            return false;
        }

        private string GetIndependentChannelName()
        {
            return string.IsNullOrWhiteSpace(_independentChannelName)
                ? DefaultIndependentChannelName
                : _independentChannelName;
        }

        private static string NormalizeChannelName(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return null;
            }

            return channelName
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static string BuildInitErrorMessage(int result)
        {
            return result == ZdsrResult.VersionMismatch
                ? L("Zdsr.Error.VersionMismatch", "ZDSR API version mismatch.")
                : L("Zdsr.Error.InitializeFailed", "Failed to initialize ZDSR.");
        }

        private static string BuildSpeakErrorMessage(int result)
        {
            switch (result)
            {
                case ZdsrResult.VersionMismatch:
                    return L("Zdsr.Error.VersionMismatch", "ZDSR API version mismatch.");
                case ZdsrResult.NotRunningOrUnauthorized:
                    return L("Zdsr.Error.NotRunningOrUnauthorized", "ZDSR is not running or not authorized.");
                default:
                    return L("Zdsr.Error.SpeakFailed", "ZDSR speech failed.");
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

        private static class ZdsrResult
        {
            public const int Success = 0;
            public const int VersionMismatch = 1;
            public const int NotRunningOrUnauthorized = 2;
        }

        private enum ZdsrSpeakState
        {
            VersionMismatch = 1,
            NotRunningOrUnauthorized = 2,
            Speaking = 3,
            Idle = 4
        }

        private sealed class ZdsrNativeApi : IZdsrNativeApi
        {
            private readonly string _libraryFullPath;
            private readonly TtsNativeLibrary _nativeLibrary;
            private InitTtsDelegate _initTts;
            private SpeakDelegate _speak;
            private GetSpeakStateDelegate _getSpeakState;
            private StopSpeakDelegate _stopSpeak;

            public ZdsrNativeApi(string libraryFullPath)
            {
                _libraryFullPath = libraryFullPath;
                _nativeLibrary = new TtsNativeLibrary(libraryFullPath);
            }

            public bool IsAvailable
            {
                get
                {
                    return File.Exists(_libraryFullPath);
                }
            }

            public bool EnsureLoaded(out string error)
            {
                error = null;
                if (_initTts != null)
                {
                    return true;
                }

                string loadError;
                if (!_nativeLibrary.EnsureLoaded(out loadError))
                {
                    error = loadError;
                    return false;
                }

                try
                {
                    _initTts = _nativeLibrary.GetDelegate<InitTtsDelegate>("InitTTS");
                    _speak = _nativeLibrary.GetDelegate<SpeakDelegate>("Speak");
                    _getSpeakState = _nativeLibrary.GetDelegate<GetSpeakStateDelegate>("GetSpeakState");
                    _stopSpeak = _nativeLibrary.GetDelegate<StopSpeakDelegate>("StopSpeak");
                    return true;
                }
                catch (EntryPointNotFoundException ex)
                {
                    error = L("Zdsr.Error.VersionMismatch.Prefix", "ZDSR API version mismatch: ") + ex.Message;
                    return false;
                }
                catch (BadImageFormatException ex)
                {
                    error = L("Zdsr.Error.BitnessMismatch.Prefix", "ZDSR API bitness mismatch: ") + ex.Message;
                    return false;
                }
            }

            public int InitTts(int type, string channelName, bool keyDownInterrupt)
            {
                return _initTts(type, channelName, keyDownInterrupt);
            }

            public int Speak(string text, bool interrupt)
            {
                return _speak(text, interrupt);
            }

            public int GetSpeakState()
            {
                return _getSpeakState();
            }

            public void StopSpeak()
            {
                _stopSpeak();
            }

            public void Dispose()
            {
                _nativeLibrary.Dispose();
            }

            private delegate int InitTtsDelegate(
                int type,
                [MarshalAs(UnmanagedType.LPWStr)] string channelName,
                [MarshalAs(UnmanagedType.Bool)] bool keyDownInterrupt);

            private delegate int SpeakDelegate(
                [MarshalAs(UnmanagedType.LPWStr)] string text,
                [MarshalAs(UnmanagedType.Bool)] bool interrupt);

            private delegate int GetSpeakStateDelegate();

            private delegate void StopSpeakDelegate();
        }
    }

    internal interface IZdsrNativeApi : IDisposable
    {
        bool IsAvailable { get; }

        bool EnsureLoaded(out string error);

        int InitTts(int type, string channelName, bool keyDownInterrupt);

        int Speak(string text, bool interrupt);

        int GetSpeakState();

        void StopSpeak();
    }
}
