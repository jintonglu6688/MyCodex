using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class RemoteServiceManagementFormTests
    {
        private static string GetWinFormsPath(params string[] parts)
        {
            var path = System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src", "MyCodex.WinForms");
            foreach (var part in parts)
            {
                path = System.IO.Path.Combine(path, part);
            }

            return System.IO.Path.GetFullPath(path);
        }

        [Test]
        public void Source_CancelsLifetimeWorkBeforeClose()
        {
            var source = System.IO.File.ReadAllText(GetWinFormsPath("Forms", "RemoteServiceManagementForm.cs"));

            Assert.That(source, Does.Contain("CancellationTokenSource _lifetimeCancellation"));
            Assert.That(source, Does.Contain("_lifetimeCancellation.Cancel();"));
            Assert.That(source, Does.Not.Contain("StartAsync(_snapshot, CancellationToken.None)"));
            Assert.That(source, Does.Not.Contain("CreatePairingInviteAsync(config, _snapshot.HostId, 600, CancellationToken.None)"));
        }
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
        public void Constructor_ExposesUserLevelActionsOnly()
        {
            using (var form = CreateForm())
            {
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ConnectAndRegister", "Connect and register this PC")), Is.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.CopyPairingInfo", "Copy pairing info")), Is.Null);
                Assert.That(FindTextBoxByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.PairingInfo", "Pairing info")), Is.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.GeneratePairingCode", "Generate pairing code")), Is.Not.Null);
            }
        }

        [Test]
        public void Constructor_WhenNotConfiguredAndNoEmbeddedService_InitialFocusIsAdvancedSettings()
        {
            using (var form = CreateForm())
            {
                Assert.That(form.InitialFocusControlForTests.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices")));
            }
        }

        [Test]
        public void Constructor_WhenEmbeddedServiceAvailable_InitialFocusIsStartRemoteService()
        {
            using (var form = CreateForm(embeddedRelayService: new FakeEmbeddedRelayService()))
            {
                Assert.That(form.InitialFocusControlForTests.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices")));
            }
        }

        [Test]
        public void Constructor_WhenConfigured_InitialFocusIsGeneratePairingCode()
        {
            var store = CreateConfiguredStore();
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                bindingStore: new FakeRemoteBindingStore()))
            {
                Assert.That(form.InitialFocusControlForTests.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices")));
            }
        }

        [Test]
        public void Constructor_ActionButtonsUseChineseAccessibleNamesWhenChineseUiCulture()
        {
            using (UseCulture("zh-CN"))
            using (var form = CreateForm())
            {
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings"));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.GeneratePairingCode", "Generate pairing code"));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.RefreshDevices", "Refresh"));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.Close", "Close"));
                Assert.That(
                    form.DevicesContextMenuForTests.MenuItems[0].Text,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceManagementForm.Menu.ReviewPairingRequest",
                        "Review Pairing Request(&R)...")));
                Assert.That(
                    form.DevicesContextMenuForTests.MenuItems[1].Text,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceManagementForm.Menu.RemoveDevice",
                        "Remove Device(&D)...")));
            }
        }

        [Test]
        public void Constructor_ConfiguredTabOrderMovesFromDevicesToServiceToPairingToAdvanced()
        {
            var store = CreateConfiguredStore();
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                null,
                true,
                new FakeEmbeddedRelayService(),
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.Show();
                var devices = FindListViewByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices"));
                var refresh = FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.RefreshDevices", "Refresh"));

                Assert.That(devices, Is.Not.Null);
                Assert.That(refresh, Is.Not.Null);
                Assert.That(refresh.TabStop, Is.False);

                form.ActiveControl = devices;
                devices.Focus();

                Assert.That(form.SelectNextControl(devices, true, true, true, true), Is.True);
                Assert.That(form.ActiveControl.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service")));

                Assert.That(form.SelectNextControl(form.ActiveControl, true, true, true, true), Is.True);
                Assert.That(form.ActiveControl.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.GeneratePairingCode", "Generate pairing code")));

                Assert.That(form.SelectNextControl(form.ActiveControl, true, true, true, true), Is.True);
                Assert.That(form.ActiveControl.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings")));
            }
        }

        [Test]
        public void Constructor_EmbeddedModeShowsStartStopAndDiagnosticsActions()
        {
            using (var form = CreateForm(embeddedRelayService: new FakeEmbeddedRelayService()))
            {
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service"));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.Diagnostics", "Diagnostics"));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings"));
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StopEmbeddedRelay", "Stop remote service")), Is.Null);
            }
        }

        [Test]
        public void Constructor_EmbeddedModeRunningUsesStopRemoteServiceAction()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "Embedded";
            store.Snapshot.EmbeddedRelayProcessId = 4242;
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                null,
                true,
                new FakeEmbeddedRelayService(),
                bindingStore: new FakeRemoteBindingStore()))
            {
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StopEmbeddedRelay", "Stop remote service"));
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service")), Is.Null);
            }
        }

        [Test]
        public async Task FirewallRuleMissing_ShowsGuidanceAndEnablesExplicitAllowAction()
        {
            var firewall = new FakeEmbeddedRelayFirewallService
            {
                Status = FirewallStatus(EmbeddedRelayFirewallState.RuleMissing)
            };
            using (var form = CreateForm(
                embeddedRelayService: new FakeEmbeddedRelayService(),
                firewallService: firewall))
            {
                await form.RefreshFirewallStatusForTests();

                AssertButtonWithAccessibleNameExists(
                    form,
                    UiLocalization.Text(
                        "RemoteServiceManagementForm.Accessible.Button.AllowFirewall",
                        "Allow local network access"));
                Assert.That(form.FirewallButtonEnabledForTests, Is.True);
                Assert.That(
                    form.FirewallStateTextForTests,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.FirewallMissing",
                        "Windows Firewall may block phone access to the built-in Remote Service. Select Allow Local Network Access before pairing.")));
            }
        }

        [Test]
        public async Task FirewallNonPrivateNetwork_GuidesUserWithoutAllowingPublicProfile()
        {
            var firewall = new FakeEmbeddedRelayFirewallService
            {
                Status = FirewallStatus(EmbeddedRelayFirewallState.NonPrivateNetwork)
            };
            using (var form = CreateForm(
                embeddedRelayService: new FakeEmbeddedRelayService(),
                firewallService: firewall))
            {
                await form.RefreshFirewallStatusForTests();

                Assert.That(form.FirewallButtonEnabledForTests, Is.False);
                Assert.That(
                    form.FirewallStateTextForTests,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.FirewallNonPrivateNetwork",
                        "Windows does not identify the active network as Private. Change it to Private before allowing phone access.")));
            }
        }

        [Test]
        public async Task AllowFirewall_RequestsElevationThroughServiceAndReportsSuccess()
        {
            var firewall = new FakeEmbeddedRelayFirewallService
            {
                Status = FirewallStatus(EmbeddedRelayFirewallState.RuleMissing),
                AllowResult = new EmbeddedRelayFirewallAllowResult
                {
                    Success = true,
                    Status = FirewallStatus(EmbeddedRelayFirewallState.Allowed)
                }
            };
            string message = null;
            using (var form = CreateForm(
                embeddedRelayService: new FakeEmbeddedRelayService(),
                firewallService: firewall))
            {
                form.MessageBoxForTests = (title, value) => message = value;
                await form.RefreshFirewallStatusForTests();

                await form.ClickAllowFirewallForTests();

                Assert.That(firewall.AllowCalls, Is.EqualTo(1));
                Assert.That(form.FirewallButtonEnabledForTests, Is.False);
                Assert.That(
                    form.FirewallStateTextForTests,
                    Is.EqualTo(UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.FirewallAllowed",
                        "Windows Firewall allows the built-in Remote Service to receive connections from the private local subnet.")));
                Assert.That(message, Is.EqualTo(UiLocalization.Text(
                    "RemoteServiceManagementForm.Message.FirewallAllowSucceeded",
                    "Windows Firewall now allows devices on the private local network to connect to the built-in Remote Service.")));
            }
        }

        [Test]
        public void Constructor_WhenEmbeddedProcessIdIsStaleUsesStartRemoteServiceAction()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "Embedded";
            store.Snapshot.EmbeddedRelayProcessId = 4242;
            var service = new FakeEmbeddedRelayService { ForcedState = EmbeddedRelayRunState.Stopped };
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                null,
                true,
                service,
                bindingStore: new FakeRemoteBindingStore()))
            {
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service"));
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StopEmbeddedRelay", "Stop remote service")), Is.Null);
                Assert.That(form.ServerStateTextForTests, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedStopped", "Status: stopped.")));
            }
        }

        [Test]
        public void Shown_WhenEmbeddedRelayIsStoppedDoesNotRefreshDevices()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "Embedded";
            store.Snapshot.EmbeddedRelayProcessId = 4242;
            var client = new FakeRelayHostClient();
            var service = new FakeEmbeddedRelayService { ForcedState = EmbeddedRelayRunState.Stopped };
            using (var form = new RemoteServiceManagementForm(
                store,
                client,
                null,
                null,
                true,
                service,
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.Show();
                PumpFor(200);

                Assert.That(client.ListDevicesCalls, Is.EqualTo(0));
            }
        }

        [Test]
        public void Shown_WhenConfiguredRefreshesDevicesAndKeepsDevicesFocused()
        {
            var client = new FakeRelayHostClient();
            client.Devices.Add(new RelayDeviceSummary { TenantId = "tenant_a", HostId = "host_a", DeviceId = "device_a", DisplayName = "Android", Platform = "android" });
            using (var form = new RemoteServiceManagementForm(
                CreateConfiguredStore(),
                client,
                null,
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.Show();

                PumpUntil(() => client.ListDevicesCalls > 0);

                Assert.That(form.DeviceCountForTests, Is.EqualTo(1));
                Assert.That(form.ActiveControl.AccessibleName, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices")));
            }
        }

        [Test]
        public void Shown_WhenHostHasNotBeenRegisteredDoesNotRefreshDevices()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.LastRegisteredAt = null;
            var client = new FakeRelayHostClient();
            using (var form = new RemoteServiceManagementForm(
                store,
                client,
                null,
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.Show();
                PumpFor(200);

                Assert.That(client.ListDevicesCalls, Is.EqualTo(0));
            }
        }

        [Test]
        public void F5_WhenConfiguredRefreshesDevicesWithoutMovingFocus()
        {
            var client = new FakeRelayHostClient();
            client.Devices.Add(new RelayDeviceSummary { TenantId = "tenant_a", HostId = "host_a", DeviceId = "device_a", DisplayName = "Android", Platform = "android" });
            using (var form = new RemoteServiceManagementForm(
                CreateConfiguredStore(),
                client,
                null,
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.Show();
                PumpUntil(() => client.ListDevicesCalls > 0);
                var devices = FindListViewByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices"));
                Assert.That(devices, Is.Not.Null);
                form.ActiveControl = devices;
                devices.Focus();
                var callsBeforeF5 = client.ListDevicesCalls;

                Assert.That(SendF5(form), Is.True);
                PumpUntil(() => client.ListDevicesCalls > callsBeforeF5);

                Assert.That(form.ActiveControl, Is.SameAs(devices));
                Assert.That(form.DeviceCountForTests, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task StartEmbeddedRelay_DelegatesToEmbeddedServiceAndReloadsSnapshot()
        {
            var store = new FakeRemoteServiceConfigStore();
            store.Snapshot.RemoteEnabled = false;
            var service = new FakeEmbeddedRelayService(store);
            string messageText = null;
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                null,
                true,
                service,
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.MessageBoxForTests = (title, message) => messageText = message;

                await form.ClickStartEmbeddedRelayForTests();

                Assert.That(service.StartCalls, Is.EqualTo(1));
                Assert.That(store.Snapshot.RemoteEnabled, Is.True);
                Assert.That(form.ServerStateTextForTests, Does.Contain("192.168.1.50:38443"));
                Assert.That(messageText, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Message.StartEmbeddedRelaySucceeded", "Remote service started.")));
            }
        }

        [Test]
        public void DiagnosticsForm_DoesNotRequireVisualOnlyAccessAndKeepsTextCopyable()
        {
            using (var form = new RemoteServiceDiagnosticsForm("tenantId=tenant_local\r\nsecret=redacted"))
            {
                Assert.That(form.DiagnosticsTextForTests, Does.Contain("tenantId=tenant_local"));
                Assert.That(FindTextBoxByAccessibleName(form, UiLocalization.Text("RemoteServiceDiagnosticsForm.Accessible.Diagnostics", "Diagnostics")), Is.Not.Null);
                Assert.That(FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceDiagnosticsForm.Accessible.Close", "Close")), Is.Not.Null);
            }
        }

        [Test]
        public async Task StopEmbeddedRelay_DisablesOnlyThisTargetAndShowsSharedServiceStillRunning()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "Embedded";
            store.Snapshot.RemoteEnabled = true;
            var service = new FakeEmbeddedRelayService(store) { ForcedState = EmbeddedRelayRunState.Running };
            using (var form = new RemoteServiceManagementForm(store, new FakeRelayHostClient(), null,
                embeddedRelayService: service, bindingStore: new FakeRemoteBindingStore()))
            {
                await form.ClickStopEmbeddedRelayForTests();
                Assert.That(store.Snapshot.RemoteEnabled, Is.False);
                Assert.That(form.ServerStateTextForTests, Is.EqualTo(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.SharedRunningTargetDisabled",
                    "This target is disconnected. The shared remote service is still running for other users.")));
                AssertButtonWithAccessibleNameExists(form, UiLocalization.Text(
                    "RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service"));
            }
        }

        [Test]
        public async Task FailedEmbeddedStart_DoesNotEnableTarget()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteEnabled = false;
            var service = new FakeEmbeddedRelayService(store) { FailStart = true };
            using (var form = new RemoteServiceManagementForm(store, new FakeRelayHostClient(), null,
                embeddedRelayService: service, bindingStore: new FakeRemoteBindingStore()))
            {
                form.MessageBoxForTests = (title, message) => { };
                await form.ClickStartEmbeddedRelayForTests();
                Assert.That(store.Snapshot.RemoteEnabled, Is.False);
            }
        }

        [Test]
        public async Task GeneratePairingCode_RegistersCreatesInviteCopiesClipboardAndDoesNotCallTts()
        {
            var client = new FakeRelayHostClient();
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "External";
            store.Snapshot.RemoteEnabled = false;
            var screenReader = new FakeScreenReaderService();
            string clipboardText = null;
            string messageText = null;
            using (var form = new RemoteServiceManagementForm(
                store,
                client,
                screenReader,
                null,
                true,
                null,
                new FakeRemoteIdentityStore(),
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.ClipboardWriterForTests = text => clipboardText = text;
                form.MessageBoxForTests = (title, message) => messageText = message;

                await form.ClickGeneratePairingCodeForTests();

                Assert.That(client.ListDevicesCalls, Is.EqualTo(1));
                Assert.That(client.RegisterHostCalls, Is.EqualTo(1));
                Assert.That(client.CreatePairingInviteCalls, Is.EqualTo(1));
                Assert.That(store.Snapshot.RemoteEnabled, Is.True);
                Assert.That(clipboardText, Does.StartWith("mycodex://pair?data="));
                var packageJson = DecodePairingPackage(clipboardText);
                Assert.That(packageJson, Does.Contain("\"url\":\"https://relay.example.com\""));
                Assert.That(packageJson, Does.Contain("\"hostName\":\"Windows\""));
                Assert.That(packageJson, Does.Contain("\"inviteSecret\":"));
                Assert.That(packageJson, Does.Not.Contain("pairingToken"));
                Assert.That(messageText, Is.EqualTo(UiLocalization.Text("RemoteServiceManagementForm.Message.PairingCodeCopied", "Pairing code copied to clipboard. Import it on your phone.")));
                Assert.That(screenReader.LastSpokenText, Is.Null);
            }
        }

        [Test]
        public async Task GeneratePairingCode_EmbeddedCopiesCertificateSha256()
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "Embedded";
            store.Snapshot.RelayHost = "192.168.1.50";
            store.Snapshot.RelayPort = 38443;
            store.Snapshot.EmbeddedRelayCertificateSha256 = new string('f', 64);
            string clipboardText = null;
            using (var form = new RemoteServiceManagementForm(
                store,
                new FakeRelayHostClient(),
                null,
                null,
                true,
                null,
                new FakeRemoteIdentityStore(),
                bindingStore: new FakeRemoteBindingStore()))
            {
                form.ClipboardWriterForTests = text => clipboardText = text;
                form.MessageBoxForTests = (title, message) => { };

                await form.ClickGeneratePairingCodeForTests();

                Assert.That(
                    DecodePairingPackage(clipboardText),
                    Does.Contain("\"embeddedTlsCertificateSha256\":\"" +
                        SecureRemoteBase64Url.Encode(Enumerable.Repeat((byte)0xff, 32).ToArray()) +
                        "\""));
            }
        }

        [Test]
        public async Task PendingPairingClaim_AutomaticallyShowsVerifiedConfirmationAndApproves()
        {
            var client = new FakeRelayHostClient();
            client.PairingClaims.Add(CreateOpaqueClaim());
            var pairing = new FakePairingWorkflow();
            var realtime = new FakeRelayHostRealtimeClient();
            var screenReader = new FakeScreenReaderService();
            using (var form = new RemoteServiceManagementForm(
                CreateConfiguredStore(),
                client,
                screenReader,
                realtime,
                false,
                null,
                new FakeRemoteIdentityStore(),
                pairing))
            {
                form.ClipboardWriterForTests = text => { };
                form.MessageBoxForTests = (title, message) => { };
                var confirmationCalls = 0;
                form.PairingConfirmationForTests = claim =>
                {
                    confirmationCalls++;
                    return true;
                };

                await form.ClickGeneratePairingCodeForTests();
                await form.PollPairingClaimsForTests();

                Assert.That(client.PairingClaimsConfig, Is.Not.SameAs(pairing.CreateConfig));
                Assert.That(
                    client.PairingClaimsConfig.RelaySigningPublicKey,
                    Is.EqualTo(pairing.CreateConfig.RelaySigningPublicKey));
                Assert.That(
                    client.PairingClaimsConfig.RelaySigningKeyFingerprint,
                    Is.EqualTo(pairing.CreateConfig.RelaySigningKeyFingerprint));
                Assert.That(confirmationCalls, Is.EqualTo(1));
                Assert.That(pairing.ApproveCalls, Is.EqualTo(1));
                Assert.That(form.PairingPollingIntervalForTests, Is.EqualTo(2000));
                Assert.That(form.PairingPollingEnabledForTests, Is.False);
                Assert.That(screenReader.SpeakCalls, Is.GreaterThanOrEqualTo(1));
                Assert.That(realtime.StartCalls, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task PendingPairingClaim_DismissalKeepsRequestWithoutRepeatedPopup()
        {
            var client = new FakeRelayHostClient();
            client.PairingClaims.Add(CreateOpaqueClaim());
            var pairing = new FakePairingWorkflow();
            using (var form = new RemoteServiceManagementForm(
                CreateConfiguredStore(),
                client,
                null,
                null,
                true,
                null,
                new FakeRemoteIdentityStore(),
                pairing))
            {
                form.ClipboardWriterForTests = text => { };
                form.MessageBoxForTests = (title, message) => { };
                var confirmationCalls = 0;
                form.PairingConfirmationForTests = claim =>
                {
                    confirmationCalls++;
                    return null;
                };

                await form.ClickGeneratePairingCodeForTests();
                await form.PollPairingClaimsForTests();
                await form.PollPairingClaimsForTests();

                Assert.That(confirmationCalls, Is.EqualTo(1));
                Assert.That(pairing.ApproveCalls, Is.EqualTo(0));
                Assert.That(client.RejectClaimCalls, Is.EqualTo(0));
                Assert.That(form.PairingPollingEnabledForTests, Is.True);
                Assert.That(form.PendingClaimSummaryForTests, Does.Contain("Demo Android"));
                Assert.That(form.PendingClaimSummaryForTests, Does.Contain("9 5 2 3 7 2"));

                form.SelectFirstDeviceForTests();
                Assert.That(form.DevicesContextMenuForTests.MenuItems[0].Enabled, Is.True);
                Assert.That(form.DevicesContextMenuForTests.MenuItems[1].Enabled, Is.False);
                await form.ReviewSelectedPairingClaimForTests();

                Assert.That(confirmationCalls, Is.EqualTo(2));
                Assert.That(client.RejectClaimCalls, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task PendingPairingClaim_ConfirmationCanRejectDirectly()
        {
            var client = new FakeRelayHostClient();
            client.PairingClaims.Add(CreateOpaqueClaim());
            var pairing = new FakePairingWorkflow();
            using (var form = new RemoteServiceManagementForm(
                CreateConfiguredStore(),
                client,
                null,
                null,
                true,
                null,
                new FakeRemoteIdentityStore(),
                pairing))
            {
                form.ClipboardWriterForTests = text => { };
                form.MessageBoxForTests = (title, message) => { };
                form.PairingConfirmationForTests = claim => false;
                await form.ClickGeneratePairingCodeForTests();
                await form.PollPairingClaimsForTests();

                Assert.That(client.RejectClaimCalls, Is.EqualTo(1));
                Assert.That(pairing.ApproveCalls, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task RefreshDevices_BindsDevicesWithoutTokens()
        {
            var client = new FakeRelayHostClient();
            const string deviceId = "11111111-2222-3333-4444-555555555555";
            client.Devices.Add(new RelayDeviceSummary
            {
                TenantId = "tenant_a",
                HostId = "host_a",
                DeviceId = deviceId,
                ApprovedAt = new DateTimeOffset(
                    new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc))
                    .ToUnixTimeMilliseconds()
            });
            var store = CreateConfiguredStore();
            var bindings = new FakeRemoteBindingStore
            {
                ActiveBinding = new RemoteDeviceBinding
                {
                    TenantId = "tenant_a",
                    HostId = "host_a",
                    DeviceId = deviceId,
                    DeviceName = "JTL Phone",
                    Platform = "android",
                    State = RemoteBindingState.Active
                }
            };
            using (var form = new RemoteServiceManagementForm(
                store,
                client,
                null,
                bindingStore: bindings))
            {
                await form.ClickRefreshDevicesForTests();

                Assert.That(form.DeviceCountForTests, Is.EqualTo(1));
                Assert.That(form.DeviceListTextForTests, Does.Contain("JTL Phone"));
                Assert.That(form.DeviceListTextForTests, Does.Not.Contain(deviceId));
                Assert.That(form.DeviceListTextForTests, Does.Not.Contain("token"));
                form.SelectFirstDeviceForTests();
                Assert.That(form.DevicesContextMenuForTests.MenuItems[0].Enabled, Is.False);
                Assert.That(form.DevicesContextMenuForTests.MenuItems[1].Enabled, Is.True);
            }
        }

        [Test]
        public void RemotePairingConfirmationForm_ShowsVerifiedDetailsAndAccessibleActions()
        {
            var requestedAt = new DateTime(2026, 7, 24, 10, 30, 0, DateTimeKind.Utc);
            var verified = new VerifiedPairingClaim
            {
                ShortCode = "952372",
                Claim = new PairingClaim
                {
                    HostId = "host_a",
                    DeviceId = "device_a",
                    DeviceName = "Demo Android",
                    Platform = "android",
                    CreatedAt = new DateTimeOffset(requestedAt).ToUnixTimeMilliseconds()
                }
            };

            using (var form = new RemotePairingConfirmationForm("Windows", verified))
            {
                var details = FindListBoxByAccessibleName(
                    form,
                    UiLocalization.Text(
                        "RemotePairingConfirmationForm.Accessible.Details",
                        "Pairing details"));
                var detailText = string.Join(
                    " ",
                    details.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                var allow = FindButtonByAccessibleName(
                    form,
                    UiLocalization.Text(
                        "RemotePairingConfirmationForm.Accessible.Allow",
                        "Allow pairing"));
                var reject = FindButtonByAccessibleName(
                    form,
                    UiLocalization.Text(
                        "RemotePairingConfirmationForm.Accessible.Reject",
                        "Reject pairing"));
                var later = FindButtonByAccessibleName(
                    form,
                    UiLocalization.Text(
                        "RemotePairingConfirmationForm.Accessible.Later",
                        "Decide later"));

                Assert.That(details, Is.Not.Null);
                Assert.That(detailText, Does.Contain("Windows"));
                Assert.That(detailText, Does.Contain("Demo Android"));
                Assert.That(detailText, Does.Contain("android"));
                Assert.That(detailText, Does.Contain("9 5 2 3 7 2"));
                Assert.That(detailText, Does.Contain(requestedAt.ToLocalTime().ToString("g")));
                Assert.That(allow, Is.Not.Null);
                Assert.That(reject, Is.Not.Null);
                Assert.That(later, Is.Not.Null);
                Assert.That(form.AcceptButton, Is.Null);
                Assert.That(form.CancelButton, Is.SameAs(later));
                Assert.That(allow.TabIndex, Is.LessThan(reject.TabIndex));
                Assert.That(reject.TabIndex, Is.LessThan(later.TabIndex));
                form.Show();
                Assert.That(form.ActiveControl, Is.SameAs(details));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(520));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(320));
            }
        }

        [Test]
        public async Task RefreshDevices_DoesNotPersistConfiguration()
        {
            var store = CreateConfiguredStore();
            var client = new FakeRelayHostClient();
            using (var form = new RemoteServiceManagementForm(
                store,
                client,
                null,
                bindingStore: new FakeRemoteBindingStore()))
            {
                store.SaveCalls = 0;

                await form.ClickRefreshDevicesForTests();

                Assert.That(store.SaveCalls, Is.EqualTo(0));
                Assert.That(client.ListDevicesCalls, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TargetOnly_ExternalToggleChangesOnlyThisTargetAndAppliesConnection(bool initiallyEnabled)
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteServiceMode = "External";
            store.Snapshot.RemoteEnabled = initiallyEnabled;
            var sibling = CreateConfiguredStore();
            sibling.Snapshot.RemoteEnabled = true;
            var embedded = new FakeEmbeddedRelayService();
            var applyCalls = 0;
            using (var form = new RemoteServiceManagementForm(store, new FakeRelayHostClient(), null,
                embeddedRelayService: embedded, bindingStore: new FakeRemoteBindingStore(),
                applyConfiguredModeAsync: cancellationToken =>
                {
                    applyCalls++;
                    Assert.That(store.Snapshot.RemoteEnabled, Is.EqualTo(!initiallyEnabled));
                    return Task.CompletedTask;
                }, targetOnly: true))
            {
                await form.ToggleTargetConnectionAsync();

                Assert.That(store.Snapshot.RemoteEnabled, Is.EqualTo(!initiallyEnabled));
                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo("External"));
                Assert.That(store.Snapshot.RelayHost, Is.EqualTo("relay.example.com"));
                Assert.That(sibling.Snapshot.RemoteEnabled, Is.True);
                Assert.That(sibling.SaveCalls, Is.Zero);
                Assert.That(applyCalls, Is.EqualTo(1));
                Assert.That(embedded.StartCalls, Is.Zero);
            }
        }

        [TestCase("Embedded")]
        [TestCase("External")]
        public async Task TargetOnly_ConnectionSettingsButtonUsesGlobalCallbackAndRestoresFocus(string mode)
        {
            var store = CreateConfiguredStore();
            store.Snapshot.RemoteEnabled = false;
            store.Snapshot.RemoteServiceMode = mode;
            var settingsCalls = 0;
            IWin32Window settingsOwner = null;
            using (var form = new RemoteServiceManagementForm(store, new FakeRelayHostClient(), null,
                embeddedRelayService: new FakeEmbeddedRelayService(), bindingStore: new FakeRemoteBindingStore(),
                embeddedRelayFirewallService: new FakeEmbeddedRelayFirewallService(), targetOnly: true,
                showConnectionSettings: owner => { settingsOwner = owner; settingsCalls++; }))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2000, -2000);
                form.Show();
                await form.RefreshFirewallStatusForTests();
                var settings = FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings"));
                var firewall = FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.AllowFirewall", "Allow local network access"));
                var toggle = FindButtonByAccessibleName(form, UiLocalization.Text("RemoteServiceManagementForm.Accessible.EnableTarget", "Enable phone connection"));

                Assert.That(settings.Visible, Is.True);
                Assert.That(settings.TabStop, Is.True);
                settings.PerformClick();
                Assert.That(settingsCalls, Is.EqualTo(1));
                Assert.That(settingsOwner, Is.SameAs(form));
                Assert.That(form.ActiveControl, Is.SameAs(settings));
                Assert.That(store.SaveCalls, Is.Zero);
                Assert.That(store.Snapshot.RemoteEnabled, Is.False);
                Assert.That(store.Snapshot.RemoteServiceMode, Is.EqualTo(mode));
                Assert.That(firewall.Visible, Is.False);
                Assert.That(toggle.Visible, Is.True);
                Assert.That(toggle.TabStop, Is.True);
                form.Close();
            }
        }

        private static RemoteServiceManagementForm CreateForm(
            FakeRelayHostClient client = null,
            IEmbeddedRelayService embeddedRelayService = null,
            IEmbeddedRelayFirewallService firewallService = null)
        {
            return new RemoteServiceManagementForm(
                new FakeRemoteServiceConfigStore(),
                client ?? new FakeRelayHostClient(),
                null,
                null,
                true,
                embeddedRelayService,
                bindingStore: new FakeRemoteBindingStore(),
                embeddedRelayFirewallService: firewallService);
        }

        private static EmbeddedRelayFirewallStatus FirewallStatus(EmbeddedRelayFirewallState state)
        {
            return new EmbeddedRelayFirewallStatus
            {
                State = state,
                ExecutablePath = @"C:\MyCodex\runtime\relay\mycodex-relay.exe"
            };
        }

        private static OpaquePairingClaim CreateOpaqueClaim()
        {
            return new OpaquePairingClaim
            {
                ClaimId = "55555555-5555-5555-5555-555555555555",
                Status = "pending",
                Header = new PairingClaimHeader
                {
                    ClaimId = "55555555-5555-5555-5555-555555555555",
                    DeviceId = "33333333-3333-3333-3333-333333333333"
                }
            };
        }

        private static FakeRemoteServiceConfigStore CreateConfiguredStore()
        {
            var store = new FakeRemoteServiceConfigStore();
            store.Snapshot = new RemoteServiceConfigSnapshot
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_a",
                EncryptedAccessSecret = "protected",
                HostId = "host_a",
                ThisPcName = "Windows",
                LastRegisteredAt = DateTime.UtcNow,
                RelaySigningPublicKey = SecureRemoteBase64Url.Encode(new byte[65]),
                RelaySigningKeyFingerprint = SecureRemoteBase64Url.Encode(new byte[32])
            };
            return store;
        }

        private static void AssertButtonWithAccessibleNameExists(Control root, string accessibleName)
        {
            Assert.That(FindButtonByAccessibleName(root, accessibleName), Is.Not.Null, accessibleName);
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition())
            {
                if (stopwatch.ElapsedMilliseconds > 3000)
                {
                    Assert.Fail("Timed out while waiting for the form to finish the expected asynchronous UI work.");
                }

                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static void PumpFor(int milliseconds)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < milliseconds)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static bool SendF5(Form form)
        {
            var method = form.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
            var message = new Message();
            var arguments = new object[] { message, Keys.F5 };
            return (bool)method.Invoke(form, arguments);
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
                result.Add(root.Name + ":" + root.GetType().Name);
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
            if (button != null && string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return button;
            }

            return null;
        }

        private static ListView FindListViewByAccessibleName(Control root, string accessibleName)
        {
            foreach (Control control in root.Controls)
            {
                var child = FindListViewByAccessibleName(control, accessibleName);
                if (child != null)
                {
                    return child;
                }
            }

            var listView = root as ListView;
            if (listView != null && string.Equals(listView.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return listView;
            }

            return null;
        }

        private static ListBox FindListBoxByAccessibleName(Control root, string accessibleName)
        {
            foreach (Control control in root.Controls)
            {
                var child = FindListBoxByAccessibleName(control, accessibleName);
                if (child != null)
                {
                    return child;
                }
            }

            var listBox = root as ListBox;
            if (listBox != null && string.Equals(listBox.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return listBox;
            }

            return null;
        }

        private static TextBox FindTextBoxByAccessibleName(Control root, string accessibleName)
        {
            foreach (Control control in root.Controls)
            {
                var child = FindTextBoxByAccessibleName(control, accessibleName);
                if (child != null)
                {
                    return child;
                }
            }

            var textBox = root as TextBox;
            if (textBox != null && string.Equals(textBox.AccessibleName, accessibleName, StringComparison.Ordinal))
            {
                return textBox;
            }

            return null;
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

            public int SaveCalls { get; set; }

            public RemoteServiceConfigSnapshot Load()
            {
                return Snapshot;
            }

            public void Save(RemoteServiceConfigSnapshot snapshot)
            {
                SaveCalls++;
                Snapshot = snapshot;
            }

            public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
            {
                ProtectedSecret = accessSecret;
                snapshot.EncryptedAccessSecret = string.IsNullOrWhiteSpace(accessSecret) ? null : "protected";
                snapshot.AccessSecretSuffix = "cret";
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

            public int SpeakCalls { get; private set; }

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
                SpeakCalls++;
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

        private sealed class FakeEmbeddedRelayFirewallService : IEmbeddedRelayFirewallService
        {
            public EmbeddedRelayFirewallStatus Status { get; set; } =
                FirewallStatus(EmbeddedRelayFirewallState.Unknown);

            public EmbeddedRelayFirewallAllowResult AllowResult { get; set; }

            public int StatusCalls { get; private set; }

            public int AllowCalls { get; private set; }

            public EmbeddedRelayFirewallStatus GetStatus()
            {
                StatusCalls++;
                return Status;
            }

            public Task<EmbeddedRelayFirewallAllowResult> AllowPrivateLocalSubnetAsync(
                CancellationToken cancellationToken)
            {
                AllowCalls++;
                if (AllowResult != null && AllowResult.Status != null)
                {
                    Status = AllowResult.Status;
                }

                return Task.FromResult(AllowResult ?? new EmbeddedRelayFirewallAllowResult
                {
                    Status = Status,
                    ErrorMessage = "not configured"
                });
            }
        }

        private sealed class FakeEmbeddedRelayService : IEmbeddedRelayService
        {
            private readonly FakeRemoteServiceConfigStore _store;

            public FakeEmbeddedRelayService(FakeRemoteServiceConfigStore store = null)
            {
                _store = store;
            }

            public int StartCalls { get; private set; }

            public EmbeddedRelayRunState? ForcedState { get; set; }
            public bool FailStart { get; set; }

            public Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken)
            {
                StartCalls++;
                if (FailStart)
                {
                    return Task.FromResult(new EmbeddedRelayStartResult { Success = false, ErrorMessage = "test startup failure" });
                }

                snapshot.RemoteServiceMode = "Embedded";
                snapshot.RelayHost = "192.168.1.50";
                snapshot.RelayPort = 38443;
                snapshot.UseTls = false;
                snapshot.TenantId = "tenant_local";
                snapshot.EncryptedAccessSecret = "protected";
                snapshot.EmbeddedRelayConfigPath = @"C:\Relay\relay-config.json";
                snapshot.EmbeddedRelayStatePath = @"C:\Relay\relay-state.db";
                snapshot.EmbeddedRelayProcessId = 4242;
                if (_store != null)
                {
                    _store.Save(snapshot);
                }

                return Task.FromResult(new EmbeddedRelayStartResult
                {
                    Success = true,
                    Status = GetStatus(snapshot)
                });
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(0);
            }

            public EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot)
            {
                if (ForcedState.HasValue)
                {
                    return new EmbeddedRelayStatus
                    {
                        State = ForcedState.Value,
                        RelayHost = snapshot == null ? null : snapshot.RelayHost,
                        RelayPort = snapshot == null ? 0 : snapshot.RelayPort,
                        TlsRequired = snapshot != null && snapshot.UseTls,
                        TenantId = snapshot == null ? null : snapshot.TenantId,
                        ProcessId = snapshot == null ? 0 : snapshot.EmbeddedRelayProcessId,
                        ConfigPath = snapshot == null ? null : snapshot.EmbeddedRelayConfigPath,
                        StatePath = snapshot == null ? null : snapshot.EmbeddedRelayStatePath
                    };
                }

                if (snapshot != null && snapshot.EmbeddedRelayProcessId > 0)
                {
                    return new EmbeddedRelayStatus
                    {
                        State = EmbeddedRelayRunState.Running,
                        RelayHost = snapshot.RelayHost,
                        RelayPort = snapshot.RelayPort,
                        TlsRequired = snapshot.UseTls,
                        RelayUrl = "http://" + snapshot.RelayHost + ":" + snapshot.RelayPort.ToString(CultureInfo.InvariantCulture),
                        HealthUrl = "http://" + snapshot.RelayHost + ":" + snapshot.RelayPort.ToString(CultureInfo.InvariantCulture) + "/health",
                        TenantId = snapshot.TenantId,
                        ProcessId = snapshot.EmbeddedRelayProcessId,
                        ConfigPath = snapshot.EmbeddedRelayConfigPath,
                        StatePath = snapshot.EmbeddedRelayStatePath
                    };
                }

                return new EmbeddedRelayStatus { State = EmbeddedRelayRunState.NotConfigured };
            }

            public EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot)
            {
                return new EmbeddedRelayDiagnosticInfo
                {
                    Status = GetStatus(snapshot),
                    Lines = new[] { "tenantId=tenant_local", "relayUrl=http://192.168.1.50:38443" }
                };
            }
        }

        private sealed class FakeRelayHostRealtimeClient : IRelayHostRealtimeClient
        {
            public event EventHandler<RelayRealtimeConnectionStatusEventArgs> StatusChanged
            {
                add { }
                remove { }
            }

            public int StartCalls { get; private set; }

            public Task StartAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                StartCalls++;
                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                return Task.CompletedTask;
            }

            public Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeRelayHostClient : IRelayHostClient
        {
            public readonly List<RelayDeviceSummary> Devices = new List<RelayDeviceSummary>();

            public readonly List<OpaquePairingClaim> PairingClaims = new List<OpaquePairingClaim>();

            public int RegisterHostCalls { get; private set; }

            public int ListDevicesCalls { get; private set; }

            public int CreatePairingInviteCalls { get; private set; }

            public int RevokeDeviceCalls { get; private set; }

            public int RejectClaimCalls { get; private set; }

            public RemoteEndpointIdentity LastIdentity { get; private set; }

            public RelayHostConfig PairingClaimsConfig { get; private set; }

            public Task<RelayMetadata> GetMetadataAsync(RelayHostConfig config, CancellationToken cancellationToken)
            {
                var key = SecureRemoteBase64Url.Encode(new byte[65]);
                var fingerprint = SecureRemoteBase64Url.Encode(new byte[32]);
                config.RelaySigningPublicKey = key;
                config.RelaySigningKeyFingerprint = fingerprint;
                return Task.FromResult(new RelayMetadata
                {
                    ProtocolVersion = 1,
                    SigningPublicKey = key,
                    SigningKeyFingerprint = fingerprint,
                    ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }

            public Task<RelayHostRegistrationResult> EnrollHostAsync(RelayHostConfig config, RemoteEndpointIdentity identity, CancellationToken cancellationToken)
            {
                RegisterHostCalls++;
                LastIdentity = identity;
                return Task.FromResult(new RelayHostRegistrationResult { TenantId = config.TenantId, HostId = identity.IdentityId });
            }

            public Task<RelayPairingInvite> CreatePairingInviteAsync(RelayHostConfig config, CreatePairingInviteRequest request, CancellationToken cancellationToken)
            {
                CreatePairingInviteCalls++;
                return Task.FromResult(new RelayPairingInvite
                {
                    TenantId = request.TenantId,
                    HostId = request.HostId,
                    InviteId = request.InviteId,
                    Status = "pending",
                    ExpiresAt = request.ExpiresAt
                });
            }

            public Task CancelPairingInviteAsync(RelayHostConfig config, string inviteId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                PairingClaimsConfig = config;
                return Task.FromResult((IReadOnlyList<OpaquePairingClaim>)PairingClaims);
            }

            public Task ApprovePairingClaimAsync(RelayHostConfig config, PairingApprovalRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task RejectPairingClaimAsync(RelayHostConfig config, string hostId, string claimId, string deviceId, CancellationToken cancellationToken)
            {
                RejectClaimCalls++;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                ListDevicesCalls++;
                return Task.FromResult((IReadOnlyList<RelayDeviceSummary>)Devices);
            }

            public Task<bool> RevokeDeviceAsync(RelayHostConfig config, string hostId, string deviceId, CancellationToken cancellationToken)
            {
                RevokeDeviceCalls++;
                return Task.FromResult(true);
            }
        }

        private sealed class FakeRemoteIdentityStore : IRemoteIdentityStore
        {
            public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId)
            {
                return new RemoteEndpointIdentity
                {
                    IdentityId = hostId,
                    KeyVersion = 1,
                    SigningPublicKey = SecureRemoteBase64Url.Encode(new byte[65]),
                    AgreementPublicKey = SecureRemoteBase64Url.Encode(new byte[65])
                };
            }

            public byte[] Sign(byte[] transcript)
            {
                return new byte[64];
            }

            public byte[] DeriveAgreement(byte[] peerPublicKey)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeRemoteBindingStore : IRemoteBindingStore
        {
            public RemoteDeviceBinding ActiveBinding { get; set; }

            public IReadOnlyList<RemoteDeviceBinding> ListActive(string tenantId, string hostId)
            {
                return ActiveBinding == null
                    ? (IReadOnlyList<RemoteDeviceBinding>)new RemoteDeviceBinding[0]
                    : new[] { ActiveBinding };
            }

            public RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId)
            {
                return ActiveBinding != null &&
                    string.Equals(ActiveBinding.TenantId, tenantId, StringComparison.Ordinal) &&
                    string.Equals(ActiveBinding.HostId, hostId, StringComparison.Ordinal) &&
                    string.Equals(ActiveBinding.DeviceId, deviceId, StringComparison.Ordinal)
                    ? ActiveBinding
                    : null;
            }

            public RemoteDeviceBinding FindPending(string claimId)
            {
                return null;
            }

            public void SavePending(RemoteDeviceBinding binding)
            {
            }

            public void Activate(string claimId, int bindingVersion)
            {
            }

            public void Revoke(string tenantId, string hostId, string deviceId)
            {
            }
        }

        private sealed class FakePairingWorkflow : ISecureRemotePairingWorkflow
        {
            public int ApproveCalls { get; private set; }

            public RelayHostConfig CreateConfig { get; private set; }

            public Task<ActivePairingInvite> CreatePairingInviteAsync(
                RelayHostConfig config,
                string hostId,
                string hostName,
                CancellationToken cancellationToken)
            {
                CreateConfig = config;
                return Task.FromResult(new ActivePairingInvite
                {
                    PairingUri = "mycodex://pair?data=test",
                    Package = new PairingPackage
                    {
                        HostId = hostId,
                        HostName = hostName,
                        InviteId = "44444444-4444-4444-4444-444444444444",
                        ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 540000
                    }
                });
            }

            public VerifiedPairingClaim VerifyClaim(
                ActivePairingInvite invite,
                OpaquePairingClaim opaqueClaim)
            {
                return new VerifiedPairingClaim
                {
                    ShortCode = "952372",
                    Claim = new PairingClaim
                    {
                        ClaimId = opaqueClaim.ClaimId,
                        InviteId = invite.Package.InviteId,
                        HostId = invite.Package.HostId,
                        DeviceId = opaqueClaim.Header.DeviceId,
                        DeviceName = "Demo Android",
                        Platform = "android",
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };
            }

            public Task ApprovePairingClaimAsync(
                RelayHostConfig config,
                VerifiedPairingClaim verifiedClaim,
                CancellationToken cancellationToken)
            {
                ApproveCalls++;
                return Task.CompletedTask;
            }

            public Task RetryPendingApprovalAsync(
                RelayHostConfig config,
                string claimId,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private static string DecodePairingPackage(string pairingUri)
        {
            var encoded = pairingUri.Substring("mycodex://pair?data=".Length);
            return Encoding.UTF8.GetString(SecureRemoteBase64Url.Decode(Uri.UnescapeDataString(encoded)));
        }
    }
}
