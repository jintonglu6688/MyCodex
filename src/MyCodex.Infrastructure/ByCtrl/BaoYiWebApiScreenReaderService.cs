using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.ByCtrl
{
    public sealed class BaoYiWebApiScreenReaderService : IScreenReaderService
    {
        private const string WebSocketUrl = "ws://127.0.0.1:26609/api";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(2500);

        private readonly object _syncRoot = new object();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly IBaoYiWebApiScreenReaderTransport _transport;
        private bool _isInitialized;
        private bool _isDisposed;
        private string _independentChannelName;
        private string _lastError;

        public BaoYiWebApiScreenReaderService()
            : this(new ClientWebSocketBaoYiWebApiTransport())
        {
        }

        internal BaoYiWebApiScreenReaderService(IBaoYiWebApiScreenReaderTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public bool IsAvailable
        {
            get
            {
                return true;
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

                var status = SendStatusRequest();
                if (status >= 0)
                {
                    _isInitialized = true;
                    _lastError = null;
                    return true;
                }

                return false;
            }
        }

        public bool IsReaderRunning()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                // Status 0 means idle, not disconnected. A live status response is the probe.
                return SendStatusRequest() >= 0;
            }
        }

        public int GetReaderState()
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                var status = SendStatusRequest();
                return status == 1 ? 1 : 0;
            }
        }

        public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
        {
            ThrowIfDisposed();

            var normalizedText = NormalizeSpeakText(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                lock (_syncRoot)
                {
                    _lastError = "Text to read cannot be empty.";
                }

                return false;
            }

            lock (_syncRoot)
            {
                var payload = CreateBasePayload("speech.speak", useSlave);
                payload["text"] = normalizedText;
                payload["append"] = append;
                return SendAndValidate(payload, "speech.speak", "Speak");
            }
        }

        public bool Stop(bool useSlave)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (!_isInitialized)
                {
                    _lastError = null;
                    return true;
                }

                var payload = CreateBasePayload("speech.stop", useSlave);
                return SendAndValidate(payload, "speech.stop", "Stop reading");
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

                _transport.Dispose();
                _isDisposed = true;
            }
        }

        private int SendStatusRequest()
        {
            var payload = CreateBasePayload("speech.status", true);
            Dictionary<string, object> response;
            if (!TrySend(payload, "speech.status", "Read status", out response))
            {
                return -1;
            }

            object statusValue;
            if (response.TryGetValue("status", out statusValue))
            {
                try
                {
                    _lastError = null;
                    return Convert.ToInt32(statusValue);
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
            }

            _lastError = "Read status failed: response did not include a valid status.";
            _isInitialized = false;
            return -1;
        }

        private bool SendAndValidate(Dictionary<string, object> payload, string expectedAction, string operation)
        {
            Dictionary<string, object> response;
            return TrySend(payload, expectedAction, operation, out response);
        }

        private bool TrySend(
            Dictionary<string, object> payload,
            string expectedAction,
            string operation,
            out Dictionary<string, object> response)
        {
            response = null;
            _isInitialized = false;
            try
            {
                var requestJson = _serializer.Serialize(payload);
                var responseJson = _transport.Send(requestJson, RequestTimeout);
                response = _serializer.Deserialize<Dictionary<string, object>>(responseJson ?? string.Empty);
                if (response == null)
                {
                    _lastError = operation + " failed: empty response.";
                    return false;
                }

                var action = GetStringValue(response, "action");
                if (!string.IsNullOrEmpty(action) && !string.Equals(action, expectedAction, StringComparison.Ordinal))
                {
                    _lastError = operation + " failed: unexpected response action.";
                    return false;
                }

                var error = GetStringValue(response, "error");
                if (string.Equals(error, "OK", StringComparison.Ordinal))
                {
                    _isInitialized = true;
                    _lastError = null;
                    return true;
                }

                _lastError = operation + " failed: " + (string.IsNullOrEmpty(error) ? "unknown error" : error) + ".";
                return false;
            }
            catch (Exception ex)
            {
                _lastError = operation + " failed: " + ex.Message;
                return false;
            }
        }

        private Dictionary<string, object> CreateBasePayload(string action, bool useSlave)
        {
            var payload = new Dictionary<string, object>();
            payload["action"] = action;
            payload["use_slave"] = useSlave;
            var slaveName = GetSlaveName(useSlave);
            if (!string.IsNullOrWhiteSpace(slaveName))
            {
                payload["slave_name"] = slaveName;
            }

            return payload;
        }

        private string GetSlaveName(bool useSlave)
        {
            return useSlave ? _independentChannelName : null;
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

        private static string GetStringValue(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private sealed class ClientWebSocketBaoYiWebApiTransport : IBaoYiWebApiScreenReaderTransport
        {
            public string Send(string requestJson, TimeSpan timeout)
            {
                return SendAsync(requestJson, timeout).GetAwaiter().GetResult();
            }

            public void Dispose()
            {
            }

            private static async Task<string> SendAsync(string requestJson, TimeSpan timeout)
            {
                using (var cancellationTokenSource = new CancellationTokenSource(timeout))
                using (var socket = new ClientWebSocket())
                {
                    await socket.ConnectAsync(new Uri(WebSocketUrl), cancellationTokenSource.Token).ConfigureAwait(false);
                    var requestBytes = Encoding.UTF8.GetBytes(requestJson ?? string.Empty);
                    await socket.SendAsync(
                        new ArraySegment<byte>(requestBytes),
                        WebSocketMessageType.Text,
                        true,
                        cancellationTokenSource.Token).ConfigureAwait(false);

                    return await ReceiveTextAsync(socket, cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }

            private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
            {
                var buffer = new byte[4096];
                using (var stream = new MemoryStream())
                {
                    while (true)
                    {
                        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return null;
                        }

                        if (result.MessageType != WebSocketMessageType.Text)
                        {
                            throw new InvalidOperationException("BaoYi Web API returned a non-text WebSocket message.");
                        }

                        stream.Write(buffer, 0, result.Count);
                        if (result.EndOfMessage)
                        {
                            break;
                        }
                    }

                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }
    }

    internal interface IBaoYiWebApiScreenReaderTransport : IDisposable
    {
        string Send(string requestJson, TimeSpan timeout);
    }
}
