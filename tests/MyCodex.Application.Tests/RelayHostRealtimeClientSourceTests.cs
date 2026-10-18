using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RelayHostRealtimeClientSourceTests
    {
        [Test]
        public void WebSocketSends_UsePerChannelSecureSendGate()
        {
            var source = RepositorySource.ReadSourceFile("RelayHostRealtimeClient.cs");

            Assert.That(source, Does.Contain("new SecureRelayChannel("));
            Assert.That(source, Does.Not.Contain("RelayEnvelope request"));
            Assert.That(source, Does.Not.Contain("sessionId=\" + Uri.EscapeDataString"));
            Assert.That(source, Does.Contain("RelayTicketPurpose.WebSocketHost"));
            Assert.That(source, Does.Not.Contain("\"Bearer \" + config.TenantSecret"));
        }

        [Test]
        public void BuildWebSocketUri_EmbeddedUsesConnectionHost()
        {
            var uri = BuildWebSocketUri(new RelayHostConfig
            {
                RelayHost = "192.168.1.50",
                ConnectionHost = "127.0.0.1",
                ConnectionPort = 39221,
                ConnectionTlsRequired = false,
                RelayPort = 38443,
                TlsRequired = true,
                TenantId = "tenant_local"
            });

            Assert.That(uri.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(uri.Port, Is.EqualTo(39221));
            Assert.That(uri.Scheme, Is.EqualTo("ws"));
        }

        [Test]
        public void BuildWebSocketUri_ExternalFallsBackToRelayHost()
        {
            var uri = BuildWebSocketUri(new RelayHostConfig
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                TlsRequired = true,
                TenantId = "tenant_external"
            });

            Assert.That(uri.Host, Is.EqualTo("relay.example.com"));
        }

        [Test]
        public void PublishAsync_ContinuesToOtherDevicesWhenOneChannelFails()
        {
            var bindings = new ThrowingBindingStore();
            var client = new RelayHostRealtimeClient(null, null, bindings);
            var connections = (System.Collections.IDictionary)typeof(RelayHostRealtimeClient)
                .GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(client);
            var connectionType = typeof(RelayHostRealtimeClient).GetNestedType("ActiveConnection", BindingFlags.NonPublic);
            var sockets = new List<ClientWebSocket>();
            try
            {
                foreach (var deviceId in new[] { "device_a", "device_b" })
                {
                    var socket = new ClientWebSocket();
                    sockets.Add(socket);
                    var channel = new SecureRelayChannel(
                        socket,
                        new UnusedIdentityStore(),
                        bindings,
                        null,
                        new RemoteDeviceBinding { TenantId = "tenant", HostId = "host", DeviceId = deviceId });
                    var stateField = typeof(SecureRelayChannel).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
                    stateField.SetValue(channel, Enum.Parse(stateField.FieldType, "Established"));
                    connections[deviceId] = Activator.CreateInstance(
                        connectionType,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new object[] { socket, channel },
                        null);
                }

                Assert.That(
                    () => client.PublishAsync(
                        new RemoteRelayEvent { EventType = "test.event", Payload = new Dictionary<string, object>() },
                        CancellationToken.None).GetAwaiter().GetResult(),
                    Throws.Nothing);
                Assert.That(bindings.FindActiveCalls, Is.EqualTo(2));
            }
            finally
            {
                client.Dispose();
                foreach (var socket in sockets)
                {
                    socket.Dispose();
                }
            }
        }

        private sealed class ThrowingBindingStore : IRemoteBindingStore
        {
            public int FindActiveCalls { get; private set; }

            public IReadOnlyList<RemoteDeviceBinding> ListActive(string tenantId, string hostId) { throw new NotSupportedException(); }

            public RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId)
            {
                FindActiveCalls++;
                throw new InvalidOperationException("Binding store unavailable.");
            }

            public RemoteDeviceBinding FindPending(string claimId) { throw new NotSupportedException(); }
            public void SavePending(RemoteDeviceBinding binding) { throw new NotSupportedException(); }
            public void Activate(string claimId, int bindingVersion) { throw new NotSupportedException(); }
            public void Revoke(string tenantId, string hostId, string deviceId) { throw new NotSupportedException(); }
        }

        private sealed class UnusedIdentityStore : IRemoteIdentityStore
        {
            public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId) { throw new NotSupportedException(); }
            public byte[] Sign(byte[] transcript) { throw new NotSupportedException(); }
            public byte[] DeriveAgreement(byte[] peerPublicKey) { throw new NotSupportedException(); }
        }

        private static Uri BuildWebSocketUri(RelayHostConfig config)
        {
            var method = typeof(RelayHostRealtimeClient).GetMethod("BuildWebSocketUri", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (Uri)method.Invoke(null, new object[] { config, "host_a", "device_a", "session_a" });
        }
    }
}
