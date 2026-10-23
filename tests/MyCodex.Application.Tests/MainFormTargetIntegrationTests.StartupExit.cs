using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms;
using MyCodex.WinForms.Forms;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [Test]
        public void AutomaticUpdatePromptCanCaptureTheReadyTargetBeforeStartupReturns()
        {
            using (var fixture = new TargetFormFixture())
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                var manifest = Path.Combine(fixture.Root, "update.json");
                File.WriteAllText(manifest, "{\"app\":{\"version\":\"999.0.0\",\"asset\":{\"url\":\"installer.exe\",\"sha256\":\"synthetic\"}}}");
                var previousUrl = Environment.GetEnvironmentVariable(AppConstants.UpdateManifestUrlEnvironmentVariable);
                var files = new PendingModeSettingsFile();
                files.Pending.SetResult(Tuple.Create(false, string.Empty));
                FormTestDriver.SetField(fixture.Form, "_modeSettingsStore", new JsonModeSettingsStore(fixture.Paths, files));
                FormTestDriver.Field<AppStateSnapshot>(fixture.Form, "_appStateSnapshot").HasCompletedStartupModeOnboarding = true;
                // Keep startup offline: mode storage is fake, connection throws in OfflineClient,
                // and no local authentication backend or phone service may be started.
                FormTestDriver.Field<AppConfigSnapshot>(fixture.Form, "_appConfigSnapshot").CodexRuntimeSource = CodexRuntimeSources.Wsl;
                FormTestDriver.Invoke(fixture.Form, "DisposeRemoteHostService");
                FormTestDriver.SetField(fixture.Form, "_remoteHostServiceController", null);
                Exception failure = null;
                var promptSeen = false;
                timer.Tick += (sender, args) =>
                {
                    var prompt = System.Windows.Forms.Application.OpenForms.OfType<AppUpdatePromptForm>().FirstOrDefault();
                    if (prompt == null || !ReferenceEquals(prompt.Owner, fixture.Form)) return;
                    timer.Stop();
                    promptSeen = true;
                    try
                    {
                        var owner = FormTestDriver.Field<TargetInstanceCoordinator>(fixture.Form, "_targetInstance").InstanceRecord;
                        var coordinator = new TargetExitAllCoordinator(owner,
                            () => TargetInstanceCoordinator.Discover(fixture.Channel, fixture.Root),
                            (instance, request) => throw new AssertionException("Inspection must not close a target."));
                        Assert.That(coordinator.CaptureScope().Single().Instance.InstanceId, Is.EqualTo(owner.InstanceId));
                    }
                    catch (Exception error) { failure = error; }
                    finally { prompt.DialogResult = System.Windows.Forms.DialogResult.Cancel; }
                };
                try
                {
                    Environment.SetEnvironmentVariable(AppConstants.UpdateManifestUrlEnvironmentVariable, new Uri(manifest).AbsoluteUri);
                    timer.Start();
                    typeof(MainForm).GetMethod("OnShown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Invoke(fixture.Form, new object[] { fixture.Form, EventArgs.Empty });
                    PumpUntil(() => FormTestDriver.Field<bool>(fixture.Form, "_targetStartupComplete") && !FormTestDriver.Field<bool>(fixture.Form, "_isUpdateCheckInProgress"));
                    Assert.That(promptSeen, Is.True);
                    Assert.That(failure, Is.Null, "The update must not reject its own target as still starting: " + failure);
                    Assert.That(fixture.Client.ConnectCalls, Is.EqualTo(1));
                    Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                }
                finally
                {
                    timer.Stop();
                    Environment.SetEnvironmentVariable(AppConstants.UpdateManifestUrlEnvironmentVariable, previousUrl);
                }
            }
        }

        [Test]
        public void TargetMenuAcceptsCancelledStartupWithoutReportingFailure()
        {
            using (var fixture = new TargetFormFixture())
            {
                var target = fixture.Catalog.AddSsh("Mac", new RemoteMacSshConfigSnapshot { HostName = "test.invalid", UserName = "test" });
                TargetInstanceCoordinator owner = null;
                try
                {
                    FormTestDriver.SetField(fixture.Form, "_targetLauncher", new TargetWindowLauncher(fixture.Catalog, fixture.Channel, fixture.Root, args =>
                    {
                        owner = TargetInstanceCoordinator.Acquire(fixture.Channel, target.Id, fixture.Root);
                        owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing });
                    }));
                    var opening = (Task)FormTestDriver.Invoke(fixture.Form, "OpenTargetWindowAsync", target.Id);
                    PumpUntil(() => opening.IsCompleted);
                    opening.GetAwaiter().GetResult();
                    Assert.That(fixture.Form.IsDisposed, Is.False);
                }
                finally { owner?.Dispose(); }
            }
        }

        [Test]
        public void StartupCanPrepareAndCommitExitWithoutFinishingLoading()
        {
            using (var fixture = new TargetFormFixture())
            {
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_targetStartupComplete"), Is.False);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.True);
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PersistenceRecoveryCanExitWithoutSavingUnreadState(bool runtimeReadFailure)
        {
            using (var fixture = new TargetFormFixture())
            {
                Exception error = runtimeReadFailure
                    ? (Exception)new RuntimeStateReadException("~/.codex/mycodex/app-state.json",
                        new InvalidOperationException("ssh: connect to host 192.168.50.3 port 22: Connection refused"))
                    : new PersistenceRecoveryRequiredException("app-state.json", null,
                        new InvalidOperationException("Invalid saved data"), null);
                FormTestDriver.Invoke(fixture.Form, "MarkPersistenceRecoveryPending", error);
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                Assert.That(FormTestDriver.Field<System.Windows.Forms.Timer>(fixture.Form, "_targetStatusTimer").Enabled, Is.False);
                var status = (TargetInstanceStatus)FormTestDriver.Invoke(fixture.Form, "BuildTargetStatus", TargetInstanceLifecycle.Running);
                Assert.That(status.Lifecycle, Is.Not.EqualTo(TargetInstanceLifecycle.Running));
                var activation = fixture.Form.HandleTargetInstanceRequestAsync(new TargetInstanceRequest
                {
                    Operation = TargetInstanceOperation.Activate
                });
                PumpUntil(() => activation.IsCompleted);
                Assert.That(activation.GetAwaiter().GetResult().Success, Is.False);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.True);
                Assert.That(fixture.State.SaveCalls, Is.Zero, "Unread state must never be overwritten on exit.");
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "TryPersistState"), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StartupAuthenticationResultWaitsForExitDecision(bool commit)
        {
            using (var fixture = new TargetFormFixture())
            {
                var pending = new TaskCompletionSource<AppServerAuthenticationState>();
                fixture.Client.ReadAuthentication = () => pending.Task;
                var original = FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState");
                var loading = (Task)FormTestDriver.Invoke(fixture.Form, "RefreshAuthenticationStateAsync", false, false);
                Assert.That(loading.IsCompleted, Is.False);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                var received = new AppServerAuthenticationState { IsAuthenticated = true };
                pending.SetResult(received);
                System.Windows.Forms.Application.DoEvents();
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.SameAs(original));
                Assert.That(loading.IsCompleted, Is.False, "Preparation pauses loading until commit or cancellation.");

                if (commit) Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                else FormTestDriver.Invoke(fixture.Form, "CancelTargetExitPreparation", "startup-exit");
                PumpUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.SameAs(commit ? original : received));
                Assert.That(fixture.Form.Enabled, Is.EqualTo(!commit));
            }
        }

        [Test]
        public void FailedStartupExitSaveLeavesLoadingUsable()
        {
            using (var fixture = new TargetFormFixture())
            {
                var pending = new TaskCompletionSource<AppServerAuthenticationState>();
                fixture.Client.ReadAuthentication = () => pending.Task;
                var loading = (Task)FormTestDriver.Invoke(fixture.Form, "RefreshAuthenticationStateAsync", false, false);
                fixture.State.FailSave = true;
                Assert.That(PrepareStartupExit(fixture), Is.False);
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
                Assert.That(fixture.Form.Enabled, Is.True);
                var received = new AppServerAuthenticationState { IsAuthenticated = true };
                pending.SetResult(received);
                PumpUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<AppServerAuthenticationState>(fixture.Form, "_authenticationState"), Is.SameAs(received));
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
            }
        }

        private static bool PrepareStartupExit(TargetFormFixture fixture)
        {
            var prepare = (Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", "startup-exit", false);
            Assert.That(prepare.IsCompleted, Is.True, "Exit must not wait for the pending startup read.");
            return prepare.GetAwaiter().GetResult();
        }

        [Test]
        public void CommittedExitStopsTheActualStartupChainBeforeConnecting()
        {
            using (var fixture = new TargetFormFixture())
            {
                var files = new PendingModeSettingsFile();
                FormTestDriver.SetField(fixture.Form, "_modeSettingsStore", new JsonModeSettingsStore(fixture.Paths, files));
                var settings = FormTestDriver.Field<ModeSettingsSnapshot>(fixture.Form, "_activeModeSettings");
                typeof(MainForm).GetMethod("OnShown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Invoke(fixture.Form, new object[] { fixture.Form, EventArgs.Empty });
                Assert.That(files.ReadStarted, Is.True);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                files.Pending.SetResult(Tuple.Create(false, string.Empty));
                PumpUntil(() => FormTestDriver.Field<bool>(fixture.Form, "_targetStartupComplete"));
                Assert.That(FormTestDriver.Field<ModeSettingsSnapshot>(fixture.Form, "_activeModeSettings"), Is.SameAs(settings));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_hasAutomaticUpdateCheckRun"), Is.False);
                Assert.That(FormTestDriver.Field<System.Windows.Forms.Timer>(fixture.Form, "_targetStatusTimer").Enabled, Is.False);
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingStartupHistoryCannotChangeTheSavedSessionUntilExitIsCancelled(bool commit)
        {
            using (var fixture = new TargetFormFixture())
            {
                var pending = new TaskCompletionSource<AppServerThreadDetails>();
                var session = new ChatSession { Id = "session", ThreadId = "thread", SessionPath = "original" };
                FormTestDriver.SetField(fixture.Form, "_currentSession", session);
                FormTestDriver.Field<List<ChatSession>>(fixture.Form, "_sessions").Add(session);
                FormTestDriver.SetField(fixture.Form, "_sessionHistoryLoadWorkflow", new SessionHistoryLoadWorkflow(
                    FormTestDriver.Field<SessionHistoryStateStore>(fixture.Form, "_sessionHistoryStateStore"),
                    FormTestDriver.Field<SessionHistoryDisplayBuilder>(fixture.Form, "_sessionHistoryDisplayBuilder"),
                    id => pending.Task, error => false, error => false));
                var loading = (Task)FormTestDriver.Invoke(fixture.Form, "LoadSessionHistoryAsync", session, true);
                Assert.That(loading.IsCompleted, Is.False);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                pending.SetResult(new AppServerThreadDetails { SessionPath = "late-result" });
                System.Windows.Forms.Application.DoEvents();
                Assert.That(loading.IsCompleted, Is.False);
                Assert.That(session.SessionPath, Is.EqualTo("original"));
                if (commit) Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                else FormTestDriver.Invoke(fixture.Form, "CancelTargetExitPreparation", "startup-exit");
                PumpUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                Assert.That(session.SessionPath, Is.EqualTo(commit ? "original" : "late-result"));
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingStartupSyncDoesNotOverwriteSavedTitlesAfterCommit(bool commit)
        {
            using (var fixture = new TargetFormFixture())
            {
                var pending = new TaskCompletionSource<List<AppServerThreadSummary>>();
                var session = new ChatSession { Id = "session", ThreadId = "thread", Title = "original", IsImported = true };
                FormTestDriver.Field<List<ChatSession>>(fixture.Form, "_sessions").Add(session);
                FormTestDriver.SetField(fixture.Form, "_connectionStatus", typeof(MainForm).GetMethod("GetConnectionStatusConnectedText",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null));
                FormTestDriver.SetField(fixture.Form, "_automaticThreadSyncWorkflow", new AutomaticThreadSyncWorkflow(
                    () => Task.FromResult(new List<string>()), () => pending.Task));
                var loading = (Task)FormTestDriver.Invoke(fixture.Form, "RunAutomaticThreadSyncAsync", false, false);
                Assert.That(loading.IsCompleted, Is.False);
                Assert.That(PrepareStartupExit(fixture), Is.True);
                if (commit) Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "startup-exit"), Is.True);
                else FormTestDriver.Invoke(fixture.Form, "CancelTargetExitPreparation", "startup-exit");
                pending.SetResult(new List<AppServerThreadSummary> { new AppServerThreadSummary { ThreadId = "thread", Title = "late-result" } });
                PumpUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                Assert.That(session.Title, Is.EqualTo(commit ? "original" : "late-result"));
                if (commit)
                {
                    Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
                    Assert.That(FormTestDriver.Field<System.Windows.Forms.Timer>(fixture.Form, "_automaticThreadSyncTimer").Enabled, Is.False);
                }
            }
        }

        private sealed class PendingModeSettingsFile : IRuntimeScopedConfigFileStore
        {
            internal readonly TaskCompletionSource<Tuple<bool, string>> Pending = new TaskCompletionSource<Tuple<bool, string>>();
            internal bool ReadStarted;
            public bool TryGetRuntimeAppStateFilePathForAsyncAccess(AppConfigSnapshot config, out string path) { path = null; return false; }
            public bool TryGetRuntimeModeSettingsFilePathForAsyncAccess(AppConfigSnapshot config, out string path) { path = "mode.json"; return true; }
            public Task<Tuple<bool, string>> ReadTextFileAsync(AppConfigSnapshot config, string path) { ReadStarted = true; return Pending.Task; }
            public bool ReadTextFile(AppConfigSnapshot config, string path, out string content) => throw new NotSupportedException();
            public void WriteTextFile(AppConfigSnapshot config, string path, string content) => throw new NotSupportedException();
            public Task WriteTextFileAsync(AppConfigSnapshot config, string path, string content) => throw new NotSupportedException();
        }
    }
}
