using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SecureRelayChannelLifecycleTests
    {
        [Test]
        public void InitialChannel_WaitsForClientHelloWithoutStartingHandshakeDeadline()
        {
            using (var socket = new ClientWebSocket())
            using (var channel = new SecureRelayChannel(
                socket,
                new UnusedIdentityStore(),
                new UnusedBindingStore(),
                null,
                new RemoteDeviceBinding
                {
                    TenantId = "tenant",
                    HostId = "host",
                    DeviceId = "device"
                }))
            {
                Assert.That(channel.HandshakeDeadlineUtc, Is.Null);
            }
        }

        private sealed class UnusedIdentityStore : IRemoteIdentityStore
        {
            public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId) { throw new NotSupportedException(); }
            public byte[] Sign(byte[] transcript) { throw new NotSupportedException(); }
            public byte[] DeriveAgreement(byte[] peerPublicKey) { throw new NotSupportedException(); }
        }

        private sealed class UnusedBindingStore : IRemoteBindingStore
        {
            public IReadOnlyList<RemoteDeviceBinding> ListActive(string tenantId, string hostId) { throw new NotSupportedException(); }
            public RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId) { throw new NotSupportedException(); }
            public void SavePending(RemoteDeviceBinding binding) { throw new NotSupportedException(); }
            public void Activate(string claimId, int bindingVersion) { throw new NotSupportedException(); }
            public void Revoke(string tenantId, string hostId, string deviceId) { throw new NotSupportedException(); }
        }
    }
}
