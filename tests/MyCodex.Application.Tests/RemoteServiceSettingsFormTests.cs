using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class RemoteServiceSettingsFormTests
    {
        [Test]
        public void Constructor_FocusableControlsHaveAccessibleNames()
        {
            using (var form = CreateForm())
            {
                var missing = FindFocusableControlsWithoutAccessibleName(form);

                Assert.That(missing, Is.Empty);
            }
        }

        [Test]
        public void Constructor_DefaultsToEmbeddedModeAndFocusesModeSelection()
        {
            using (var form = CreateForm())
            {
                Assert.That(form.SelectedServiceModeForTests, Is.EqualTo("Embedded"));
                Assert.That(form.InitialFocusControlForTests, Is.TypeOf<ComboBox>());
            }
        }

        [Test]
        public async Task Save_WhenEmbeddedServerIsRunning_CanSwitchWithoutManualStopRequirement()
        {
            var store = new FakeRemoteServiceConfigStore
            {
                Snapshot = new RemoteServiceConfigSnapshot
                {
                    RemoteServiceMode = "Embedded",
                    EmbeddedRelayEnabled = true
                }
            };
            using (var form = CreateForm(store: store))
            {
                var modeComboBox = FindComboBoxByAccessibleName(
                    form,
                    UiLocalization.Text("RemoteServiceSettingsForm.Accessible.ServerMode", "Server mode"));

                Assert.That(modeComboBox, Is.Not.Null);
                Assert.That(modeComboBox.Enabled, Is.True);
                form.SetServiceModeForTests("External");
                await form.ClickSaveForTests();

                Assert.That(store.SaveCount, Is.EqualTo(1));
                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo("External"));
            }
        }

        [Test]
        public void Constructor_WhenChineseUiCulture_UsesChineseButtonAccessibleNames()
        {
            using (UseCulture("zh-CN"))
            using (var form = CreateForm())
            {
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.Save", "Save")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.Cancel", "Cancel")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.ImportServerInfo", "Import server information")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.DetectServer", "Detect server")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, "\u4FDD\u5B58\u5E76\u8FDE\u63A5"), Is.Null);
                Assert.That(FindButtonByAccessibleName(form, "\u4EC5\u4FDD\u5B58"), Is.Null);
            }
        }

        [Test]
        public void ImportServerInfo_LoadsDeploymentJsonWithoutSaving()
        {
            var store = new FakeRemoteServiceConfigStore();
            var screenReader = new FakeScreenReaderService();
            using (var form = CreateForm(store: store, screenReader: screenReader))
            {
                form.ClipboardReaderForTests = () =>
                    "{\"relayHost\":\"relay.example.com\",\"relayPort\":443,\"tlsRequired\":true," +
                    "\"tenants\":[{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"enabled\":true}]," +
                    "\"secret\":{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"value\":\"secret-value\"}}";

                form.ClickImportServerInfoForTests();

                Assert.That(form.SelectedServiceModeForTests, Is.EqualTo("External"));
                Assert.That(form.RelayHostForTests, Is.EqualTo("relay.example.com"));
                Assert.That(form.RelayPortForTests, Is.EqualTo(443));
                Assert.That(form.UseTlsForTests, Is.True);
                Assert.That(form.TenantIdForTests, Is.EqualTo("11111111-1111-4111-8111-111111111111"));
                Assert.That(form.AccessSecretForTests, Is.EqualTo("secret-value"));
                Assert.That(
                    form.ServerInfoStatusForTests,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.Imported",
                        "Server information was imported. Review the settings and save.")));
                Assert.That(screenReader.LastSpokenText, Is.EqualTo(form.ServerInfoStatusForTests));
                Assert.That(store.ProtectedSecret, Is.Null);
            }
        }

        [Test]
        public async Task DetectServer_ReadsPublicMetadataWithoutTenantCredentials()
        {
            var client = new FakeRelayHostClient
            {
                Metadata = new RelayMetadata
                {
                    ProtocolVersion = 1,
                    SigningKeyFingerprint = "relay-fingerprint",
                    MaxMessageBytes = 11534336
                }
            };
            var screenReader = new FakeScreenReaderService();
            using (var form = CreateForm(client, screenReader: screenReader))
            {
                form.SetConnectionFieldsForTests(
                    "relay.example.com",
                    443,
                    true,
                    "11111111-1111-4111-8111-111111111111",
                    "tenant-secret",
                    "Windows");

                await form.ClickDetectServerForTests();

                Assert.That(client.Calls, Is.EqualTo(new[] { "GetMetadata" }));
                Assert.That(client.MetadataConfig.RelayHost, Is.EqualTo("relay.example.com"));
                Assert.That(client.MetadataConfig.RelayPort, Is.EqualTo(443));
                Assert.That(client.MetadataConfig.TlsRequired, Is.True);
                Assert.That(client.MetadataConfig.TenantId, Is.Null);
                Assert.That(client.MetadataConfig.TenantSecret, Is.Null);
                Assert.That(form.ServerInfoStatusForTests, Does.Contain("relay-fingerprint"));
                Assert.That(screenReader.LastSpokenText, Is.EqualTo(form.ServerInfoStatusForTests));
            }
        }

        [Test]
        public async Task Save_PersistsConfigWithoutCallingRelayOrTts()
        {
            var store = new FakeRemoteServiceConfigStore();
            var client = new FakeRelayHostClient();
            var screenReader = new FakeScreenReaderService();
            using (var form = CreateForm(client, store, screenReader))
            {
                form.SetServiceModeForTests("External");
                form.SetConnectionFieldsForTests("relay.example.com", 443, true, "tenant_a", "tenant-secret", "Windows");

                await form.ClickSaveForTests();

                Assert.That(client.Calls, Is.Empty);
                Assert.That(store.Snapshot.RelayHost, Is.EqualTo("relay.example.com"));
                Assert.That(store.Snapshot.TenantId, Is.EqualTo("tenant_a"));
                Assert.That(store.ProtectedSecret, Is.EqualTo("tenant-secret"));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(
                    screenReader.LastSpokenText,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceSettingsForm.Announcement.ExternalMode",
                        "Independent public service settings")));
            }
        }

        [Test]
        public async Task Save_WhenEndpointIsUnchanged_PreservesRelayIdentityPins()
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetConnectionFieldsForTests(
                    store.Snapshot.RelayHost,
                    store.Snapshot.RelayPort,
                    store.Snapshot.UseTls,
                    store.Snapshot.TenantId,
                    "********",
                    store.Snapshot.ThisPcName);

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RelaySigningPublicKey, Is.EqualTo("relay-signing-key"));
                Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.EqualTo("relay-signing-fingerprint"));
            }
        }

        [TestCase("changed.example.com", 443, true)]
        [TestCase("relay.example.com", 8443, true)]
        [TestCase("relay.example.com", 443, false)]
        public async Task Save_WhenEndpointChanges_ClearsRelayIdentityPins(
            string relayHost,
            int relayPort,
            bool useTls)
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetConnectionFieldsForTests(
                    relayHost,
                    relayPort,
                    useTls,
                    store.Snapshot.TenantId,
                    "********",
                    store.Snapshot.ThisPcName);

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RelaySigningPublicKey, Is.Null);
                Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.Null);
            }
        }

        [Test]
        public async Task Save_EmbeddedPublicAddressStoresExplicitOverride()
        {
            var store = new FakeRemoteServiceConfigStore
            {
                Snapshot = new RemoteServiceConfigSnapshot
                {
                    RemoteServiceMode = "Embedded",
                    RelayHost = "192.168.1.50",
                    RelayPort = 38443,
                    UseTls = true
                }
            };
            using (var form = CreateForm(store: store))
            {
                form.SetConnectionFieldsForTests(
                    "a.jtl.world",
                    443,
                    true,
                    null,
                    null,
                    "Windows");

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo("Embedded"));
                Assert.That(store.Snapshot.RelayHost, Is.EqualTo("192.168.1.50"));
                Assert.That(store.Snapshot.EmbeddedRelayPublicHostOverride, Is.EqualTo("a.jtl.world"));
                Assert.That(store.Snapshot.EmbeddedRelayPublicPortOverride, Is.EqualTo(443));
            }
        }

        [Test]
        public async Task Save_ExternalModePersistsSelectionWithoutChangingEmbeddedPublicOverride()
        {
            var store = new FakeRemoteServiceConfigStore
            {
                Snapshot = new RemoteServiceConfigSnapshot
                {
                    RemoteServiceMode = "Embedded",
                    RelayHost = "a.jtl.world",
                    RelayPort = 443,
                    UseTls = true,
                    EmbeddedRelayPublicHostOverride = "a.jtl.world",
                    EmbeddedRelayPublicPortOverride = 443
                }
            };
            using (var form = CreateForm(store: store))
            {
                form.SetServiceModeForTests("External");
                form.SetConnectionFieldsForTests(
                    "relay.example.com",
                    443,
                    true,
                    "tenant_a",
                    "tenant-secret",
                    "Windows");

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo("External"));
                Assert.That(store.Snapshot.RelayHost, Is.EqualTo("relay.example.com"));
                Assert.That(store.Snapshot.EmbeddedRelayPublicHostOverride, Is.EqualTo("a.jtl.world"));
                Assert.That(store.Snapshot.EmbeddedRelayPublicPortOverride, Is.EqualTo(443));
            }
        }

        [Test]
        public async Task Save_WhenOnlyModeChanges_ClearsRelayIdentityPins()
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetServiceModeForTests("Embedded");
                form.SetConnectionFieldsForTests(
                    store.Snapshot.RelayHost,
                    store.Snapshot.RelayPort,
                    store.Snapshot.UseTls,
                    store.Snapshot.TenantId,
                    "********",
                    store.Snapshot.ThisPcName);

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RelaySigningPublicKey, Is.Null);
                Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.Null);
            }
        }

        [Test]
        public void ModeSelection_PreservesSeparateDraftsWithoutSaving()
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetConnectionFieldsForTests(
                    "external.example.com",
                    8443,
                    true,
                    "tenant_external",
                    "external-secret",
                    "Windows");

                form.SetServiceModeForTests("Embedded");
                form.SetConnectionFieldsForTests(
                    "192.168.1.60",
                    38443,
                    true,
                    null,
                    null,
                    "Windows");
                form.StartWithApplicationForTests = true;
                form.SetServiceModeForTests("External");

                Assert.That(form.RelayHostForTests, Is.EqualTo("external.example.com"));
                Assert.That(form.RelayPortForTests, Is.EqualTo(8443));
                Assert.That(form.TenantIdForTests, Is.EqualTo("tenant_external"));
                Assert.That(form.AccessSecretForTests, Is.EqualTo("external-secret"));
                Assert.That(form.TenantCredentialsVisibleForTests, Is.True);
                Assert.That(store.SaveCount, Is.EqualTo(0));

                form.SetServiceModeForTests("Embedded");
                Assert.That(form.RelayHostForTests, Is.EqualTo("192.168.1.60"));
                Assert.That(form.RelayPortForTests, Is.EqualTo(38443));
                Assert.That(form.StartWithApplicationForTests, Is.True);
                Assert.That(form.TenantCredentialsVisibleForTests, Is.False);
            }
        }

        [Test]
        public async Task Save_EmbeddedModePreservesExternalProfile()
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetServiceModeForTests("Embedded");
                form.SetConnectionFieldsForTests("192.168.1.60", 38443, true, null, null, "Windows");
                form.StartWithApplicationForTests = true;

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo("Embedded"));
                Assert.That(store.Snapshot.StartEmbeddedRelayWithApplication, Is.True);
                Assert.That(store.Snapshot.EmbeddedRelayPublicHostOverride, Is.EqualTo("192.168.1.60"));
                Assert.That(store.Snapshot.ExternalProfile.RelayHost, Is.EqualTo("relay.example.com"));
                Assert.That(store.Snapshot.ExternalProfile.TenantId, Is.EqualTo("tenant_a"));
                Assert.That(store.Snapshot.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected"));
            }
        }

        [Test]
        public async Task Save_EmbeddedModeAlsoPersistsEditedExternalDraft()
        {
            var store = CreatePinnedStore();
            using (var form = CreateForm(store: store))
            {
                form.SetConnectionFieldsForTests(
                    "updated.example.com",
                    8443,
                    true,
                    "tenant_updated",
                    "updated-secret",
                    "Windows");
                form.SetServiceModeForTests("Embedded");
                form.SetConnectionFieldsForTests("192.168.1.60", 38443, true, null, null, "Windows");

                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.ExternalProfile.RelayHost, Is.EqualTo("updated.example.com"));
                Assert.That(store.Snapshot.ExternalProfile.RelayPort, Is.EqualTo(8443));
                Assert.That(store.Snapshot.ExternalProfile.TenantId, Is.EqualTo("tenant_updated"));
                Assert.That(store.Snapshot.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected"));
                Assert.That(store.ProtectedSecret, Is.EqualTo("updated-secret"));
            }
        }

        [TestCase("changed.example.com", 443, true)]
        [TestCase("relay.example.com", 8443, true)]
        [TestCase("relay.example.com", 443, false)]
        public async Task SharedApply_EndpointMoveRetainsExistingRelayIdentityPins(string host, int port, bool useTls)
        {
            var store = CreatePinnedStore();
            using (var form = new RemoteServiceSettingsForm(store, new FakeRelayHostClient(), null, snapshot =>
            {
                store.Save(snapshot);
                return Task.FromResult(true);
            }))
            {
                form.SetConnectionFieldsForTests(host, port, useTls, "tenant_a", "********", "ignored");
                await form.ClickSaveForTests();

                Assert.That(store.Snapshot.RelayHost, Is.EqualTo(host));
                Assert.That(store.Snapshot.RelayPort, Is.EqualTo(port));
                Assert.That(store.Snapshot.UseTls, Is.EqualTo(useTls));
                Assert.That(store.Snapshot.RelaySigningPublicKey, Is.EqualTo("relay-signing-key"));
                Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.EqualTo("relay-signing-fingerprint"));
            }
        }

        [Test]
        public async Task SharedApply_SavesThroughCallbackAndKeepsStatusVisibleInOpenEditor()
        {
            var store = new FakeRemoteServiceConfigStore();
            using (var form = new RemoteServiceSettingsForm(store, new FakeRelayHostClient(), null, snapshot =>
            {
                store.Save(snapshot);
                return Task.FromResult(true);
            }))
            {
                await form.ClickSaveForTests();
                form.SetSharedServiceStatus("Shared service is running.");

                Assert.That(store.SaveCount, Is.EqualTo(1));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(form.ServerInfoStatusForTests, Is.EqualTo("Shared service is running."));
            }
        }

        [Test]
        public async Task SharedApply_DeclinedConfirmationDoesNotPersistDraft()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shared-editor-" + Guid.NewGuid().ToString("N"));
            try
            {
                var config = new AppConfigService(root);
                var store = new JsonRemoteServiceConfigStore(config);
                store.Save(new RemoteServiceConfigSnapshot { RemoteServiceMode = "Embedded", EmbeddedRelayPublicHostOverride = "original.example" });
                var before = File.ReadAllBytes(config.GetRemoteServiceConfigFilePath());
                using (var form = new RemoteServiceSettingsForm(store, new FakeRelayHostClient(), null, snapshot => Task.FromResult(false)))
                {
                    form.SetConnectionFieldsForTests("changed.example", 38443, true, null, null, "ignored");
                    await form.ClickSaveForTests();

                    Assert.That(File.ReadAllBytes(config.GetRemoteServiceConfigFilePath()), Is.EqualTo(before));
                    Assert.That(form.RelayHostForTests, Is.EqualTo("changed.example"));
                    Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                    Assert.That(form.IsDisposed, Is.False);
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task SharedApply_BusySaveDisablesCloseAndRejectsWindowClosing()
        {
            var pending = new TaskCompletionSource<bool>();
            using (var form = new RemoteServiceSettingsForm(new FakeRemoteServiceConfigStore(), new FakeRelayHostClient(), null, snapshot => pending.Task))
            {
                var save = form.ClickSaveForTests();
                var close = FindButtonByAccessibleName(form, UiLocalization.Text("SharedRemoteService.Accessible.Close", "Close"));
                var apply = FindButtonByAccessibleName(form, UiLocalization.Text("SharedRemoteService.Accessible.Apply", "Apply settings"));
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { closing });

                Assert.That(close.Enabled, Is.False);
                Assert.That(apply.Enabled, Is.False);
                Assert.That(closing.Cancel, Is.True);
                form.SetSharedServiceStatus("Background result");
                Assert.That(form.ServerInfoStatusForTests, Is.Not.EqualTo("Background result"));
                form.SetSharedServiceStatus("Applying shared settings", whileBusy: true);
                Assert.That(form.ServerInfoStatusForTests, Is.EqualTo("Applying shared settings"));
                pending.SetResult(true);
                await save;
                Assert.That(close.Enabled, Is.True);
                Assert.That(apply.Enabled, Is.True);
                Assert.That(form.IsDisposed, Is.False);
            }
        }

        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void SharedEditor_OmitsTargetIdentityAndPreservesNativeAccessibleTabOrder(string culture)
        {
            using (UseCulture(culture))
            using (var form = new RemoteServiceSettingsForm(new FakeRemoteServiceConfigStore(), new FakeRelayHostClient(), null, snapshot => Task.FromResult(false)))
            {
                Assert.That(form.Text, Is.EqualTo(UiLocalization.Text("SharedRemoteService.Title", "Phone Connection Settings")));
                Assert.That(FindFocusableControlsWithoutAccessibleName(form), Is.Empty);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(720));
                Assert.That(form.InitialFocusControlForTests, Is.TypeOf<ComboBox>());
                var modes = (ComboBox)form.InitialFocusControlForTests;
                Assert.That(modes.Items.Cast<string>(), Is.EqualTo(new[]
                {
                    UiLocalization.Text("RemoteServiceSettingsForm.Mode.Embedded", "Built-in service"),
                    UiLocalization.Text("RemoteServiceSettingsForm.Mode.External", "Public service")
                }));
                var layout = form.Controls.OfType<TableLayoutPanel>().Single();
                var description = layout.Controls.OfType<Label>().Single(label =>
                    label.Text == UiLocalization.Text("SharedRemoteService.Description", "These settings apply to all targets. Manage pairing and device access under Phone Connection in each target."));
                Assert.That(description.TabStop, Is.False);
                Assert.That(layout.GetRow(description), Is.Zero);
                Assert.That(layout.GetColumnSpan(description), Is.EqualTo(2));
                Assert.That(layout.GetRow(modes), Is.EqualTo(1));
                Assert.That(layout.Controls.OfType<FlowLayoutPanel>().Single().TabIndex, Is.GreaterThan(5));
                foreach (var name in new[] { "_thisPcNameTextBox", "_hostIdTextBox" })
                {
                    var identity = (Control)typeof(RemoteServiceSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Assert.That(identity.Parent, Is.Null, name);
                }
                var previousIndex = -1;
                foreach (var name in new[] { "_serverModeComboBox", "_serviceHostTextBox", "_portNumericUpDown", "_startWithApplicationCheckBox" })
                {
                    var control = (Control)typeof(RemoteServiceSettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Assert.That(control.TabStop, Is.True, name);
                    Assert.That(control.TabIndex, Is.GreaterThan(previousIndex), name);
                    previousIndex = control.TabIndex;
                }
                Assert.That(form.AcceptButton, Is.SameAs(FindButtonByAccessibleName(form, UiLocalization.Text("SharedRemoteService.Accessible.Apply", "Apply settings"))));
                Assert.That(form.CancelButton, Is.SameAs(FindButtonByAccessibleName(form, UiLocalization.Text("SharedRemoteService.Accessible.Close", "Close"))));
            }
        }

        private static FakeRemoteServiceConfigStore CreatePinnedStore()
        {
            var store = new FakeRemoteServiceConfigStore();
            store.Snapshot = new RemoteServiceConfigSnapshot
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                RemoteServiceMode = "External",
                TenantId = "tenant_a",
                EncryptedAccessSecret = "protected",
                ThisPcName = "Windows",
                RelaySigningPublicKey = "relay-signing-key",
                RelaySigningKeyFingerprint = "relay-signing-fingerprint"
            };
            return store;
        }

        private static RemoteServiceSettingsForm CreateForm(
            FakeRelayHostClient client = null,
            FakeRemoteServiceConfigStore store = null,
            FakeScreenReaderService screenReader = null)
        {
            return new RemoteServiceSettingsForm(
                store ?? new FakeRemoteServiceConfigStore(),
                client ?? new FakeRelayHostClient(),
                screenReader);
        }

        private static List<string> FindFocusableControlsWithoutAccessibleName(Control root)
        {
            var result = new List<string>();
            foreach (Control control in root.Controls)
            {
                result.AddRange(FindFocusableControlsWithoutAccessibleName(control));
            }

            if (root.TabStop && string.IsNullOrWhiteSpace(root.AccessibleName))
            {
                result.Add(root.GetType().Name);
            }

            return result;
        }

        private static Button FindButtonByAccessibleName(Control root, string accessibleName)
        {
            foreach (Control control in root.Controls)
            {
                var child = FindButtonByAccessibleName(control, accessibleName);
                if (child != null)
                {
                    return child;
                }
            }

            var button = root as Button;
            return button != null && string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal) ? button : null;
        }

        private static ComboBox FindComboBoxByAccessibleName(Control root, string accessibleName)
        {
            foreach (Control control in root.Controls)
            {
                var child = FindComboBoxByAccessibleName(control, accessibleName);
                if (child != null)
                {
                    return child;
                }
            }

            var comboBox = root as ComboBox;
            return comboBox != null && string.Equals(comboBox.AccessibleName, accessibleName, StringComparison.Ordinal)
                ? comboBox
                : null;
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }

        private sealed class FakeRemoteServiceConfigStore : IRemoteServiceConfigStore
        {
            public RemoteServiceConfigSnapshot Snapshot = new RemoteServiceConfigSnapshot();

            public string ProtectedSecret { get; private set; }

            public int SaveCount { get; private set; }

            public RemoteServiceConfigSnapshot Load()
            {
                return Snapshot;
            }

            public void Save(RemoteServiceConfigSnapshot snapshot)
            {
                Snapshot = snapshot;
                SaveCount++;
            }

            public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
            {
                ProtectedSecret = accessSecret;
                snapshot.EncryptedAccessSecret = string.IsNullOrWhiteSpace(accessSecret) ? null : "protected";
            }

            public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot)
            {
                return string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret) ? null : "tenant-secret";
            }

            public void Clear()
            {
                Snapshot = new RemoteServiceConfigSnapshot();
            }
        }

        private sealed class FakeScreenReaderService : IScreenReaderService
        {
            public bool IsAvailable { get { return true; } }

            public bool IsInitialized { get { return true; } }

            public string LastError { get { return null; } }

            public string LastSpokenText { get; private set; }

            public bool EnsureInitialized()
            {
                return true;
            }

            public bool IsReaderRunning()
            {
                return true;
            }

            public int GetReaderState()
            {
                return 0;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                LastSpokenText = text;
                return true;
            }

            public bool Stop(bool useSlave)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeRelayHostClient : IRelayHostClient
        {
            public readonly List<string> Calls = new List<string>();

            public RelayMetadata Metadata { get; set; }

            public RelayHostConfig MetadataConfig { get; private set; }

            public Task<RelayMetadata> GetMetadataAsync(RelayHostConfig config, CancellationToken cancellationToken)
            {
                Calls.Add("GetMetadata");
                MetadataConfig = config;
                return Task.FromResult(Metadata ?? new RelayMetadata());
            }

            public Task<RelayHostRegistrationResult> EnrollHostAsync(RelayHostConfig config, RemoteEndpointIdentity identity, CancellationToken cancellationToken)
            {
                Calls.Add("EnrollHost");
                return Task.FromResult(new RelayHostRegistrationResult { TenantId = config.TenantId, HostId = identity.IdentityId });
            }

            public Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                Calls.Add("ListDevices");
                return Task.FromResult((IReadOnlyList<RelayDeviceSummary>)new RelayDeviceSummary[0]);
            }

            public Task<RelayPairingInvite> CreatePairingInviteAsync(RelayHostConfig config, CreatePairingInviteRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task CancelPairingInviteAsync(RelayHostConfig config, string inviteId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task ApprovePairingClaimAsync(RelayHostConfig config, PairingApprovalRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task RejectPairingClaimAsync(RelayHostConfig config, string hostId, string claimId, string deviceId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<bool> RevokeDeviceAsync(RelayHostConfig config, string hostId, string deviceId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
