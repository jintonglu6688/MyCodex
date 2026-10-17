using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class RelayHostRealtimeClient : IRelayHostRealtimeClient
    {
        private readonly object _sync = new object();
        private readonly RemoteCommandDispatcher _dispatcher;
        private readonly SecureRelayAuthenticator _authenticator;
        private readonly IRemoteBindingStore _bindings;
        private readonly IRemoteConversationSnapshotInvalidator _snapshotInvalidator;
        private readonly Dictionary<string, ActiveConnection> _connections = new Dictionary<string, ActiveConnection>(StringComparer.Ordinal);
        private CancellationTokenSource _lifetime;

        public RelayHostRealtimeClient() : this(null, null, null) { }
        public RelayHostRealtimeClient(RemoteCommandDispatcher dispatcher) : this(dispatcher, null, null) { }

        public RelayHostRealtimeClient(RemoteCommandDispatcher dispatcher, SecureRelayAuthenticator authenticator, IRemoteBindingStore bindingStore, IRemoteConversationSnapshotInvalidator snapshotInvalidator = null)
        {
            _dispatcher = dispatcher;
            _authenticator = authenticator;
            _bindings = bindingStore;
            _snapshotInvalidator = snapshotInvalidator;
        }

        public event EventHandler<RelayRealtimeConnectionStatusEventArgs> StatusChanged;

        public Task StartAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Relay host ID is required.", nameof(hostId));
            if (_authenticator == null || _bindings == null) throw new InvalidOperationException("Secure Relay authentication is required.");
            var active = _bindings.ListActive(config.TenantId, hostId).Where(binding => binding != null && !string.IsNullOrWhiteSpace(binding.DeviceId)).ToArray();
            Trace.TraceInformation(
                "[RemoteRelay] Host realtime start. Host={0}; ActiveDevices={1}",
                RemoteRelayDiagnostics.ShortId(hostId),
                active.Length);
            lock (_sync)
            {
                StopLocked();
                _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                foreach (var binding in active)
                {
                    var token = _lifetime.Token;
                    Task.Run(() => RunDeviceLoopAsync(Clone(config), binding, token), token);
                }
            }
            return Task.FromResult(0);
        }

        public Task StopAsync()
        {
            lock (_sync) StopLocked();
            Trace.TraceInformation("[RemoteRelay] Host realtime stopped.");
            RaiseStatus(RelayRealtimeConnectionState.Disconnected, null);
            return Task.FromResult(0);
        }

        public async Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken)
        {
            if (remoteEvent == null) return;
            ActiveConnection[] connections;
            lock (_sync) connections = _connections.Values.ToArray();
            var delivered = 0;
            foreach (var connection in connections)
            {
                if (!connection.Channel.IsEstablished)
                {
                    continue;
                }

                // One device's broken channel must not stop delivery to the other paired devices;
                // the failed channel closes itself and its device loop reconnects.
                try
                {
                    await connection.Channel.PublishAsync(remoteEvent, cancellationToken).ConfigureAwait(false);
                    delivered++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        "[RemoteRelay] Event publish to one device failed. Type={0}; Error={1}",
                        remoteEvent.EventType ?? "-",
                        RemoteRelayDiagnostics.ExceptionSummary(ex));
                }
            }
            Trace.TraceInformation(
                "[RemoteRelay] Event publish. Type={0}; Request={1}; Delivered={2}",
                remoteEvent.EventType ?? "-",
                RemoteRelayDiagnostics.ShortId(remoteEvent.RequestId),
                delivered);
        }

        public void Dispose() { lock (_sync) StopLocked(); }

        private async Task RunDeviceLoopAsync(RelayHostConfig config, RemoteDeviceBinding binding, CancellationToken cancellationToken)
        {
            var attempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                ClientWebSocket socket = null;
                SecureRelayChannel channel = null;
                try
                {
                    attempt++;
                    Trace.TraceInformation(
                        "[RemoteRelay] Connection attempt. Device={0}; Attempt={1}",
                        RemoteRelayDiagnostics.ShortId(binding.DeviceId),
                        attempt);
                    RaiseStatus(RelayRealtimeConnectionState.Connecting, null);
                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    using (var transportBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        transportBudget.CancelAfter(TimeSpan.FromSeconds(8));
                        var ticket = await _authenticator.GetTicketAsync(config, RelayTicketPurpose.WebSocketHost, binding.HostId, binding.DeviceId, transportBudget.Token).ConfigureAwait(false);
                        Trace.TraceInformation("[RemoteRelay] Authentication ticket acquired. Device={0}", RemoteRelayDiagnostics.ShortId(binding.DeviceId));
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
                        await socket.ConnectAsync(BuildWebSocketUri(config, binding.HostId, binding.DeviceId, null), transportBudget.Token).ConfigureAwait(false);
                        Trace.TraceInformation("[RemoteRelay] WebSocket connected. Device={0}", RemoteRelayDiagnostics.ShortId(binding.DeviceId));
                    }

                    channel = new SecureRelayChannel(socket, GetIdentityStore(), _bindings, _dispatcher, binding);
                    SetConnection(binding.DeviceId, socket, channel);
                    RaiseStatus(RelayRealtimeConnectionState.Connected, null);
                    while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        string text;
                        var deadline = channel.HandshakeDeadlineUtc;
                        if (deadline.HasValue)
                        {
                            using (var handshakeBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                var remaining = deadline.Value - DateTime.UtcNow;
                                if (remaining <= TimeSpan.Zero) throw new TimeoutException("Secure Relay handshake timed out.");
                                handshakeBudget.CancelAfter(remaining);
                                text = await ReceiveTextAsync(socket, handshakeBudget.Token).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            text = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
                        }
                        if (text == null) break;
                        var wasEstablished = channel.IsEstablished;
                        await channel.HandleAsync(text, cancellationToken).ConfigureAwait(false);
                        if (!wasEstablished && channel.IsEstablished)
                        {
                            Trace.TraceInformation("[RemoteRelay] Secure handshake established. Device={0}", RemoteRelayDiagnostics.ShortId(binding.DeviceId));
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Trace.TraceWarning(
                        "[RemoteRelay] Connection failed. Device={0}; Attempt={1}; Error={2}",
                        RemoteRelayDiagnostics.ShortId(binding.DeviceId),
                        attempt,
                        RemoteRelayDiagnostics.ExceptionSummary(ex, config.TenantSecret));
                    if (!HasConnections()) RaiseStatus(RelayRealtimeConnectionState.Failed, "Secure remote connection failed.");
                }
                finally
                {
                    Trace.TraceInformation("[RemoteRelay] Connection ended. Device={0}; State={1}", RemoteRelayDiagnostics.ShortId(binding.DeviceId), socket == null ? "not_created" : socket.State.ToString());
                    ClearConnection(binding.DeviceId, socket);
                    if (channel != null) channel.Dispose();
                    if (socket != null) socket.Dispose();
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            if (!HasConnections()) RaiseStatus(RelayRealtimeConnectionState.Disconnected, null);
        }

        // Kept separate so MainForm controls the one long-lived identity store shared with pairing/authentication.
        private IRemoteIdentityStore GetIdentityStore()
        {
            return _authenticator.IdentityStore;
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using (var stream = new MemoryStream())
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return null;
                    if (result.MessageType != WebSocketMessageType.Text || stream.Length + result.Count > SecureRelayChannel.MaxWireFrameBytes)
                    {
                        socket.Abort();
                        throw new FormatException("The Relay WebSocket frame is invalid.");
                    }
                    stream.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage) break;
                }
                return new UTF8Encoding(false, true).GetString(stream.ToArray());
            }
        }

        private void SetConnection(string deviceId, ClientWebSocket socket, SecureRelayChannel channel)
        {
            lock (_sync)
            {
                ActiveConnection existing;
                if (_connections.TryGetValue(deviceId, out existing)) existing.Channel.Dispose();
                _connections[deviceId] = new ActiveConnection(socket, channel);
            }
        }

        private void ClearConnection(string deviceId, ClientWebSocket socket)
        {
            lock (_sync)
            {
                ActiveConnection current;
                if (_connections.TryGetValue(deviceId, out current) && ReferenceEquals(current.Socket, socket)) _connections.Remove(deviceId);
            }
        }

        private bool HasConnections() { lock (_sync) return _connections.Count != 0; }

        private void StopLocked()
        {
            if (_snapshotInvalidator != null) _snapshotInvalidator.Clear();
            if (_lifetime != null) { _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = null; }
            foreach (var connection in _connections.Values) connection.Channel.Dispose();
            _connections.Clear();
        }

        private void RaiseStatus(RelayRealtimeConnectionState state, string message)
        {
            var handler = StatusChanged;
            if (handler != null) handler(this, new RelayRealtimeConnectionStatusEventArgs(state, message));
        }

        private static Uri BuildWebSocketUri(RelayHostConfig config, string hostId, string deviceId, string ignoredSessionId)
        {
            var connectionHost = string.IsNullOrWhiteSpace(config.ConnectionHost) ? config.RelayHost : config.ConnectionHost;
            var connectionPort = config.ConnectionPort > 0 ? config.ConnectionPort : config.RelayPort;
            var tls = string.IsNullOrWhiteSpace(config.ConnectionHost) ? config.TlsRequired : config.ConnectionTlsRequired;
            return new Uri((tls ? "wss" : "ws") + "://" + connectionHost.Trim().TrimEnd('/') + ":" + connectionPort.ToString(CultureInfo.InvariantCulture) +
                "/v1/ws?connection=host&tenantId=" + Uri.EscapeDataString(config.TenantId) + "&hostId=" + Uri.EscapeDataString(hostId) + "&deviceId=" + Uri.EscapeDataString(deviceId));
        }

        private static RelayHostConfig Clone(RelayHostConfig value)
        {
            return new RelayHostConfig { RelayHost=value.RelayHost, ConnectionHost=value.ConnectionHost, ConnectionPort=value.ConnectionPort, ConnectionTlsRequired=value.ConnectionTlsRequired, RelayPort=value.RelayPort, TlsRequired=value.TlsRequired, CertificateSha256=value.CertificateSha256, RelaySigningPublicKey=value.RelaySigningPublicKey, RelaySigningKeyFingerprint=value.RelaySigningKeyFingerprint, TenantId=value.TenantId, TenantSecret=value.TenantSecret };
        }

        private sealed class ActiveConnection
        {
            public ActiveConnection(ClientWebSocket socket, SecureRelayChannel channel) { Socket=socket; Channel=channel; }
            public ClientWebSocket Socket { get; private set; }
            public SecureRelayChannel Channel { get; private set; }
        }
    }
}
