using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class PendingAutomationAssignmentTests
    {
        private string _root;
        private AppConfigService _legacy;
        private TargetCatalogStore _catalog;
        private TargetProfileSnapshot _linux;
        private TargetProfileSnapshot _ssh;
        private string PendingPath => Path.Combine(_root, "targets", "pending-automations.json");
        private string AssignmentsPath => Path.Combine(_root, "targets", "pending-automation-assignments.json");

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pending-assignment-" + Guid.NewGuid().ToString("N"));
            _legacy = new AppConfigService(_root);
            new JsonAutomationStore(_legacy).Save(new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    PendingDefinition("coding", AppInteractionMode.Coding),
                    PendingDefinition("office", AppInteractionMode.Office)
                },
                Runs = new List<AutomationRunSnapshot>
                {
                    new AutomationRunSnapshot
                    {
                        Id = "old-run", AutomationId = "coding", ThreadId = "old-thread", TurnId = "old-turn",
                        WorkspaceId = "old-workspace", WorkspacePath = "/old/project", Summary = "Retained findings",
                        Status = AutomationRunStatus.Completed, CompletedAt = new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                    }
                }
            });
            new TargetDataMigration(_legacy).Run();
            _catalog = new TargetCatalogStore(_legacy);
            _linux = _catalog.AddWsl("Research Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            _ssh = _catalog.AddSsh("Build server", new RemoteMacSshConfigSnapshot
            {
                HostName = "build.example", UserName = "dev", AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\keys\id.ppk"
            });
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public void SelectingTaskTargetAndDirectoryThenCancelling_DoesNotPersistOrDefaultToWindows()
        {
            var before = SnapshotFiles();
            using (var form = new PendingAutomationAssignmentForm(_catalog))
            {
                Show(form);
                var tasks = Get<ListView>(form, "_tasks");
                var target = Get<ComboBox>(form, "_target");
                var directory = Get<TextBox>(form, "_workspace");
                var save = Get<Button>(form, "_save");
                Assert.That(form.ActiveControl, Is.SameAs(tasks));
                Assert.That(target.SelectedIndex, Is.EqualTo(-1));
                Assert.That(save.Enabled, Is.False);
                Assert.That(form.AcceptButton, Is.Null);
                SelectTask(tasks, "coding");
                Assert.That(target.SelectedIndex, Is.EqualTo(-1), "Task selection must require an explicit destination.");
                Assert.That(save.Enabled, Is.False);
                target.SelectedIndex = TargetIndex(_linux.Id);
                directory.Text = "/srv/chosen-project";
                Assert.That(save.Enabled, Is.True);
                Assert.That(tasks.View, Is.EqualTo(View.Details));
                foreach (var control in new Control[] { tasks, target, directory, save, (Button)form.CancelButton })
                    Assert.That(control.AccessibleName, Is.Not.Empty);
                ((Button)form.CancelButton).PerformClick();
                form.Close();
            }
            AssertFilesUnchanged(before);
            Assert.That(_catalog.ListPendingAutomations().Count, Is.EqualTo(2));
        }

        [Test]
        public void SaveButton_AssignsOnlyExplicitlySelectedTargetAndLeavesTaskDisabled()
        {
            var pendingBefore = File.ReadAllBytes(PendingPath);
            var windowsBefore = File.ReadAllBytes(_catalog.GetPaths("windows").GetAutomationsFilePath());
            using (var form = new PendingAutomationAssignmentForm(_catalog))
            {
                Show(form);
                SelectTask(Get<ListView>(form, "_tasks"), "coding");
                Get<ComboBox>(form, "_target").SelectedIndex = TargetIndex(_linux.Id);
                Get<TextBox>(form, "_workspace").Text = "/srv/ui-project";
                Get<Button>(form, "_save").PerformClick();
                Assert.That(Get<ListView>(form, "_tasks").Items.Count, Is.EqualTo(1));
                var saved = new JsonAutomationStore(_catalog.GetPaths(_linux.Id)).Load().Definitions.Single();
                Assert.That(saved.Enabled, Is.False);
                Assert.That(saved.Context.RuntimeProfileId, Is.EqualTo(_linux.RuntimeScopeId));
                Assert.That(saved.Context.WorkspacePath, Is.EqualTo("/srv/ui-project"));
            }
            Assert.That(File.ReadAllBytes(PendingPath), Is.EqualTo(pendingBefore));
            Assert.That(File.ReadAllBytes(_catalog.GetPaths("windows").GetAutomationsFilePath()), Is.EqualTo(windowsBefore));
        }

        [TestCase("Windows", AppInteractionMode.Coding, @"C:\assigned\project")]
        [TestCase("Windows", AppInteractionMode.Office, @"C:\assigned\documents")]
        [TestCase("Wsl", AppInteractionMode.Coding, "/srv/assigned/project")]
        [TestCase("Wsl", AppInteractionMode.Office, "/srv/assigned/documents")]
        [TestCase("Ssh", AppInteractionMode.Coding, "/home/dev/project")]
        [TestCase("Ssh", AppInteractionMode.Office, "/home/dev/documents")]
        public void Assignment_ScopesNewCopyClearsInheritedIdentityAndRetainsOriginalHistoryAndDestinationTasks(
            string kind, AppInteractionMode mode, string workspace)
        {
            var target = Target(kind);
            var store = new JsonAutomationStore(_catalog.GetPaths(target.Id));
            var existing = PendingDefinition("existing", AppInteractionMode.Coding);
            existing.Context.RuntimeProfileId = target.RuntimeScopeId;
            existing.Context.WorkspacePath = kind == "Windows" ? @"C:\existing" : "/existing";
            var existingRun = new AutomationRunSnapshot { Id = "existing-run", AutomationId = existing.Id, Summary = "Keep destination history" };
            store.Save(new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot> { existing },
                Runs = new List<AutomationRunSnapshot> { existingRun }
            });
            var existingBytes = Serialize(store.Load().Definitions.Single());
            var existingRunBytes = Serialize(store.Load().Runs.Single());
            var pendingBefore = File.ReadAllBytes(PendingPath);
            var pendingId = mode == AppInteractionMode.Office ? "office" : "coding";

            var result = _catalog.AssignPendingAutomation(pendingId, target.Id, "  " + workspace + "  ");

            var destination = store.Load();
            Assert.That(destination.Definitions.Count, Is.EqualTo(2));
            var assigned = destination.Definitions.Single(item => item.Id == result.Id);
            Assert.That(assigned.Id, Is.Not.EqualTo(pendingId));
            Assert.That(assigned.Enabled, Is.False);
            Assert.That(assigned.NextRunAt, Is.Null);
            Assert.That(assigned.Context.RuntimeProfileId, Is.EqualTo(target.RuntimeScopeId));
            Assert.That(assigned.Context.OfficeRuntimeScopeId, Is.EqualTo(mode == AppInteractionMode.Office ? target.RuntimeScopeId : null));
            Assert.That(assigned.Context.WorkspacePath, Is.EqualTo(workspace));
            Assert.That(assigned.Context.OfficeRootPath, Is.EqualTo(mode == AppInteractionMode.Office ? workspace : null));
            Assert.That(assigned.Context.WorkspaceId, Is.Null);
            Assert.That(assigned.Context.SessionId, Is.Null);
            Assert.That(assigned.Context.ThreadId, Is.Null);
            Assert.That(Serialize(destination.Definitions.Single(item => item.Id == existing.Id)), Is.EqualTo(existingBytes));
            Assert.That(Serialize(destination.Runs.Single()), Is.EqualTo(existingRunBytes));
            Assert.That(File.ReadAllBytes(PendingPath), Is.EqualTo(pendingBefore));
            Assert.That(_catalog.ListPendingAutomations().Select(item => item.Definition.Id), Does.Not.Contain(pendingId));
        }

        [Test]
        public void FailureAfterCopy_ReservesDestinationAndRetryKeepsSavedEditsWithoutAddingAnotherCopy()
        {
            var pendingBefore = File.ReadAllBytes(PendingPath);
            var interrupted = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", checkpoint =>
            {
                if (checkpoint == "automation-assigned") throw new IOException("Injected failure after copying.");
            });
            Assert.Throws<IOException>(() => interrupted.AssignPendingAutomation("coding", _linux.Id, "/srv/first"));
            var pending = _catalog.ListPendingAutomations().Single(item => item.Definition.Id == "coding");
            Assert.That(pending.ReservedTargetId, Is.EqualTo(_linux.Id));
            var destination = new JsonAutomationStore(_catalog.GetPaths(_linux.Id));
            var copy = destination.Load().Definitions.Single();
            Assert.That(copy.Enabled, Is.False);
            var copiedId = copy.Id;
            destination.Mutate(snapshot =>
            {
                var userEdit = snapshot.Definitions.Single();
                userEdit.Name = "User renamed the copied task";
                userEdit.Prompt = "Keep the user's new prompt";
                userEdit.Context.WorkspacePath = "/srv/user-edited";
                userEdit.Enabled = true;
                return snapshot;
            });
            var savedEdit = Serialize(destination.Load().Definitions.Single());
            var windowsBefore = File.ReadAllBytes(_catalog.GetPaths("windows").GetAutomationsFilePath());
            Assert.Throws<InvalidOperationException>(() => _catalog.AssignPendingAutomation("coding", "windows", @"C:\other"));
            Assert.That(File.ReadAllBytes(_catalog.GetPaths("windows").GetAutomationsFilePath()), Is.EqualTo(windowsBefore));

            var retried = _catalog.AssignPendingAutomation("coding", _linux.Id, "/srv/retry");
            var repeated = _catalog.AssignPendingAutomation("coding", _linux.Id, "/srv/repeated");

            Assert.That(retried.Id, Is.EqualTo(copiedId));
            Assert.That(repeated.Id, Is.EqualTo(copiedId));
            Assert.That(destination.Load().Definitions.Count, Is.EqualTo(1));
            Assert.That(Serialize(destination.Load().Definitions.Single()), Is.EqualTo(savedEdit));
            Assert.That(_catalog.ListPendingAutomations().Select(item => item.Definition.Id), Does.Not.Contain("coding"));
            Assert.That(File.ReadAllBytes(PendingPath), Is.EqualTo(pendingBefore));
        }

        [TestCase("Windows", "/home/dev/wrong-host")]
        [TestCase("Wsl", @"C:\wrong-host")]
        [TestCase("Ssh", @"C:\wrong-host")]
        public void IncompatiblePath_IsRejectedBeforeReservationOrAnyDestinationWrite(string kind, string path)
        {
            var before = SnapshotFiles();
            Assert.Throws<InvalidOperationException>(() => _catalog.AssignPendingAutomation("coding", Target(kind).Id, path));
            Assert.That(File.Exists(AssignmentsPath), Is.False);
            Assert.That(_catalog.ListPendingAutomations().Single(item => item.Definition.Id == "coding").ReservedTargetId, Is.Null);
            AssertFilesUnchanged(before);
        }

        [Test]
        public void InterruptedAssignmentForm_RestoresItsReservedTargetAndRetainsEditableDirectoryAfterFailure()
        {
            var interrupted = new TargetCatalogStore(_root, _legacy.GetInstallationDirectory(), "MyCodex", checkpoint =>
            {
                if (checkpoint == "automation-assigned") throw new IOException("Injected retry failure.");
            });
            Assert.Throws<IOException>(() => interrupted.AssignPendingAutomation("coding", _linux.Id, "/srv/first"));
            using (var form = new PendingAutomationAssignmentForm(interrupted))
            {
                Show(form);
                var tasks = Get<ListView>(form, "_tasks");
                SelectTask(tasks, "coding");
                var target = Get<ComboBox>(form, "_target");
                Assert.That(target.SelectedIndex, Is.EqualTo(TargetIndex(_linux.Id)));
                Assert.That(target.Enabled, Is.False);
                Get<TextBox>(form, "_workspace").Text = "/srv/editable-retry";
                Get<Button>(form, "_save").PerformClick();
                Assert.That(target.SelectedIndex, Is.EqualTo(TargetIndex(_linux.Id)));
                Assert.That(target.Enabled, Is.False);
                Assert.That(Get<TextBox>(form, "_workspace").Text, Is.EqualTo("/srv/editable-retry"));
                Assert.That(Get<Label>(form, "_status").Text, Does.Contain("Injected retry failure."));
                Assert.That(tasks.SelectedItems.Count, Is.EqualTo(1));
            }
        }

        private TargetProfileSnapshot Target(string kind)
        {
            return kind == "Windows" ? _catalog.GetTarget("windows") : kind == "Wsl" ? _linux : _ssh;
        }

        private int TargetIndex(string id)
        {
            return _catalog.Load().Targets.FindIndex(target => target.Id == id);
        }

        private Dictionary<string, byte[]> SnapshotFiles()
        {
            return Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        }

        private void AssertFilesUnchanged(Dictionary<string, byte[]> before)
        {
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(before.Keys));
            foreach (var file in before) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
        }

        private static AutomationDefinitionSnapshot PendingDefinition(string id, AppInteractionMode mode)
        {
            return new AutomationDefinitionSnapshot
            {
                Id = id, Name = "Old " + id, Enabled = true, Prompt = "Original prompt", NextRunAt = DateTime.UtcNow,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Context = new AutomationContextSnapshot
                {
                    InteractionMode = mode, RuntimeProfileId = "unknown-old-runtime", OfficeRuntimeScopeId = "unknown-old-office",
                    WorkspaceId = "old-workspace", SessionId = "old-session", ThreadId = "old-thread",
                    WorkspacePath = "/old/project", OfficeRootPath = "/old/documents"
                }
            };
        }

        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static void Show(Form form)
        {
            form.ShowInTaskbar = false;
            form.Show();
            System.Windows.Forms.Application.DoEvents();
        }

        private static void SelectTask(ListView tasks, string id)
        {
            foreach (ListViewItem row in tasks.Items)
                row.Selected = ((PendingAutomationAssignment)row.Tag).Definition.Id == id;
            Assert.That(tasks.SelectedItems.Count, Is.EqualTo(1));
        }

        private static T Get<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }
    }
}
