using System;
using System.IO;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedRemoteServiceSettingsTests
    {
        private string _root;
        private string _sharedPath;
        private string _identityPath;
        private SharedRelayStateStore _ownership;
        private SeparatedRemoteServiceConfigStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shared-settings-" + Guid.NewGuid().ToString("N"));
            _sharedPath = Path.Combine(_root, "config", "relay-service.json");
            _identityPath = Path.Combine(_root, "target", "remote-service.json");
            _ownership = new SharedRelayStateStore(_root, identity => true);
            _store = new SeparatedRemoteServiceConfigStore(_sharedPath, _identityPath, _ownership);
            var shared = _store.LoadShared();
            shared.RemoteServiceMode = "Embedded";
            shared.RelayHost = "original.example";
            shared.RelayPort = 38443;
            shared.EncryptedAccessSecret = "preserved-secret";
            shared.RelaySigningKeyFingerprint = "preserved-pin";
            _store.SaveShared(shared);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ConfirmedSave_PreservesConcurrentHelperMetadataAndTargetIdentity()
        {
            var identity = _store.LoadTargetIdentity();
            identity.HostId = "e1497f3b-76a0-4c18-870f-a2d6863e2f63";
            identity.ThisPcName = "Target A";
            identity.RemoteEnabled = true;
            _store.SaveTargetIdentity(identity);
            var identityBefore = File.ReadAllBytes(_identityPath);
            var draft = _store.LoadShared();
            var helper = new SeparatedRemoteServiceConfigStore(_sharedPath, _identityPath, _ownership);
            var metadata = helper.LoadShared();
            metadata.EmbeddedRelayProcessId = 42;
            metadata.EmbeddedRelayCertificateSha256 = "fresh-certificate";
            helper.SaveSharedFromService(metadata);
            AddLease("a");
            AddLease("b");

            draft.EmbeddedRelayPublicHostOverride = "public.example";
            _store.SaveSharedForManagement(draft, new[] { "b", "a" });

            var saved = _store.LoadShared();
            Assert.That(saved.EmbeddedRelayPublicHostOverride, Is.EqualTo("public.example"));
            Assert.That(saved.EmbeddedRelayProcessId, Is.EqualTo(42));
            Assert.That(saved.EmbeddedRelayCertificateSha256, Is.EqualTo("fresh-certificate"));
            Assert.That(saved.EncryptedAccessSecret, Is.EqualTo("preserved-secret"));
            Assert.That(saved.RelaySigningKeyFingerprint, Is.EqualTo("preserved-pin"));
            Assert.That(File.ReadAllBytes(_identityPath), Is.EqualTo(identityBefore));
            _ownership.WithLock(state =>
            {
                Assert.That(state.SettingsRevision, Is.EqualTo(1));
                Assert.That(state.Ready, Is.False);
                Assert.That(state.Error, Is.Null);
                Assert.That(state.Leases.Count, Is.EqualTo(2));
                return true;
            }, CancellationToken.None);
        }

        [Test]
        public void ConfirmedSave_ChangedUsersRejectsWriteAndPreservesRevision()
        {
            var draft = _store.LoadShared();
            var before = File.ReadAllBytes(_sharedPath);
            AddLease("unconfirmed-user");
            draft.EmbeddedRelayPublicHostOverride = "changed.example";

            Assert.Throws<InvalidOperationException>(() => _store.SaveSharedForManagement(draft, new string[0]));

            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(before));
            Assert.That(_ownership.WithLock(state => state.SettingsRevision, CancellationToken.None), Is.Zero);
            Assert.That(File.Exists(_identityPath), Is.False);
        }

        [Test]
        public void ConfirmedSave_ConflictDoesNotOverwriteAnotherEditorsSettings()
        {
            var stale = _store.LoadShared();
            var current = _store.LoadShared();
            current.EmbeddedRelayPublicHostOverride = "first.example";
            _store.SaveSharedForManagement(current, new string[0]);
            stale.EmbeddedRelayPublicHostOverride = "second.example";

            Assert.Throws<InvalidOperationException>(() => _store.SaveSharedForManagement(stale, new string[0]));

            Assert.That(_store.LoadShared().EmbeddedRelayPublicHostOverride, Is.EqualTo("first.example"));
            Assert.That(_ownership.WithLock(state => state.SettingsRevision, CancellationToken.None), Is.EqualTo(1));
        }

        [Test]
        public void ConfirmedSave_UnchangedDraftDoesNotRequestRelayRestart()
        {
            AddLease("a");
            var draft = _store.LoadShared();

            _store.SaveSharedForManagement(draft, new[] { "a" });

            _ownership.WithLock(state =>
            {
                Assert.That(state.SettingsRevision, Is.Zero);
                Assert.That(state.Ready, Is.True);
                Assert.That(state.Error, Is.EqualTo("existing-status"));
                return true;
            }, CancellationToken.None);
            Assert.That(File.Exists(_identityPath), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConfirmedSave_InactiveProfileOrStartupPreferenceDoesNotRestartRunningEmbeddedRelay(bool changeStartupPreference)
        {
            AddLease("a");
            var draft = _store.LoadShared();
            if (changeStartupPreference)
                draft.StartEmbeddedRelayWithApplication = !draft.StartEmbeddedRelayWithApplication;
            else
                draft.ExternalProfile.RelayHost = "inactive-external.example";

            _store.SaveSharedForManagement(draft, new[] { "a" });

            var saved = _store.LoadShared();
            if (changeStartupPreference)
                Assert.That(saved.StartEmbeddedRelayWithApplication, Is.True);
            else
                Assert.That(saved.ExternalProfile.RelayHost, Is.EqualTo("inactive-external.example"));
            _ownership.WithLock(state =>
            {
                Assert.That(state.SettingsRevision, Is.Zero);
                Assert.That(state.Ready, Is.True);
                return true;
            }, CancellationToken.None);
        }

        [Test]
        public void SharedEditor_SaveDoesNotCreateSyntheticTargetIdentity()
        {
            var config = new AppConfigService(Path.Combine(_root, "editor"), _root, "MyCodex", _root);
            var editor = new SharedRemoteServiceSettingsStore(config, new TargetCatalogStore(config), "test-shared-settings");
            var draft = editor.Load();
            RemoteServiceConfigPlanner.EnsureDefaults(draft, "Synthetic editor");
            draft.EmbeddedRelayPublicHostOverride = "edited.example";

            Assert.Throws<InvalidOperationException>(() => editor.Save(draft));
            editor.SaveConfirmed(draft, editor.GetUsage());

            Assert.That(_store.LoadShared().EmbeddedRelayPublicHostOverride, Is.EqualTo("edited.example"));
            Assert.That(File.Exists(config.GetRemoteServiceConfigFilePath()), Is.False);
            Assert.That(File.ReadAllText(_sharedPath), Does.Not.Contain("HostId"));
        }

        [Test]
        public void SharedEditor_FailedSaveRetainsPairingImpactWarningOnRetry()
        {
            var config = new AppConfigService(Path.Combine(_root, "editor"), _root, "MyCodex", _root);
            var editor = new SharedRemoteServiceSettingsStore(config, new TargetCatalogStore(config), "test-shared-settings");
            var draft = editor.Load();
            RemoteServiceConfigPlanner.SelectMode(draft, "External");
            draft.TenantId = "replacement-tenant";
            draft.EmbeddedRelayPublicHostOverride = "requested.example";
            var concurrent = _store.LoadShared();
            concurrent.EmbeddedRelayPublicHostOverride = "concurrent.example";
            _store.SaveShared(concurrent);

            Assert.That(editor.PairingIdentityChanged(draft), Is.True);
            Assert.Throws<InvalidOperationException>(() => editor.SaveConfirmed(draft, editor.GetUsage()));

            Assert.That(editor.PairingIdentityChanged(draft), Is.True);
            Assert.That(_store.LoadShared().RemoteServiceMode, Is.EqualTo("Embedded"));
        }

        [Test]
        public void SharedEditor_ReapplyingSavedExternalSettingsDoesNotResetTransportDefaults()
        {
            var config = new AppConfigService(Path.Combine(_root, "editor"), _root, "MyCodex", _root);
            var editor = new SharedRemoteServiceSettingsStore(config, new TargetCatalogStore(config), "test-shared-settings");
            var draft = editor.Load();
            RemoteServiceConfigPlanner.SelectMode(draft, "External");
            draft.RelayHost = "external.example";
            draft.UseTls = false;
            editor.SaveConfirmed(draft, editor.GetUsage());

            RemoteServiceConfigPlanner.EnsureDefaults(draft, "Synthetic editor");
            Assert.That(draft.UseTls, Is.False);
            editor.SaveConfirmed(draft, editor.GetUsage());

            Assert.That(_store.LoadShared().UseTls, Is.False);
            Assert.That(File.Exists(config.GetRemoteServiceConfigFilePath()), Is.False);
        }

        private void AddLease(string id)
        {
            _ownership.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease
                {
                    Id = id,
                    User = new SharedRelayProcessRecord { ProcessId = 42, ExecutablePath = "client.exe", StartTimeUtcTicks = 123 }
                });
                state.Ready = true;
                state.Error = "existing-status";
                return true;
            }, CancellationToken.None);
        }
    }
}
