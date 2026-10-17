using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Tts;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.ByCtrl
{
    public sealed class ByCtrlScreenReaderService : IScreenReaderService
    {
        private const int SpeakChunkUtf8Bytes = 4800;
        private const int PreferredBreakThresholdBytes = 3072;
        private const string LibraryRelativePath = @"tts\byctrl\byctrl-x64.dll";

        private readonly object _syncRoot;
        private readonly TtsNativeLibrary _nativeLibrary;
        private InitializeDelegate _initialize;
        private Speak2Delegate _speak2;
        private StopSpeaking2Delegate _stopSpeaking2;
        private IsReaderRunningDelegate _isReaderRunning;
        private GetReaderStateDelegate _getReaderState;
        private UninitializeDelegate _uninitialize;
        private string _independentChannelName;
        private bool _isInitialized;
        private bool _isDisposed;
        private string _lastError;

        public ByCtrlScreenReaderService(AppConfigService appConfigService)
        {
            if (appConfigService == null)
            {
                throw new ArgumentNullException(nameof(appConfigService));
            }

            _syncRoot = new object();
            _nativeLibrary = new TtsNativeLibrary(GetLibraryFullPath());
        }

        public bool IsAvailable
        {
            get
            {
                return Environment.Is64BitProcess &&
                       File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibraryRelativePath));
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

        public void ApplyIndependentChannelName(string channelName)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                _independentChannelName = NormalizeChannelName(channelName);
                _lastError = null;
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
                        L("ByCtrl.Error.LibraryNotFound.Template", "{0} was not found."),
                        GetLibraryFileName());
                    return false;
                }

                try
                {
                    if (!EnsureNativeMethodsLoaded())
                    {
                        return false;
                    }

                    var result = _initialize(null);
                    if (result == ByCtrlError.Success)
                    {
                        _isInitialized = true;
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildErrorMessage(L("ByCtrl.Operation.Initialize", "Initialize"), result);
                    return false;
                }
                catch (DllNotFoundException ex)
                {
                    _lastError = L("ByCtrl.Error.LoadFailed.Prefix", "Failed to load byctrl: ") + ex.Message;
                    return false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    _lastError = L("ByCtrl.Error.VersionMismatch.Prefix", "byctrl API version mismatch: ") + ex.Message;
                    return false;
                }
                catch (BadImageFormatException ex)
                {
                    _lastError = L("ByCtrl.Error.BitnessMismatch.Prefix", "byctrl API bitness mismatch: ") + ex.Message;
                    return false;
                }
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
                    return _isReaderRunning != null && _isReaderRunning();
                }
                catch (Exception ex)
                {
                    _lastError = L("ByCtrl.Error.ReadStateFailed.Prefix", "Failed to read screen reader state: ") + ex.Message;
                    return false;
                }
            }
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
                    return _getReaderState != null ? _getReaderState() : 0;
                }
                catch (Exception ex)
                {
                    _lastError = L("ByCtrl.Error.ReadDetailStateFailed.Prefix", "Failed to read detailed screen reader state: ") + ex.Message;
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
                    _lastError = L("ByCtrl.Error.SpeakTextRequired", "Text to read cannot be empty.");
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
                    var chunks = SplitSpeakText(text);
                    if (chunks.Length == 0)
                    {
                        _lastError = L("ByCtrl.Error.SpeakTextRequired", "Text to read cannot be empty.");
                        return false;
                    }

                    for (var i = 0; i < chunks.Length; i++)
                    {
                        var chunkAppend = i == 0 ? append : true;
                        var result = _speak2(
                            chunks[i],
                            useSlave,
                            GetSlaveName(useSlave),
                            chunkAppend,
                            allowBreak,
                            IntPtr.Zero);
                        if (result == ByCtrlError.Success)
                        {
                            continue;
                        }

                        _lastError = BuildErrorMessage(L("ByCtrl.Operation.Speak", "Speak"), result);
                        return false;
                    }

                    _lastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    _lastError = L("ByCtrl.Error.SpeakInvokeFailed.Prefix", "Failed to invoke screen reader: ") + ex.Message;
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
                    var result = _stopSpeaking2(useSlave, GetSlaveName(useSlave));
                    if (result == ByCtrlError.Success)
                    {
                        _lastError = null;
                        return true;
                    }

                    _lastError = BuildErrorMessage(L("ByCtrl.Operation.Stop", "Stop reading"), result);
                    return false;
                }
                catch (Exception ex)
                {
                    _lastError = L("ByCtrl.Error.StopFailed.Prefix", "Failed to stop reading: ") + ex.Message;
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                if (_isInitialized)
                {
                    try
                    {
                        if (_uninitialize != null)
                        {
                            _uninitialize();
                        }
                    }
                    catch
                    {
                    }

                    _isInitialized = false;
                }

                _nativeLibrary.Dispose();
                _isDisposed = true;
            }
        }

        private static string GetLibraryFileName()
        {
            return Path.GetFileName(LibraryRelativePath);
        }

        private static string GetLibraryFullPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibraryRelativePath);
        }

        private bool EnsureNativeMethodsLoaded()
        {
            if (_initialize != null)
            {
                return true;
            }

            string loadError;
            if (!_nativeLibrary.EnsureLoaded(out loadError))
            {
                _lastError = L("ByCtrl.Error.LoadFailed.Prefix", "Failed to load byctrl: ") + loadError;
                return false;
            }

            try
            {
                _initialize = _nativeLibrary.GetDelegate<InitializeDelegate>("BoyCtrlInitialize");
                _speak2 = _nativeLibrary.GetDelegate<Speak2Delegate>("BoyCtrlSpeak2");
                _stopSpeaking2 = _nativeLibrary.GetDelegate<StopSpeaking2Delegate>("BoyCtrlStopSpeaking2");
                _isReaderRunning = _nativeLibrary.GetDelegate<IsReaderRunningDelegate>("BoyCtrlIsReaderRunning");
                _getReaderState = _nativeLibrary.GetDelegate<GetReaderStateDelegate>("BoyCtrlGetReaderState");
                _uninitialize = _nativeLibrary.GetDelegate<UninitializeDelegate>("BoyCtrlUninitialize");
                return true;
            }
            catch (EntryPointNotFoundException ex)
            {
                _lastError = L("ByCtrl.Error.VersionMismatch.Prefix", "byctrl API version mismatch: ") + ex.Message;
                return false;
            }
            catch (BadImageFormatException ex)
            {
                _lastError = L("ByCtrl.Error.BitnessMismatch.Prefix", "byctrl API bitness mismatch: ") + ex.Message;
                return false;
            }
        }

        private string GetSlaveName(bool useSlave)
        {
            return useSlave ? _independentChannelName : null;
        }

        private static string BuildErrorMessage(string operation, ByCtrlError error)
        {
            switch (error)
            {
                case ByCtrlError.Success:
                    return null;
                case ByCtrlError.Arg:
                    return string.Format(
                        L("ByCtrl.Error.OperationInvalidArgument.Template", "{0} failed: invalid argument."),
                        operation);
                case ByCtrlError.Unavailable:
                    return string.Format(
                        L("ByCtrl.Error.OperationUnavailable.Template", "{0} failed: byctrl service is unavailable."),
                        operation);
                default:
                    return string.Format(
                        L("ByCtrl.Error.OperationFailed.Template", "{0} failed."),
                        operation);
            }
        }

        private static string[] SplitSpeakText(string text)
        {
            var normalized = NormalizeSpeakText(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new string[0];
            }

            if (Encoding.UTF8.GetByteCount(normalized) <= SpeakChunkUtf8Bytes)
            {
                return new[] { normalized };
            }

            var chunks = new List<string>();
            var remaining = normalized;
            while (!string.IsNullOrEmpty(remaining))
            {
                var chunk = TakeSpeakChunk(remaining, SpeakChunkUtf8Bytes);
                if (string.IsNullOrEmpty(chunk))
                {
                    chunk = FitTextToUtf8ByteLimit(remaining, SpeakChunkUtf8Bytes);
                }

                if (string.IsNullOrEmpty(chunk))
                {
                    break;
                }

                chunks.Add(chunk);
                remaining = remaining.Substring(chunk.Length);
            }

            return chunks.ToArray();
        }

        private static string NormalizeSpeakText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Trim('\n');
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

        private static string TakeSpeakChunk(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0)
            {
                return string.Empty;
            }

            var byteCount = 0;
            var safeIndex = 0;
            var lastBoundaryIndex = -1;
            var lastBoundaryBytes = 0;

            for (var index = 0; index < text.Length; index++)
            {
                var nextIndex = index + 1;
                string segment;
                if (char.IsHighSurrogate(text[index]) &&
                    index + 1 < text.Length &&
                    char.IsLowSurrogate(text[index + 1]))
                {
                    segment = text.Substring(index, 2);
                    nextIndex = index + 2;
                    index++;
                }
                else
                {
                    segment = text[index].ToString();
                }

                var segmentBytes = Encoding.UTF8.GetByteCount(segment);
                if (byteCount + segmentBytes > maxBytes)
                {
                    break;
                }

                byteCount += segmentBytes;
                safeIndex = nextIndex;

                if (IsPreferredBreakChar(text[nextIndex - 1]))
                {
                    lastBoundaryIndex = nextIndex;
                    lastBoundaryBytes = byteCount;
                }
            }

            var cutIndex = lastBoundaryIndex > 0 && lastBoundaryBytes >= PreferredBreakThresholdBytes
                ? lastBoundaryIndex
                : safeIndex;

            return cutIndex > 0 ? text.Substring(0, cutIndex) : string.Empty;
        }

        private static string FitTextToUtf8ByteLimit(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || maxBytes <= 0)
            {
                return string.Empty;
            }

            var byteCount = 0;
            var endIndex = 0;
            for (var index = 0; index < text.Length; index++)
            {
                var nextIndex = index + 1;
                string segment;
                if (char.IsHighSurrogate(text[index]) &&
                    index + 1 < text.Length &&
                    char.IsLowSurrogate(text[index + 1]))
                {
                    segment = text.Substring(index, 2);
                    nextIndex = index + 2;
                    index++;
                }
                else
                {
                    segment = text[index].ToString();
                }

                var segmentBytes = Encoding.UTF8.GetByteCount(segment);
                if (byteCount + segmentBytes > maxBytes)
                {
                    break;
                }

                byteCount += segmentBytes;
                endIndex = nextIndex;
            }

            return endIndex > 0 ? text.Substring(0, endIndex) : string.Empty;
        }

        private static bool IsPreferredBreakChar(char value)
        {
            switch (value)
            {
                case '\n':
                case '\t':
                case ' ':
                case '\u3002':
                case '\uFF01':
                case '\uFF1F':
                case '\uFF1A':
                case '\uFF0C':
                case '\u3001':
                case '.':
                case '!':
                case '?':
                case ';':
                case ':':
                case ',':
                case ')':
                case '\uFF09':
                case ']':
                case '\u3011':
                case '>':
                case '\u300B':
                    return true;
                default:
                    return false;
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

        private enum ByCtrlError
        {
            Success = 0,
            Fail = 1,
            Arg = 2,
            Unavailable = 3
        }

        private delegate ByCtrlError InitializeDelegate([MarshalAs(UnmanagedType.LPWStr)] string logPath);

        private delegate ByCtrlError Speak2Delegate(
            [MarshalAs(UnmanagedType.LPWStr)] string text,
            [MarshalAs(UnmanagedType.I1)] bool useSlave,
            [MarshalAs(UnmanagedType.LPWStr)] string slaveName,
            [MarshalAs(UnmanagedType.I1)] bool append,
            [MarshalAs(UnmanagedType.I1)] bool allowBreak,
            IntPtr onCompletion);

        private delegate ByCtrlError StopSpeaking2Delegate(
            [MarshalAs(UnmanagedType.I1)] bool useSlave,
            [MarshalAs(UnmanagedType.LPWStr)] string slaveName);

        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool IsReaderRunningDelegate();

        private delegate int GetReaderStateDelegate();

        private delegate void UninitializeDelegate();
    }
}
