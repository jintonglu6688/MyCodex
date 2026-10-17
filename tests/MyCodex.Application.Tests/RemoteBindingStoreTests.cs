using System;
using System.IO;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteBindingStoreTests
    {
        [Test]
        public void Constructor_RequiresExplicitPath()
        {
            Assert.That(typeof(JsonRemoteBindingStore).GetConstructor(Type.EmptyTypes), Is.Null);
        }

        [Test]
        public void PendingBinding_DoesNotAuthorize()
        {
            var path = CreatePath();
            var store = new JsonRemoteBindingStore(path);
            store.SavePending(CreateBinding());

            Assert.That(store.FindActive("tenant", "host", "device"), Is.Null);
        }

        [Test]
        public void ActiveBinding_AuthorizesExactKeysAndVersion()
        {
            var path = CreatePath();
            var store = new JsonRemoteBindingStore(path);
            var binding = CreateBinding();
            store.SavePending(binding);
            store.Activate(binding.ClaimId, 1);

            var active = store.FindActive("tenant", "host", "device");
            Assert.That(active, Is.Not.Null);
            Assert.That(active.DeviceSigningPublicKey, Is.EqualTo("signing"));
            Assert.That(active.DeviceAgreementPublicKey, Is.EqualTo("agreement"));
            Assert.That(active.DeviceKeyVersion, Is.EqualTo(2));
            Assert.That(active.BindingVersion, Is.EqualTo(1));
        }

        [Test]
        public void RevokedBinding_DoesNotAuthorize()
        {
            var path = CreatePath();
            var store = new JsonRemoteBindingStore(path);
            var binding = CreateBinding();
            store.SavePending(binding);
            store.Activate(binding.ClaimId, 1);
            store.Revoke("tenant", "host", "device");

            Assert.That(store.FindActive("tenant", "host", "device"), Is.Null);
        }

        [Test]
        public void OldConfigAndBindingData_AreDiscarded()
        {
            var path = CreatePath();
            File.WriteAllText(path, "{\"version\":0,\"bindings\":[{\"tenantId\":\"tenant\",\"hostId\":\"host\",\"deviceId\":\"device\",\"state\":\"active\"}]}");

            var store = new JsonRemoteBindingStore(path);

            Assert.That(store.FindActive("tenant", "host", "device"), Is.Null);
        }

        [Test]
        public void RePairing_AssignsNextVersionAndRevokesPreviousActiveBinding()
        {
            var path = CreatePath();
            var store = new JsonRemoteBindingStore(path);
            var first = CreateBinding();
            first.ClaimId = "claim-1";
            store.SavePending(first);
            store.Activate(first.ClaimId, first.BindingVersion);

            var second = CreateBinding();
            second.ClaimId = "claim-2";
            store.SavePending(second);
            store.Activate(second.ClaimId, second.BindingVersion);

            var active = store.FindActive("tenant", "host", "device");
            Assert.That(first.BindingVersion, Is.EqualTo(1));
            Assert.That(second.BindingVersion, Is.EqualTo(2));
            Assert.That(active.ClaimId, Is.EqualTo("claim-2"));
            Assert.That(active.BindingVersion, Is.EqualTo(2));
        }

        [Test]
        public void SavePending_RejectsChangedIdentityForExistingDevice()
        {
            var path = CreatePath();
            var store = new JsonRemoteBindingStore(path);
            var first = CreateBinding();
            first.ClaimId = "claim-1";
            store.SavePending(first);
            store.Activate(first.ClaimId, first.BindingVersion);
            var changed = CreateBinding();
            changed.ClaimId = "claim-2";
            changed.DeviceSigningPublicKey = "changed-signing";

            Assert.Throws<InvalidOperationException>(() => store.SavePending(changed));
        }

        [Test]
        public void CorruptBindingData_FailsClosed()
        {
            var path = CreatePath();
            File.WriteAllText(path, "{\"version\":1,\"bindings\":[");
            var store = new JsonRemoteBindingStore(path);

            Assert.Throws<InvalidOperationException>(
                () => store.FindActive("tenant", "host", "device"));
        }

        private static RemoteDeviceBinding CreateBinding()
        {
            return new RemoteDeviceBinding
            {
                ClaimId = "claim",
                TenantId = "tenant",
                HostId = "host",
                DeviceId = "device",
                DeviceSigningPublicKey = "signing",
                DeviceAgreementPublicKey = "agreement",
                DeviceKeyVersion = 2
            };
        }

        private static string CreatePath()
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "remote-bindings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "bindings.json");
        }
    }
}
