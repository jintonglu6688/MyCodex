using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class TargetManagerFormTests
    {
        private string _root;
        private TargetCatalogStore _catalog;
        private string _channel;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-manager-" + Guid.NewGuid().ToString("N"));
            _channel = "ManagerTest-" + Guid.NewGuid().ToString("N");
            var paths = new AppConfigService(_root);
            new TargetDataMigration(paths).Run();
            _catalog = new TargetCatalogStore(paths);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void Constructor_PreservesNativeAccessibleResizableTaskOrder()
        {
            using (var form = new TargetManagerForm(_catalog, _channel, "MyCodex Test"))
            {
                var targets = FormTestDriver.Field<ListView>(form, "_targets");
                var edit = FormTestDriver.Field<Button>(form, "_edit");
                var close = FormTestDriver.Field<Button>(form, "_close");
                var refresh = FormTestDriver.Field<Button>(form, "_refresh");
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(680));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(380));
                Assert.That(targets.View, Is.EqualTo(View.Details));
                Assert.That(targets.MultiSelect, Is.False);
                Assert.That(targets.AccessibleName, Is.EqualTo(UiLocalization.Text("TargetManagerForm.Accessible.Targets", "Targets")));
                Assert.That(form.Controls.OfType<TextBox>(), Is.Empty);
                Assert.That(edit.Parent.Controls.OfType<Button>().ToArray(), Has.Length.EqualTo(6));
                Assert.That(edit.Parent.TabIndex, Is.GreaterThan(targets.TabIndex));
                Assert.That(edit.Parent.TabStop, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_status").TabStop, Is.False);
                Assert.That(refresh.TabStop, Is.False);
                Assert.That(form.AcceptButton, Is.Null);
                Assert.That(form.ActiveControl, Is.SameAs(targets));
                Assert.That(form.CancelButton, Is.SameAs(close));
                var previousIndex = -1;
                foreach (var name in new[] { "_refresh", "_addWsl", "_addSsh", "_edit", "_remove", "_close" })
                {
                    var button = FormTestDriver.Field<Button>(form, name);
                    Assert.That(button.AccessibleName, Is.Not.Empty, name);
                    Assert.That(button.TabIndex, Is.GreaterThan(previousIndex), name);
                    previousIndex = button.TabIndex;
                }
            }
        }

        [Test]
        public void SelectionEnterAndDoubleClickDoNotOpenOrSaveTargets()
        {
            var linux = _catalog.AddWsl("Work Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var beforeCatalog = File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json"));
            var saves = 0;
            using (var form = new TargetManagerForm(_catalog, _channel, "MyCodex Test", (id, settings) =>
            {
                saves++;
                return Task.FromResult(new TargetInstanceReply { Success = true });
            }))
            {
                var targets = FormTestDriver.Field<ListView>(form, "_targets");
                var handle = targets.Handle;
                var row = new ListViewItem(linux.Name) { Tag = linux };
                row.SubItems.Add("WSL");
                row.SubItems.Add("Not running");
                targets.Items.Add(row);
                row.Selected = true;

                Assert.That(targets.SelectedItems.Count, Is.EqualTo(1));
                Assert.That(FormTestDriver.Field<Button>(form, "_edit").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<bool>(form, "_busy"), Is.False);
                typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(targets, new object[] { EventArgs.Empty });
                var enterHandled = (bool)typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { Keys.Enter });
                Assert.That(enterHandled, Is.False);
                Assert.That(saves, Is.Zero);
                Assert.That(File.ReadAllBytes(Path.Combine(_root, "targets", "catalog.json")), Is.EqualTo(beforeCatalog));

                row.Selected = false;
                Assert.That(FormTestDriver.Field<Button>(form, "_edit").Enabled, Is.False);
                Assert.That(saves, Is.Zero);
            }
        }

        [Test]
        public void ShowingManagerKeepsNativeFocusOnListDuringAndAfterInitialLoad()
        {
            using (var form = new TargetManagerForm(_catalog, _channel, "MyCodex Test", currentTargetId: "windows"))
            using (var closer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                var targets = FormTestDriver.Field<ListView>(form, "_targets");
                var initiallyFocused = false;
                var loadedFocused = false;
                string selectedId = null;
                var timeout = Stopwatch.StartNew();
                form.Shown += (sender, args) => initiallyFocused = targets.Focused && targets.Enabled;
                closer.Tick += (sender, args) =>
                {
                    if (FormTestDriver.Field<bool>(form, "_busy"))
                    {
                        if (timeout.Elapsed > TimeSpan.FromSeconds(5)) form.Dispose();
                        return;
                    }
                    loadedFocused = targets.Focused;
                    if (targets.SelectedItems.Count == 1)
                        selectedId = ((TargetProfileSnapshot)targets.SelectedItems[0].Tag).Id;
                    form.Close();
                };
                closer.Start();
                form.ShowDialog();
                closer.Stop();

                Assert.That(initiallyFocused, Is.True);
                Assert.That(loadedFocused, Is.True);
                Assert.That(selectedId, Is.EqualTo("windows"));
            }
        }

        [TestCase("Windows", "Custom PC", "Custom PC")]
        [TestCase("Wsl", "Ubuntu", "Ubuntu")]
        [TestCase("Ssh", "Development", "Development")]
        public void TargetNamesDoNotAddPlatformOrStatus(string kind, string name, string expected)
        {
            Assert.That(TargetManagerForm.DisplayName(new TargetProfileSnapshot { Kind = kind, Name = name }), Is.EqualTo(expected));
        }

        [Test]
        public void DefaultWindowsNameIsLocalizedThisPc()
        {
            Assert.That(TargetManagerForm.DisplayName(new TargetProfileSnapshot { Kind = "Windows", Name = "Windows" }),
                Is.EqualTo(UiLocalization.Text("Target.Name.Local", "This PC")));
        }

        [Test]
        public void RemovalSelectionIsExplicitAndWindowsCannotBeRemoved()
        {
            var calls = 0;
            var linux = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            using (var form = new TargetManagerForm(_catalog, _channel, "MyCodex Test", removeAsync: id =>
            {
                calls++;
                return Task.FromResult(new TargetInstanceReply { Success = true });
            }))
            {
                var targets = FormTestDriver.Field<ListView>(form, "_targets");
                var handle = targets.Handle;
                var windowsRow = new ListViewItem("Windows") { Tag = _catalog.GetTarget("windows") };
                var linuxRow = new ListViewItem("Linux") { Tag = linux };
                targets.Items.AddRange(new[] { windowsRow, linuxRow });
                windowsRow.Selected = true;
                Assert.That(FormTestDriver.Field<Button>(form, "_remove").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_status").Text, Is.EqualTo(UiLocalization.Text("TargetManagerForm.Removal.Status.Windows", "The local Windows target cannot be removed.")));
                windowsRow.Selected = false;
                linuxRow.Selected = true;
                Assert.That(FormTestDriver.Field<Button>(form, "_remove").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<Label>(form, "_status").Text, Is.Empty);
                Assert.That(calls, Is.Zero);
            }
        }

        [Test]
        public void RemovalConfirmationDefaultsToCancelAndUsesExplicitAccessibleActions()
        {
            using (var form = TargetManagerForm.CreateRemovalConfirmation(new TargetProfileSnapshot { Kind = "Ssh", Name = "Build machine" }))
            {
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(540));
                Assert.That(form.Text, Does.Contain("Build machine"));
                var buttons = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().ToArray();
                var cancel = buttons.Single(b => b.DialogResult == DialogResult.Cancel);
                var remove = buttons.Single(b => b.DialogResult == DialogResult.OK);
                Assert.That(form.AcceptButton, Is.SameAs(cancel));
                Assert.That(form.CancelButton, Is.SameAs(cancel));
                Assert.That(form.ActiveControl, Is.SameAs(cancel));
                Assert.That(remove.AccessibleName, Is.EqualTo(UiLocalization.Text("TargetManagerForm.Removal.Accessible.Remove", "Remove target")));
                Assert.That(cancel.AccessibleName, Is.Not.Empty);
                Assert.That(form.Controls.OfType<Label>().Single().TabStop, Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemovalDialog_RequiresConfirmationAndKeepsReportedWarningAfterRefresh(bool confirm)
        {
            var target = _catalog.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var calls = 0;
            Action changeSelection = null;
            using (var form = new TargetManagerForm(_catalog, _channel, "MyCodex Test", currentTargetId: "windows", removeAsync: id =>
            {
                calls++;
                _catalog.RemoveLocalData(id);
                changeSelection?.Invoke();
                return Task.FromResult(new TargetInstanceReply { Success = true, Error = "retained archive fixture" });
            }))
            using (var driver = new System.Windows.Forms.Timer { Interval = 20 })
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                var requested = false;
                var answered = false;
                var focused = false;
                string outcome = null;
                var elapsed = Stopwatch.StartNew();
                changeSelection = () =>
                {
                    var targets = FormTestDriver.Field<ListView>(form, "_targets");
                    foreach (ListViewItem item in targets.Items) item.Selected = false;
                    targets.Items.Cast<ListViewItem>().Single(item => ((TargetProfileSnapshot)item.Tag).Id == "windows").Selected = true;
                };
                driver.Tick += (s, e) =>
                {
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) { form.Dispose(); return; }
                    var dialog = System.Windows.Forms.Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Owner == form && f.Modal);
                    if (dialog != null && !answered)
                    {
                        answered = true;
                        var button = dialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>()
                            .Single(b => b.DialogResult == (confirm ? DialogResult.OK : DialogResult.Cancel));
                        button.PerformClick();
                        return;
                    }
                    if (FormTestDriver.Field<bool>(form, "_busy")) return;
                    if (!requested)
                    {
                        var targets = FormTestDriver.Field<ListView>(form, "_targets");
                        var row = targets.Items.Cast<ListViewItem>().Single(item => ((TargetProfileSnapshot)item.Tag).Id == target.Id);
                        foreach (ListViewItem item in targets.Items) item.Selected = false;
                        row.Selected = true;
                        requested = true;
                        FormTestDriver.Field<Button>(form, "_remove").PerformClick();
                        return;
                    }
                    if (!answered) return;
                    outcome = FormTestDriver.Field<Label>(form, "_status").Text;
                    focused = FormTestDriver.Field<ListView>(form, "_targets").Focused;
                    form.Close();
                };
                driver.Start();
                form.ShowDialog();
                driver.Stop();
                Assert.That(answered, Is.True);
                Assert.That(calls, Is.EqualTo(confirm ? 1 : 0));
                Assert.That(focused, Is.True);
                Assert.That(_catalog.Load().Targets.Any(t => t.Id == target.Id), Is.EqualTo(!confirm));
                if (confirm) Assert.That(outcome, Is.EqualTo("retained archive fixture"));
            }
        }

        [Test]
        public void MissingOrUnresponsiveTarget_HasDistinctStatus()
        {
            Assert.That(TargetManagerForm.StatusText(null), Is.EqualTo(UiLocalization.Text("Target.Status.NotRunning", "Not running")));
            Assert.That(TargetManagerForm.StatusText(TargetInstanceReply.Failure("timeout")), Is.EqualTo(UiLocalization.Text("Target.Status.Unavailable", "Unavailable")));
            Assert.That(TargetManagerForm.StatusText(new TargetInstanceReply { Success = true }), Is.EqualTo(UiLocalization.Text("Target.Status.Unavailable", "Unavailable")));
        }

        [TestCase(TargetInstanceLifecycle.Starting, true, true, true, "Target.Status.Starting", "Starting")]
        [TestCase(TargetInstanceLifecycle.Closing, true, true, true, "Target.Status.Closing", "Closing")]
        [TestCase(TargetInstanceLifecycle.Running, true, true, true, "Target.Status.Pending", "Waiting for interaction")]
        [TestCase(TargetInstanceLifecycle.Running, true, false, true, "Target.Status.Working", "Working")]
        [TestCase(TargetInstanceLifecycle.Running, false, false, true, "Target.Status.Running", "Running")]
        [TestCase(TargetInstanceLifecycle.Running, false, false, false, "Target.Status.Failed", "Connection failed")]
        public void StatusText_MapsActualLifecycleWorkInteractionAndConnection(
            TargetInstanceLifecycle lifecycle, bool working, bool pending, bool connected, string key, string fallback)
        {
            var reply = new TargetInstanceReply
            {
                Success = true,
                Status = new TargetInstanceStatus
                {
                    Lifecycle = lifecycle,
                    IsWorking = working,
                    HasPendingInteraction = pending,
                    IsConnected = connected
                }
            };
            Assert.That(TargetManagerForm.StatusText(reply), Is.EqualTo(UiLocalization.Text(key, fallback)));
        }
    }
}
