using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TargetSettingsFormTests
    {
        [TestCase("Windows", 3)]
        [TestCase("Wsl", 1)]
        [TestCase("Ssh", 1)]
        public void Editor_RestrictsRuntimeFamilyAndSharedUpdates(string kind, int sourceCount)
        {
            using (var form = CreateForm(kind))
            {
                var sources = FormTestDriver.Field<ComboBox>(form, "_runtimeSourceComboBox");
                Assert.That(sources.Items.Count, Is.EqualTo(sourceCount));
                Assert.That(sources.Enabled, Is.EqualTo(kind == "Windows"));
                Assert.That(FormTestDriver.Field<ComboBox>(form, "_remoteProfileComboBox").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_addRemoteProfileButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_deleteRemoteProfileButton").Enabled, Is.False);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(760));
                Assert.That(FormTestDriver.Field<TextBox>(form, "_targetNameTextBox").AccessibleName, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_targetNameTextBox").TabIndex, Is.LessThan(sources.TabIndex));
                Assert.That(FormTestDriver.Field<FlowLayoutPanel>(form, "_buttonPanel").Controls.Count, Is.EqualTo(3));
                Assert.That(form.AcceptButton, Is.SameAs(FormTestDriver.Field<Button>(form, "_okButton")));
                Assert.That(form.CancelButton, Is.SameAs(FormTestDriver.Field<Button>(form, "_cancelButton")));
                Assert.That(FormTestDriver.Field<Button>(form, "_okButton").DialogResult, Is.EqualTo(DialogResult.None));
            }
        }

        [Test]
        public async Task SaveFailureOrCancelledConfirmation_RetainsDraftAndNeverChangesOriginal()
        {
            var original = Settings("Windows");
            original.ExpectedRuntimeRevision = "revision-original";
            TargetRuntimeSettings saved = null;
            using (var form = CreateForm("Windows", settings: original, save: draft =>
            {
                saved = draft;
                return Task.FromResult(false);
            }))
            {
                FormTestDriver.Field<TextBox>(form, "_targetNameTextBox").Text = "Renamed Windows";
                await form.SaveTargetForTestsAsync();

                Assert.That(saved.Name, Is.EqualTo("Renamed Windows"));
                Assert.That(saved.ExpectedRuntimeRevision, Is.EqualTo("revision-original"));
                Assert.That(original.Name, Is.EqualTo("Windows"));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_targetNameTextBox").Text, Is.EqualTo("Renamed Windows"));
                Assert.That(FormTestDriver.Field<Button>(form, "_okButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<Button>(form, "_cancelButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_diagnosticsTextBox").Text, Is.EqualTo(UiLocalization.Text("TargetSettingsForm.Status.SaveNotCompleted", "The operation was not completed. Your draft is retained; retry or cancel.")));
            }
        }

        [Test]
        public async Task SaveWaitsForPersistenceAndClosesOnlyAfterSuccess()
        {
            var completion = new TaskCompletionSource<bool>();
            var saveRequested = false;
            using (var form = CreateForm("Windows", save: draft =>
            {
                saveRequested = true;
                return completion.Task;
            }))
            {
                var save = form.SaveTargetForTestsAsync();
                Assert.That(saveRequested, Is.True);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(FormTestDriver.Field<Button>(form, "_okButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_cancelButton").Enabled, Is.False);
                completion.SetResult(true);
                await save;
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
            }
        }

        [Test]
        public async Task ReconnectFailure_PreservesCallbackStatusAndAllowsRetry()
        {
            WslAppServerSettingsForm form = null;
            var calls = 0;
            form = CreateForm("Windows", save: draft =>
            {
                calls++;
                form.SetTargetSaveStatus("Saved; reconnect failed. Retry.");
                return Task.FromResult(false);
            });
            using (form)
            {
                await form.SaveTargetForTestsAsync();
                Assert.That(FormTestDriver.Field<TextBox>(form, "_diagnosticsTextBox").Text, Is.EqualTo("Saved; reconnect failed. Retry."));
                await form.SaveTargetForTestsAsync();
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(form.IsDisposed, Is.False);
            }
        }

        [Test]
        public async Task ExistingSsh_HostIsEditableAndDraftPreservesUserAndPassword()
        {
            var original = Settings("Ssh");
            TargetRuntimeSettings saved = null;
            using (var form = CreateForm("Ssh", settings: original, save: draft =>
            {
                saved = draft;
                return Task.FromResult(false);
            }))
            {
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").ReadOnly, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").AccessibleName, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").TabStop, Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteUserNameTextBox").ReadOnly, Is.True);
                FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").Text = "other.example";
                FormTestDriver.Field<TextBox>(form, "_remoteUserNameTextBox").Text = "other-user";
                FormTestDriver.Field<TextBox>(form, "_remotePortTextBox").Text = "2222";
                FormTestDriver.Field<TextBox>(form, "_remoteShellTextBox").Text = "/bin/sh";
                await form.SaveTargetForTestsAsync();

                Assert.That(saved.RemoteSsh.HostName, Is.EqualTo("other.example"));
                Assert.That(saved.RemoteSsh.HostKeyFingerprint, Is.Empty.Or.Null);
                Assert.That(saved.RemoteSsh.UserName, Is.EqualTo("tester"));
                Assert.That(saved.RemoteSsh.Port, Is.EqualTo(2222));
                Assert.That(saved.RemoteSsh.ShellCommand, Is.EqualTo("/bin/sh"));
                Assert.That(saved.RemoteSsh.ProtectedPassword, Is.EqualTo("protected-original"));
                Assert.That(original.RemoteSsh.Port, Is.EqualTo(22));
                Assert.That(original.RemoteSsh.HostName, Is.EqualTo("host.example"));
            }
        }

        [TestCase("HOST.EXAMPLE", false)]
        [TestCase("new.example", true)]
        public async Task HostEditing_ClearsOldTrustBeforeDetectionAndPreservesConfirmedKey(string host, bool changed)
        {
            TargetRuntimeSettings saved = null;
            using (var form = CreateForm("Ssh", save: draft => { saved = draft; return Task.FromResult(false); }))
            {
                FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").Text = host;
                var key = FormTestDriver.Field<TextBox>(form, "_remoteHostKeyTextBox");
                Assert.That(key.Text, Is.EqualTo(changed ? string.Empty : "SHA256:original"));
                if (changed) key.Text = "SHA256:confirmed-new-host";
                await form.SaveTargetForTestsAsync();
                Assert.That(saved.RemoteSsh.HostName, Is.EqualTo(host));
                Assert.That(saved.RemoteSsh.HostKeyFingerprint, Is.EqualTo(key.Text));
            }
        }

        [Test]
        public void NewSsh_DoesNotInheritPasswordOrFingerprint()
        {
            using (var form = CreateForm("Ssh", isNew: true))
            {
                var profiles = FormTestDriver.Field<List<RemoteUnixSshProfileConfigSnapshot>>(form, "_remoteProfiles");
                Assert.That(profiles, Has.Count.EqualTo(1));
                Assert.That(profiles[0].Ssh.ProtectedPassword, Is.Null);
                Assert.That(profiles[0].Ssh.HostKeyFingerprint, Is.Null);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").ReadOnly, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteUserNameTextBox").ReadOnly, Is.False);
            }
        }

        [Test]
        public async Task NewSshSavedButFollowupFailed_RetainsEditableHostAndSavedPasswordForRetry()
        {
            WslAppServerSettingsForm form = null;
            var drafts = new List<TargetRuntimeSettings>();
            form = CreateForm("Ssh", isNew: true, save: draft =>
            {
                drafts.Add(draft);
                if (drafts.Count == 1)
                {
                    form.MarkTargetSaved(new TargetRuntimeSettings
                    {
                        ExpectedRuntimeRevision = "revision-created",
                        Name = draft.Name,
                        CodexRuntimeSource = draft.CodexRuntimeSource,
                        RemoteSsh = draft.RemoteSsh
                    });
                }

                form.SetTargetSaveStatus("Saved; further settings update failed. Retry.");
                return Task.FromResult(false);
            });
            using (form)
            {
                FormTestDriver.Field<TextBox>(form, "_remotePasswordTextBox").Text = "fixture-password";
                await form.SaveTargetForTestsAsync();
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").ReadOnly, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteUserNameTextBox").ReadOnly, Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remotePasswordTextBox").Text, Is.Empty);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_diagnosticsTextBox").Text, Is.EqualTo("Saved; further settings update failed. Retry."));
                FormTestDriver.Field<TextBox>(form, "_remoteHostTextBox").Text = "corrected.example";
                await form.SaveTargetForTestsAsync();
                Assert.That(drafts, Has.Count.EqualTo(2));
                Assert.That(drafts[1].RemoteSsh.HostName, Is.EqualTo("corrected.example"));
                Assert.That(drafts[1].RemoteSsh.ProtectedPassword, Is.EqualTo(drafts[0].RemoteSsh.ProtectedPassword));
                Assert.That(drafts[1].ExpectedRuntimeRevision, Is.EqualTo("revision-created"));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
            }
        }

        [Test]
        public async Task NewWslSavedButFollowupFailed_LocksSavedDistributionForRetry()
        {
            WslAppServerSettingsForm form = null;
            var drafts = new List<TargetRuntimeSettings>();
            form = CreateForm("Wsl", isNew: true, save: draft =>
            {
                drafts.Add(draft);
                if (drafts.Count == 1)
                {
                    form.MarkTargetSaved(draft);
                }

                return Task.FromResult(false);
            }, listDistributions: token => Task.FromResult<IList<string>>(new[] { "Ubuntu", "Debian" }));
            using (form)
            {
                await form.RefreshTargetDistributionsForTestsAsync();
                FormTestDriver.Field<ComboBox>(form, "_targetDistributionComboBox").SelectedItem = "Debian";
                await form.SaveTargetForTestsAsync();
                Assert.That(FormTestDriver.Field<ComboBox>(form, "_targetDistributionComboBox").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_refreshDistributionsButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_distributionTextBox").ReadOnly, Is.True);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_distributionTextBox").TabStop, Is.True);
                await form.SaveTargetForTestsAsync();
                Assert.That(drafts[1].WslAppServer.DistributionName, Is.EqualTo("Debian"));
            }
        }

        [Test]
        public async Task WslRefresh_IsExplicitAndSelectionDoesNotExecuteCommands()
        {
            var calls = 0;
            using (var form = CreateForm("Wsl", isNew: true, listDistributions: token =>
            {
                calls++;
                return Task.FromResult<IList<string>>(new[] { "Ubuntu", "Debian" });
            }))
            {
                var distribution = FormTestDriver.Field<ComboBox>(form, "_targetDistributionComboBox");
                Assert.That(calls, Is.Zero);
                await form.RefreshTargetDistributionsForTestsAsync();
                Assert.That(calls, Is.EqualTo(1));
                distribution.SelectedItem = "Debian";
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<TextBox>(form, "_distributionTextBox").Text, Is.EqualTo("Debian"));
                Assert.That(FormTestDriver.Field<Button>(form, "_refreshDistributionsButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<Button>(form, "_refreshDistributionsButton").AccessibleName, Is.Not.Empty);
            }
        }

        [Test]
        public async Task ExistingWsl_DraftCannotChangeDistribution()
        {
            TargetRuntimeSettings saved = null;
            using (var form = CreateForm("Wsl", save: draft =>
            {
                saved = draft;
                return Task.FromResult(false);
            }))
            {
                Assert.That(FormTestDriver.Field<TextBox>(form, "_distributionTextBox").ReadOnly, Is.True);
                FormTestDriver.Field<TextBox>(form, "_distributionTextBox").Text = "Debian";
                FormTestDriver.Field<TextBox>(form, "_codexCommandTextBox").Text = "/opt/codex";
                await form.SaveTargetForTestsAsync();
                Assert.That(saved.WslAppServer.DistributionName, Is.EqualTo("Ubuntu"));
                Assert.That(saved.WslAppServer.CodexCommand, Is.EqualTo("/opt/codex"));
            }
        }

        [TestCase("Wsl")]
        [TestCase("Ssh")]
        public async Task RemoteEditor_NeverUsesLocalWindowsSandboxEvenIfClientPassed(string kind)
        {
            var client = new CountingSandboxClient();
            using (var form = CreateForm(kind, sandbox: client))
            {
                await form.DetectWindowsSandboxForTestsAsync();
                await form.StartWindowsSandboxSetupForTestsAsync();
                Assert.That(client.Calls, Is.Zero);
                Assert.That(form.WindowsSandboxDetectEnabledForTests, Is.False);
                Assert.That(form.WindowsSandboxSetupEnabledForTests, Is.False);
            }
        }

        [Test]
        public async Task WindowsShellChange_BlocksSandboxUntilTargetReconnects()
        {
            var client = new CountingSandboxClient();
            using (var form = CreateForm("Windows", sandbox: client))
            {
                FormTestDriver.Field<ComboBox>(form, "_windowsShellComboBox").SelectedIndex = 1;
                await form.DetectWindowsSandboxForTestsAsync();
                Assert.That(client.Calls, Is.Zero);
                Assert.That(form.WindowsSandboxDetectEnabledForTests, Is.False);
            }
        }

        private static WslAppServerSettingsForm CreateForm(
            string kind,
            bool isNew = false,
            TargetRuntimeSettings settings = null,
            Func<TargetRuntimeSettings, Task<bool>> save = null,
            Func<CancellationToken, Task<IList<string>>> listDistributions = null,
            IAppServerWindowsSandboxClient sandbox = null)
        {
            var dataRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-editor-" + Guid.NewGuid().ToString("N"));
            var paths = new AppConfigService(dataRoot);
            return new WslAppServerSettingsForm(
                new TargetProfileSnapshot { Id = "target", Kind = kind, SshProfileId = "ssh-profile" },
                isNew,
                settings ?? Settings(kind),
                new CodexRuntimeManager(paths),
                new CodexRuntimeUpdateService(paths),
                sandbox,
                null,
                save ?? (draft => Task.FromResult(false)),
                listDistributions);
        }

        private static TargetRuntimeSettings Settings(string kind)
        {
            return new TargetRuntimeSettings
            {
                Name = kind,
                CodexRuntimeSource = kind == "Windows" ? CodexRuntimeSources.Bundled : kind == "Wsl" ? CodexRuntimeSources.Wsl : CodexRuntimeSources.RemoteUnixSsh,
                WindowsShellPreference = WindowsShellPreferences.Auto,
                WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" },
                RemoteSsh = new RemoteMacSshConfigSnapshot
                {
                    HostName = "host.example",
                    UserName = "tester",
                    Port = 22,
                    AuthenticationMode = RemoteSshAuthenticationMode.Password,
                    ProtectedPassword = "protected-original",
                    HostKeyFingerprint = "SHA256:original",
                    ShellCommand = "bash"
                }
            };
        }

        private sealed class CountingSandboxClient : IAppServerWindowsSandboxClient
        {
            public int Calls { get; private set; }

            public Task<AppServerWindowsSandboxReadiness> ReadWindowsSandboxReadinessAsync()
            {
                Calls++;
                return Task.FromResult(new AppServerWindowsSandboxReadiness { Status = AppServerWindowsSandboxReadinessStatus.Ready });
            }

            public Task<AppServerWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(AppServerWindowsSandboxSetupMode mode, string cwd = null)
            {
                Calls++;
                return Task.FromResult(new AppServerWindowsSandboxSetupStartResult { Started = true });
            }
        }
    }
}
