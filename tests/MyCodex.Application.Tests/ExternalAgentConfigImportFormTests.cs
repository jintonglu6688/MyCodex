using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ExternalAgentConfigImportFormTests
    {
        [Test]
        public async Task DetectAsync_BindsItemsWithoutImporting()
        {
            var client = new FakeExternalAgentConfigClient();
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Subagents,
                RawItemType = "SUBAGENTS",
                Description = "Subagents",
                Cwd = @"E:\Repo",
                Details = new ExternalAgentConfigMigrationDetails
                {
                    Subagents = new List<ExternalAgentConfigNamedMigration>
                    {
                        new ExternalAgentConfigNamedMigration { Name = "reviewer" }
                    }
                },
                RawItem = new Dictionary<string, object> { { "itemType", "SUBAGENTS" }, { "description", "Subagents" } }
            });

            using (var form = new ExternalAgentConfigImportForm(client, @"E:\Repo"))
            {
                await form.LoadMigrationItemsForTestsAsync();

                Assert.That(form.MigrationItemCountForTests, Is.EqualTo(1));
                Assert.That(form.FirstMigrationItemTypeTextForTests, Is.EqualTo("SUBAGENTS"));
                Assert.That(form.FirstMigrationItemSelectedForTests, Is.True);
                Assert.That(client.ImportCalls, Is.EqualTo(0));
                Assert.That(client.LastWorkingDirectories, Is.EqualTo(new[] { @"E:\Repo" }));
                Assert.That(client.LastIncludeHome, Is.True);
            }
        }

        [Test]
        public async Task ImportAsync_SendsOnlyCheckedItems()
        {
            var client = new FakeExternalAgentConfigClient();
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Commands,
                RawItemType = "COMMANDS",
                Description = "Commands",
                RawItem = new Dictionary<string, object> { { "itemType", "COMMANDS" }, { "description", "Commands" } }
            });

            using (var form = new ExternalAgentConfigImportForm(client, @"E:\Repo"))
            {
                await form.LoadMigrationItemsForTestsAsync();
                await form.ImportSelectedItemsForTestsAsync();

                Assert.That(client.ImportCalls, Is.EqualTo(1));
                Assert.That(client.ImportedItems.Single().RawItemType, Is.EqualTo("COMMANDS"));
                Assert.That(form.ImportCompleted, Is.True);
            }
        }

        [Test]
        public async Task ImportAsync_ExcludesBroadConfigItemsBeforeSubmitting()
        {
            var client = new FakeExternalAgentConfigClient();
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Config,
                RawItemType = "CONFIG",
                Description = "Broad configuration",
                RawItem = new Dictionary<string, object> { { "itemType", "CONFIG" } }
            });
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Commands,
                RawItemType = "COMMANDS",
                Description = "Commands",
                RawItem = new Dictionary<string, object> { { "itemType", "COMMANDS" } }
            });

            using (var form = new ExternalAgentConfigImportForm(client, @"E:\Repo"))
            {
                await form.LoadMigrationItemsForTestsAsync();
                await form.ImportSelectedItemsForTestsAsync();

                Assert.That(client.ImportCalls, Is.EqualTo(1));
                Assert.That(client.ImportedItems.Select(item => item.RawItemType), Is.EqualTo(new[] { "COMMANDS" }));
            }
        }

        [Test]
        public async Task DetectAsync_KeepsUnknownItemsVisibleButDoesNotImportThem()
        {
            var client = new FakeExternalAgentConfigClient();
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Config,
                RawItemType = "CONFIG",
                RawItem = new Dictionary<string, object> { { "itemType", "CONFIG" } }
            });
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Unknown,
                RawItemType = "FUTURE_ITEM",
                RawItem = new Dictionary<string, object> { { "itemType", "FUTURE_ITEM" } }
            });
            client.DetectResult.Items.Add(new ExternalAgentConfigMigrationItem
            {
                ItemType = ExternalAgentConfigMigrationItemType.Skills,
                RawItemType = "SKILLS",
                RawItem = new Dictionary<string, object> { { "itemType", "SKILLS" } }
            });

            using (var form = new ExternalAgentConfigImportForm(client, @"E:\Repo"))
            {
                await form.LoadMigrationItemsForTestsAsync();
                await form.ImportSelectedItemsForTestsAsync();

                Assert.That(form.MigrationItemCountForTests, Is.EqualTo(2));
                Assert.That(form.FirstMigrationItemTypeTextForTests, Is.EqualTo("FUTURE_ITEM"));
                Assert.That(client.ImportedItems.Select(item => item.RawItemType), Is.EqualTo(new[] { "SKILLS" }));
            }
        }

        [Test]
        public void Constructor_ExposesNamedKeyboardControls()
        {
            using (var form = new ExternalAgentConfigImportForm(new FakeExternalAgentConfigClient(), @"E:\Repo"))
            {
                var controls = FindControls(form).Where(control => control.TabStop).ToList();

                Assert.That(controls, Has.All.Matches<Control>(control => !string.IsNullOrWhiteSpace(control.AccessibleName)));
                Assert.That(controls.Select(control => control.TabIndex), Is.Unique);
                Assert.That(FindControls(form).OfType<Label>(), Has.All.Matches<Label>(label => !label.TabStop));
            }
        }

        private sealed class FakeExternalAgentConfigClient : IAppServerExternalAgentConfigClient
        {
            public FakeExternalAgentConfigClient()
            {
                DetectResult = new ExternalAgentConfigDetectResult();
                ImportedItems = new List<ExternalAgentConfigMigrationItem>();
            }

            public ExternalAgentConfigDetectResult DetectResult { get; private set; }

            public List<ExternalAgentConfigMigrationItem> ImportedItems { get; private set; }

            public int ImportCalls { get; private set; }

            public IReadOnlyCollection<string> LastWorkingDirectories { get; private set; }

            public bool LastIncludeHome { get; private set; }

            public Task<ExternalAgentConfigDetectResult> DetectExternalAgentConfigAsync(
                IReadOnlyCollection<string> workingDirectories,
                bool includeHome,
                CancellationToken cancellationToken)
            {
                LastWorkingDirectories = workingDirectories;
                LastIncludeHome = includeHome;
                return Task.FromResult(DetectResult);
            }

            public Task ImportExternalAgentConfigAsync(
                IReadOnlyCollection<ExternalAgentConfigMigrationItem> migrationItems,
                CancellationToken cancellationToken)
            {
                ImportCalls++;
                ImportedItems.AddRange(migrationItems);
                return Task.CompletedTask;
            }
        }

        private static List<Control> FindControls(Control root)
        {
            var result = new List<Control>();
            foreach (Control control in root.Controls)
            {
                result.Add(control);
                result.AddRange(FindControls(control));
            }

            return result;
        }
    }
}
