using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void UnchangedHistoryRefreshDoesNotInvalidateOrReselect(bool withSelection)
        {
            using (var fixture = new TargetFormFixture())
            {
                var history = FormTestDriver.Field<ChatHistoryListBox>(fixture.Form, "_chatHistoryListBox");
                history.Dock = DockStyle.None;
                history.ClientSize = new Size(400, history.ItemHeight * 3);
                var handle = history.Handle;
                var replace = typeof(MainForm).GetMethod("ReplaceChatHistoryItems", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(ChatHistoryItem[]), typeof(bool) }, null);
                var texts = Enumerable.Range(0, 12).Select(index => index == 0
                    ? new string('x', 100000) + "\r\nlast line" : "message " + index).ToArray();
                Func<ChatHistoryItem[]> items = () => texts.Select(text => ChatHistoryItemFactory.Create("User", text)).ToArray();
                replace.Invoke(fixture.Form, new object[] { items(), true });
                if (withSelection)
                {
                    history.SelectSingleIndex(2);
                    history.ProcessExplorerSelectionKeyForTests(Keys.Shift | Keys.Down);
                    history.TopIndex = 1;
                }
                else history.TopIndex = history.Items.Count - 1;
                var selected = history.GetSelectedIndicesInDisplayOrder();
                var focused = history.FocusedIndex;
                var top = history.TopIndex;
                var invalidations = 0;
                var selectionChanges = 0;
                history.Invalidated += (sender, args) => invalidations++;
                history.SelectedIndexChanged += (sender, args) => selectionChanges++;

                for (var iteration = 0; iteration < 3; iteration++)
                    replace.Invoke(fixture.Form, new object[] { items(), true });

                Assert.Multiple(() =>
                {
                    Assert.That(invalidations, Is.Zero);
                    Assert.That(selectionChanges, Is.Zero);
                    Assert.That(history.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(selected));
                    Assert.That(history.FocusedIndex, Is.EqualTo(focused));
                    Assert.That(history.TopIndex, Is.EqualTo(top));
                });
            }
        }

        [Test]
        public void UnselectedHistoryRefreshStillFollowsLatestMessageWhenViewportIsAboveIt()
        {
            using (var fixture = new TargetFormFixture())
            {
                var history = FormTestDriver.Field<ChatHistoryListBox>(fixture.Form, "_chatHistoryListBox");
                history.Dock = DockStyle.None;
                history.ClientSize = new Size(400, history.ItemHeight * 3);
                var handle = history.Handle;
                var items = Enumerable.Range(0, 12).Select(index => ChatHistoryItemFactory.Create("User", "message " + index)).ToArray();
                var replace = typeof(MainForm).GetMethod("ReplaceChatHistoryItems", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(ChatHistoryItem[]), typeof(bool) }, null);
                replace.Invoke(fixture.Form, new object[] { items, true });
                history.TopIndex = 0;

                replace.Invoke(fixture.Form, new object[] { items, false });

                Assert.That(history.TopIndex, Is.GreaterThan(0));
                Assert.That(history.GetItemRectangle(history.Items.Count - 1).Bottom, Is.LessThanOrEqualTo(history.ClientSize.Height));
            }
        }

        [Test]
        public void WindowsTitlesOmitTargetNameEvenWhenOtherTargetsAreConfigured()
        {
            using (var fixture = new TargetFormFixture())
            {
                Assert.That(FormTestDriver.Invoke(fixture.Form, "GetCurrentRuntimeDisplayName"), Is.EqualTo(UiLocalization.Text("Target.Name.Local", "This PC")));
                Assert.That(fixture.Form.Text, Does.StartWith("MyCodex"));
                Assert.That(FormTestDriver.Invoke(fixture.Form, "GetCurrentWindowRuntimeDisplayName"), Is.Null);
                Assert.That(FormTestDriver.Invoke(fixture.Form, "BuildRuntimeFileWindowTitle", "Files"), Is.EqualTo("Files"));
                Assert.That(FormTestDriver.Field<NotifyIcon>(fixture.Form, "_trayIcon").Text, Does.StartWith(UiLocalization.Text("Target.Name.Local", "This PC") + " — "));
                fixture.Catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
                FormTestDriver.Invoke(fixture.Form, "RefreshWindowTitle");
                Assert.That(fixture.Form.Text, Does.StartWith("MyCodex"));
            }
        }

        [TestCase("Wsl", "My Linux")]
        [TestCase("Ssh", "Build server")]
        public void OtherTargetTitlesUseOnlyConfiguredName(string kind, string name)
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.SetField(fixture.Form, "_fixedTarget", new TargetProfileSnapshot { Id = "other", Kind = kind, Name = name });
                FormTestDriver.Invoke(fixture.Form, "RefreshWindowTitle");

                Assert.That(fixture.Form.Text, Does.StartWith(name + " - MyCodex"));
                Assert.That(FormTestDriver.Invoke(fixture.Form, "GetCurrentWindowRuntimeDisplayName"), Is.EqualTo(name));
                Assert.That(FormTestDriver.Invoke(fixture.Form, "BuildRuntimeFileWindowTitle", "Files"), Is.EqualTo(name + " - Files"));
            }
        }

        [Test]
        public void NativeTargetPopupShowsNamesAndKeepsManagerCommandUsableAcrossCatalogChanges()
        {
            using (var fixture = new TargetFormFixture())
            {
                var targets = FormTestDriver.Field<MenuItem>(fixture.Form, "_targetsMenuItem");
                var manager = FormTestDriver.Field<MenuItem>(fixture.Form, "_appServerRuntimeSettingsMenuItem");
                for (var opening = 0; opening < 3; opening++)
                {
                    if (opening == 1) fixture.Catalog.AddWsl("Linux & test", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
                    if (opening == 2) fixture.Catalog.AddSsh("Remote test", new RemoteMacSshConfigSnapshot
                    { HostName = "test.example.invalid", UserName = "test" });
                    // Exercise the real native popup path, rather than MenuItem.PerformClick.
                    SendMessage(fixture.Form.Handle, 0x0117, targets.Handle, IntPtr.Zero);
                    var popup = targets.Handle;
                    var command = GetMenuItemID(popup, manager.Index);
                    var rows = targets.MenuItems.Cast<MenuItem>().Where(item => item.Tag is TargetProfileSnapshot).ToArray();
                    var rowCommands = rows.Select(item => GetMenuItemID(popup, item.Index)).ToArray();
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(GetMenuItemCount(popup), Is.EqualTo(targets.MenuItems.Count),
                        "The native popup displayed to the user must remain valid.");
                    Assert.That(GetMenuItemID(popup, manager.Index), Is.EqualTo(command),
                        "The selected native command must still open the manager.");
                    Assert.That(command, Is.Not.EqualTo(uint.MaxValue));
                    Assert.That(rows.Length, Is.EqualTo(opening + 1));
                    Assert.That(rows.Select(item => GetMenuItemID(popup, item.Index)), Is.EqualTo(rowCommands));
                    Assert.That(rows.Select(item => item.Text), Is.EqualTo(new[]
                    {
                        UiLocalization.Text("Target.Name.Local", "This PC"), "Linux && test", "Remote test"
                    }.Take(opening + 1)));
                    Assert.That(rows.Count(item => item.Checked), Is.EqualTo(1));
                    var opened = false;
                    var timeout = Stopwatch.StartNew();
                    using (var closer = new System.Windows.Forms.Timer { Interval = 20 })
                    {
                        closer.Tick += (sender, args) =>
                        {
                            var dialog = FormTestDriver.Field<TargetManagerForm>(fixture.Form, "_targetManagerForm");
                            if (dialog == null || !dialog.Visible) return;
                            opened = true;
                            if (!FormTestDriver.Field<bool>(dialog, "_busy")) dialog.Close();
                            else if (timeout.Elapsed > TimeSpan.FromSeconds(5)) dialog.Dispose();
                        };
                        closer.Start();
                        // Enter on a native menu item dispatches WM_COMMAND, not MenuItem.PerformClick.
                        SendMessage(fixture.Form.Handle, 0x0111, new IntPtr(command), IntPtr.Zero);
                        closer.Stop();
                    }
                    Assert.That(opened, Is.True, "The native manager command did not open its dialog.");
                    Assert.That(manager.Parent, Is.SameAs(targets));
                    Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
                }
            }
        }

        [TestCase("Windows", null)]
        [TestCase("Wsl", "Ubuntu")]
        [TestCase("Ssh", "Mac")]
        public void NativePhoneConnectionOpensGlobalSettingsFromItsButtonAndRestoresFocusWithoutSaving(string kind, string name)
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.SetField(fixture.Form, "_fixedTarget", new TargetProfileSnapshot { Id = "windows", Kind = kind, Name = name });
                var targetName = (string)FormTestDriver.Invoke(fixture.Form, "GetCurrentRuntimeDisplayName");
                var store = new JsonRemoteServiceConfigStore(fixture.Paths);
                var snapshot = store.Load();
                RemoteServiceConfigPlanner.SelectMode(snapshot, "External");
                snapshot.RemoteEnabled = false;
                store.Save(snapshot);
                FormTestDriver.Invoke(fixture.Form, "PrepareOwnedRemoteDisplayName", store, store.Load());
                var sharedPath = Path.Combine(fixture.Paths.GetSharedDataDirectory(), "config", "relay-service.json");
                var sharedBefore = File.ReadAllBytes(sharedPath);
                var identityBefore = File.ReadAllBytes(fixture.Paths.GetRemoteServiceConfigFilePath());
                var settings = FormTestDriver.Field<MenuItem>(fixture.Form, "_settingsMenuItem");
                var connection = FormTestDriver.Field<MenuItem>(fixture.Form, "_remoteServiceMenuItem");
                Assert.That(connection.Parent, Is.SameAs(settings));
                Assert.That(connection.MenuItems.Count, Is.Zero);
                Assert.That(settings.MenuItems.Count, Is.EqualTo(7));
                Assert.That(connection.Text, Is.EqualTo(UiLocalization.Text("Settings.Menu.RemoteService", "Phone Connection(&R)...")));

                var opened = false;
                var focused = false;
                var restoredFocus = false;
                var correctOwner = false;
                Form managementOwner = null;
                string title = null;
                using (var closer = new System.Windows.Forms.Timer { Interval = 20 })
                {
                    closer.Tick += (sender, args) =>
                    {
                        var dialog = System.Windows.Forms.Application.OpenForms.OfType<RemoteServiceSettingsForm>().FirstOrDefault();
                        if (dialog != null && dialog.Visible)
                        {
                            opened = true;
                            focused = dialog.ActiveControl == dialog.InitialFocusControlForTests;
                            correctOwner = dialog.Owner == managementOwner;
                            FormTestDriver.Field<TargetWindowGroup>(fixture.Form, "_targetWindowGroup").RefreshTitles();
                            FormTestDriver.Field<TargetWindowGroup>(fixture.Form, "_targetWindowGroup").RefreshTitles();
                            title = dialog.Text;
                            dialog.Close();
                            return;
                        }
                        var management = System.Windows.Forms.Application.OpenForms.OfType<RemoteServiceManagementForm>().FirstOrDefault();
                        if (management == null || !management.Visible || !management.Enabled) return;
                        managementOwner = management;
                        var button = FormTestDriver.Field<Button>(management, "_serverSettingsButton");
                        button.PerformClick();
                        restoredFocus = management.ActiveControl == button;
                        management.Close();
                    };
                    closer.Start();
                    var command = GetMenuItemID(settings.Handle, connection.Index);
                    SendMessage(fixture.Form.Handle, 0x0111, new IntPtr(command), IntPtr.Zero);
                    closer.Stop();
                }
                Assert.That(opened, Is.True);
                Assert.That(focused, Is.True);
                Assert.That(correctOwner, Is.True);
                Assert.That(restoredFocus, Is.True);
                Assert.That(title, Is.EqualTo(UiLocalization.Text("SharedRemoteService.Title", "Phone Connection Settings") + " - " + targetName));
                Assert.That(File.ReadAllBytes(sharedPath), Is.EqualTo(sharedBefore));
                Assert.That(File.ReadAllBytes(fixture.Paths.GetRemoteServiceConfigFilePath()), Is.EqualTo(identityBefore));
                Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void DecliningTheNativeRuntimePromptDoesNotStartInstallationOrExit()
        {
            using (var fixture = new TargetFormFixture())
            using (var responder = new System.Windows.Forms.Timer { Interval = 20 })
            {
                var sawConfirmation = false;
                var defaultedToNo = false;
                responder.Tick += (sender, args) => EnumerateExitThreadWindows(GetExitCurrentThreadId(), (window, state) =>
                {
                    var name = new System.Text.StringBuilder(64);
                    GetExitWindowClass(window, name, name.Capacity);
                    if (name.ToString() != "#32770" || GetExitWindow(window, 4) != fixture.Form.Handle) return true;
                    sawConfirmation = true;
                    defaultedToNo = (SendMessage(window, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0xffff) == 7;
                    PostExitWindowMessage(window, 0x0111, new IntPtr(7), IntPtr.Zero);
                    return true;
                }, IntPtr.Zero);
                responder.Start();
                var offered = (Task<bool>)FormTestDriver.Invoke(fixture.Form, "OfferBundledRuntimeUpdateAsync", new CodexRuntimeUpdateInfo
                {
                    InstalledVersion = "0.162.1", LatestVersion = "0.163.0", TargetTriple = "x86_64-pc-windows-msvc"
                }, CancellationToken.None);
                responder.Stop();
                Assert.That(offered.GetAwaiter().GetResult(), Is.False);
                Assert.That(sawConfirmation, Is.True);
                Assert.That(defaultedToNo, Is.True);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                Assert.That(FormTestDriver.Field<CodexRuntimeUpdateResult>(fixture.Form, "_installedBundledRuntimeUpdate"), Is.Null);
                Assert.That(System.Windows.Forms.Application.OpenForms.OfType<BundledRuntimeUpdateForm>(), Is.Empty);
                Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [TestCase("ReconnectAfterAuthenticationAsync")]
        [TestCase("RestartAppServerAfterOfficeCliEnvironmentChangeAsync")]
        [TestCase("RestartAppServerAfterInvalidEncryptedContentRepairFailureAsync")]
        [TestCase("RestartAppServerForRuntimeSettingsAsync")]
        public void EveryRestartPathWaitsForAnInProgressRuntimeUpdate(string method)
        {
            using (var fixture = new TargetFormFixture())
            {
                var finished = new TaskCompletionSource<bool>();
                var coordinator = new BundledRuntimeUpdateCoordinator(token => Task.FromResult(new CodexRuntimeUpdateInfo
                {
                    InstalledVersion = "0.162.0", LatestVersion = "0.162.1", TargetTriple = "x86_64-pc-windows-msvc"
                }), (info, token) => finished.Task);
                var updating = coordinator.EnsureAsync(MyCodex.Shared.Constants.CodexRuntimeSources.Bundled, CancellationToken.None);
                FormTestDriver.SetField(fixture.Form, "_bundledRuntimeUpdates", coordinator);
                var args = method == "RestartAppServerForRuntimeSettingsAsync"
                    ? new object[] { MyCodex.Domain.Enums.AppInteractionMode.Coding, false, null, null } : new object[0];
                var task = (Task)FormTestDriver.Invoke(fixture.Form, method, args);
                Assert.That(task.IsCompleted, Is.False);
                Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
                Assert.That(coordinator.HasPendingUpdate, Is.True);
                var runtimeSettings = method == "RestartAppServerForRuntimeSettingsAsync";
                // That path prepares a real authentication host after the gate; stop before external work.
                if (runtimeSettings) FormTestDriver.Field<CancellationTokenSource>(fixture.Form, "_targetShutdownCancellation").Cancel();
                finished.SetResult(false);
                PumpUntil(() => task.IsCompleted && updating.IsCompleted);
                task.GetAwaiter().GetResult();
                Assert.That(updating.GetAwaiter().GetResult(), Is.True);
                Assert.That(fixture.Client.RestartCalls, Is.EqualTo(runtimeSettings ? 0 : 1));
                Assert.That(coordinator.HasPendingUpdate, Is.False);
            }
        }

        [Test]
        public void SwitchingASavedSystemRuntimeBackToBundledChecksBeforeAnyConnection()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var edit = fixture.Catalog.LoadRuntimeSettings("windows");
                edit.CodexRuntimeSource = MyCodex.Shared.Constants.CodexRuntimeSources.System;
                Assert.That(Apply(fixture.Form, edit).Success, Is.True);
                Assert.That(fixture.RuntimeChecks, Is.Zero);
                edit = fixture.Catalog.LoadRuntimeSettings("windows");
                edit.CodexRuntimeSource = MyCodex.Shared.Constants.CodexRuntimeSources.Bundled;
                Assert.That(Apply(fixture.Form, edit).Success, Is.True);
                Assert.That(fixture.RuntimeChecks, Is.EqualTo(1));
                Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void FailedDraftPersistenceRejectsOwnerEditWithoutChangingRuntimeOrCatalog()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var edit = fixture.Catalog.LoadRuntimeSettings("windows");
                edit.Name = "Must remain a draft";
                var configBytes = File.ReadAllBytes(fixture.Paths.GetConfigFilePath());
                var catalogBytes = File.ReadAllBytes(Path.Combine(fixture.Root, "targets", "catalog.json"));
                fixture.State.FailSave = true;
                var savesBefore = fixture.State.SaveCalls;

                var reply = Apply(fixture.Form, edit);

                Assert.That(reply.Success, Is.False);
                Assert.That(reply.SettingsSaved, Is.False);
                Assert.That(fixture.State.SaveCalls, Is.GreaterThan(savesBefore));
                Assert.That(File.ReadAllBytes(fixture.Paths.GetConfigFilePath()), Is.EqualTo(configBytes));
                Assert.That(File.ReadAllBytes(Path.Combine(fixture.Root, "targets", "catalog.json")), Is.EqualTo(catalogBytes));
                Assert.That(edit.Name, Is.EqualTo("Must remain a draft"));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void LiveSshOwnerRejectsHostChangeBeforeSavingOrRestarting()
        {
            using (var fixture = new TargetFormFixture(withSshTarget: true))
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var targetId = FormTestDriver.Field<TargetProfileSnapshot>(fixture.Form, "_fixedTarget").Id;
                var edit = fixture.Catalog.LoadRuntimeSettings(targetId);
                edit.RemoteSsh.HostName = "correct.example";
                var before = File.ReadAllBytes(fixture.Paths.GetConfigFilePath());
                var savesBefore = fixture.State.SaveCalls;
                var direct = Apply(fixture.Form, edit);
                Assert.That(direct.Success, Is.False);
                Assert.That(direct.SettingsSaved, Is.False);
                Assert.That(direct.Error, Is.EqualTo(UiLocalization.Text("TargetSettingsForm.Error.HostRequiresClosed", "Close this target before changing its SSH host. Your draft is retained.")));
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(savesBefore));
                Assert.That(File.ReadAllBytes(fixture.Paths.GetConfigFilePath()), Is.EqualTo(before));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void InitialStartupRejectsFiniteEditsAndKeepsStartingStatus()
        {
            using (var fixture = new TargetFormFixture())
            {
                var edit = fixture.Catalog.LoadRuntimeSettings("windows");
                var originalName = edit.Name;
                edit.Name = "Not during initialization";
                var direct = Apply(fixture.Form, edit);
                var wire = TargetInstanceCoordinator.Send(fixture.Channel, "windows", fixture.Root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.ApplyRuntimeSettings, RuntimeSettings = edit });
                var status = TargetInstanceCoordinator.Send(fixture.Channel, "windows", fixture.Root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });

                Assert.That(direct.Success, Is.False);
                Assert.That(wire.Success, Is.False);
                Assert.That(status.Success, Is.True, status.Error);
                Assert.That(status.Status.Lifecycle, Is.EqualTo(TargetInstanceLifecycle.Starting));
                Assert.That(FormTestDriver.Field<System.Windows.Forms.Timer>(fixture.Form, "_targetStatusTimer").Enabled, Is.False);
                Assert.That(fixture.Catalog.GetTarget("windows").Name, Is.EqualTo(originalName));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void ClosingUiRejectsPreviouslyQueuedOwnerRequests()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var edit = fixture.Catalog.LoadRuntimeSettings("windows");
                edit.Name = "Too late";
                var queued = fixture.Form.HandleTargetInstanceRequestAsync(new TargetInstanceRequest
                {
                    Operation = TargetInstanceOperation.ApplyRuntimeSettings, RuntimeSettings = edit
                });
                FormTestDriver.SetField(fixture.Form, "_isExitRequested", true);
                FormTestDriver.Invoke(fixture.Form, "MarkTargetClosing");
                PumpUntil(() => queued.IsCompleted);

                Assert.That(queued.GetAwaiter().GetResult().Success, Is.False);
                Assert.That(fixture.Catalog.GetTarget("windows").Name, Is.Not.EqualTo("Too late"));
                var status = TargetInstanceCoordinator.Send(fixture.Channel, "windows", fixture.Root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                Assert.That(status.Status.Lifecycle, Is.EqualTo(TargetInstanceLifecycle.Closing));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void RenameUpdatesTargetAndTrayNamesPreservingWindowsTitleIdentityPathsAndPreferences()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var beforeTarget = fixture.Catalog.GetTarget("windows");
                var identityStore = new JsonRemoteServiceConfigStore(fixture.Paths);
                var beforeIdentity = identityStore.LoadTargetIdentity();
                var beforeConfig = fixture.Config.Load();
                var dataRoot = fixture.Paths.GetAppDataDirectory();
                var runtimeRoot = fixture.Paths.GetRuntimeDirectory();
                var edit = fixture.Catalog.LoadRuntimeSettings("windows");
                edit.Name = "Renamed workstation";

                var reply = Apply(fixture.Form, edit);

                Assert.That(reply.Success, Is.True, reply.Error);
                Assert.That(reply.SettingsSaved, Is.True);
                Assert.That(fixture.Form.Text, Does.Not.Contain("Renamed workstation"));
                Assert.That(fixture.Form.Text, Does.StartWith("MyCodex"));
                Assert.That(FormTestDriver.Invoke(fixture.Form, "GetCurrentRuntimeDisplayName"), Is.EqualTo("Renamed workstation"));
                Assert.That(FormTestDriver.Field<NotifyIcon>(fixture.Form, "_trayIcon").Text, Does.Contain("Renamed workstation"));
                var afterTarget = fixture.Catalog.GetTarget("windows");
                Assert.That(afterTarget.Id, Is.EqualTo(beforeTarget.Id));
                Assert.That(afterTarget.RuntimeScopeId, Is.EqualTo(beforeTarget.RuntimeScopeId));
                Assert.That(fixture.Catalog.GetPaths(afterTarget.Id).GetAppDataDirectory(), Is.EqualTo(dataRoot));
                Assert.That(fixture.Catalog.GetPaths(afterTarget.Id).GetRuntimeDirectory(), Is.EqualTo(runtimeRoot));
                var afterIdentity = identityStore.LoadTargetIdentity();
                Assert.That(afterIdentity.HostId, Is.EqualTo(beforeIdentity.HostId));
                Assert.That(afterIdentity.ThisPcName, Is.EqualTo("Renamed workstation"));
                var afterConfig = fixture.Config.Load();
                Assert.That(afterConfig.UiThemePreference, Is.EqualTo(beforeConfig.UiThemePreference));
                Assert.That(afterConfig.EnableAutomaticChatReading, Is.EqualTo(beforeConfig.EnableAutomaticChatReading));
                Assert.That(afterConfig.CodexRuntimeSource, Is.EqualTo(beforeConfig.CodexRuntimeSource));
                Assert.That(afterConfig.CodexCustomExecutablePath, Is.EqualTo(beforeConfig.CodexCustomExecutablePath));
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void RealNestedModalDialogsSelectEnabledEditorInsteadOfDisabledManager()
        {
            using (var owner = IsolatedDialog())
            using (var manager = IsolatedDialog())
            using (var editor = IsolatedDialog())
            {
                Form selected = null;
                var managerDisabled = false;
                var editorEnabled = false;
                Exception error = null;
                editor.Shown += (sender, args) => editor.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // ShowDialog disables the owner's HWND without changing its managed Enabled value.
                        managerDisabled = !IsWindowEnabled(manager.Handle);
                        editorEnabled = editor.Enabled && IsWindowEnabled(editor.Handle);
                        selected = (Form)typeof(MainForm).GetMethod("FindVisibleModalDialog", BindingFlags.Static | BindingFlags.NonPublic)
                            .Invoke(null, new object[] { owner });
                    }
                    catch (Exception exception) { error = exception; }
                    finally { editor.Close(); }
                }));
                manager.Shown += (sender, args) => manager.BeginInvoke(new Action(() =>
                {
                    try { editor.ShowDialog(manager); }
                    catch (Exception exception) { error = exception; }
                    finally { manager.Close(); }
                }));

                manager.ShowDialog(owner);

                Assert.That(error, Is.Null);
                Assert.That(managerDisabled, Is.True);
                Assert.That(editorEnabled, Is.True);
                Assert.That(selected, Is.SameAs(editor));
            }
        }

        private static Form IsolatedDialog()
        {
            return new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), Size = new Size(240, 140) };
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr menu);

        [DllImport("user32.dll")]
        private static extern uint GetMenuItemID(IntPtr menu, int position);
        private static TargetInstanceReply Apply(MainForm form, TargetRuntimeSettings settings)
        {
            var task = (Task<TargetInstanceReply>)FormTestDriver.Invoke(form, "ApplyOwnedTargetRuntimeSettingsAsync", settings);
            PumpUntil(() => task.IsCompleted);
            return task.GetAwaiter().GetResult();
        }

        private static void PumpUntil(Func<bool> complete)
        {
            var timer = Stopwatch.StartNew();
            while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.That(complete(), Is.True, "The isolated UI operation did not complete.");
        }

        private sealed class TargetFormFixture : IDisposable
        {
            public readonly string Root;
            public readonly string Channel;
            public readonly TargetCatalogStore Catalog;
            public readonly AppConfigService Paths;
            public readonly JsonAppConfigStore Config;
            public readonly StateStore State;
            public readonly OfflineClient Client;
            public readonly MainForm Form;
            public int RuntimeChecks;
            private readonly TargetInstanceCoordinator _instance;

            public TargetFormFixture(bool withTargetContext = true, bool withSshTarget = false)
            {
                Root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-form-" + Guid.NewGuid().ToString("N"));
                Channel = "TargetForm-" + Guid.NewGuid().ToString("N");
                var legacy = new AppConfigService(Root);
                new TargetDataMigration(legacy).Run();
                Catalog = new TargetCatalogStore(legacy);
                var targetId = withSshTarget ? Catalog.AddSsh("Mac", new RemoteMacSshConfigSnapshot
                    { HostName = "wrong.example", UserName = "test" }).Id : "windows";
                Paths = Catalog.GetPaths(targetId);
                Config = new JsonAppConfigStore(Paths);
                var preferences = Config.Load();
                preferences.UiThemePreference = "dark";
                preferences.EnableAutomaticChatReading = true;
                Config.Save(preferences);
                State = new StateStore(new JsonAppStateStore(Paths).Load());
                var runtime = new CodexRuntimeManager(Paths);
                Client = new OfflineClient(runtime, Config);
                Form = new MainForm(Client, State, Config, Paths, new Reader(), runtime);
                FormTestDriver.SetField(Form, "_bundledRuntimeUpdates", new BundledRuntimeUpdateCoordinator(token =>
                {
                    RuntimeChecks++;
                    return Task.FromResult(new CodexRuntimeUpdateInfo { InstalledVersion = "0.162.1", LatestVersion = "0.162.1" });
                }, (info, token) => throw new AssertionException("No actual downloads in isolated target tests.")));
                _instance = TargetInstanceCoordinator.Acquire(Channel, targetId, Root);
                if (withTargetContext) Form.ConfigureTargetContext(Catalog, _instance, Channel);
                var handle = Form.Handle;
                _instance.StartServer(Form.HandleTargetInstanceRequestAsync);
            }

            public void Dispose()
            {
                // Never raise MainForm.FormClosed: its production teardown can stop real user services.
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                    Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), Form, "OnSystemUserPreferenceChanged");
                FormTestDriver.Field<System.Windows.Forms.Timer>(Form, "_targetStatusTimer")?.Dispose();
                FormTestDriver.Invoke(Form, "DisposeThreadSyncTimersOnExit");
                FormTestDriver.Invoke(Form, "DisposeLocalSyncTimersOnExit");
                FormTestDriver.Invoke(Form, "DisposeRateLimitAutoResetTimerOnExit");
                FormTestDriver.Invoke(Form, "DisposeRemoteHostService");
                FormTestDriver.Invoke(Form, "DisposeTrayIconOnExit");
                Form.Dispose();
                _instance.Dispose();
                Client.Dispose();
                Directory.Delete(Root, true);
            }
        }

        private sealed class StateStore : IAppStateStore
        {
            private AppStateSnapshot _snapshot;
            public bool FailSave;
            public Exception SaveError;
            public int SaveCalls;
            public StateStore(AppStateSnapshot snapshot) { _snapshot = snapshot; }
            public AppStateSnapshot Load() => _snapshot;
            public void Save(AppStateSnapshot snapshot)
            {
                SaveCalls++;
                if (SaveError != null) throw SaveError;
                if (FailSave) throw new IOException("Injected draft write failure.");
                _snapshot = snapshot;
            }
        }

        private sealed class OfflineClient : AppServerClient, IAppServerClient
        {
            public int ConnectCalls;
            public int RestartCalls;
            public Func<Task<AppServerAuthenticationState>> ReadAuthentication;
            public OfflineClient(CodexRuntimeManager runtime, IAppConfigStore config) : base(runtime, config) { }
            public new Task ConnectAsync() { ConnectCalls++; throw new InvalidOperationException("Unexpected connection in an isolated UI test."); }
            public new Task RestartAsync() { RestartCalls++; throw new InvalidOperationException("Unexpected restart in an isolated UI test."); }
            public new Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                return ReadAuthentication != null ? ReadAuthentication() : throw new InvalidOperationException("Unexpected authentication read.");
            }
        }

        private sealed class Reader : IScreenReaderService
        {
            public bool IsAvailable => false;
            public bool IsInitialized => false;
            public string LastError => null;
            public bool EnsureInitialized() => false;
            public bool IsReaderRunning() => false;
            public int GetReaderState() => 0;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) => false;
            public bool Stop(bool useSlave) => false;
            public void Dispose() { }
        }
    }
}
