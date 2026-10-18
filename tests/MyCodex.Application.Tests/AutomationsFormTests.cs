using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AutomationsFormTests
    {
        [Test]
        public void AutomationsForm_UsesAutomationAndRunResultsTabs()
        {
            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations"))
            {
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(760));
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                Assert.That(tabs.TabPages.Cast<TabPage>().Select(item => item.Text).ToArray(), Is.EquivalentTo(new[]
                {
                    UiLocalization.Text("AutomationsForm.Tab.Automations", "Automations"),
                    UiLocalization.Text("AutomationsForm.Tab.RunHistory", "Run results")
                }));
                Assert.That(string.IsNullOrEmpty(tabs.AccessibleName), Is.True);
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, "Pending"), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results")), Is.Not.Null);
            }
        }

        [Test]
        public void AutomationsForm_AcceptsAutomationInputSelectionCallbacks()
        {
            var callbacks = new AutomationInputSelectionCallbacks
            {
                SelectSkillAsync = owner => Task.FromResult(new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Skill,
                    Name = "news-digest",
                    DisplayName = "News Digest",
                    Path = @"E:\skills\news-digest"
                }),
                SelectPluginAsync = owner => Task.FromResult(new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Plugin,
                    Name = "mycodex-office",
                    DisplayName = "MyCodex Office",
                    Path = @"E:\plugins\mycodex-office"
                })
            };

            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations", callbacks))
            {
                Assert.That(form, Is.Not.Null);
            }
        }

        [Test]
        public void AutomationsForm_AcceptsAutomationModelSelectionCallbacks()
        {
            var callbacks = new AutomationModelSelectionCallbacks
            {
                LoadModelsAsync = () =>
                {
                    IReadOnlyList<AppServerModelOption> models = new[]
                    {
                        new AppServerModelOption
                        {
                            ModelId = "gpt-5.5",
                            DisplayName = "gpt-5.5",
                            DefaultReasoningEffort = "high"
                        }
                    };
                    return Task.FromResult(models);
                },
                SupportsReasoningEffortForModel = modelId => true
            };

            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations", null, null, null, callbacks))
            {
                Assert.That(form, Is.Not.Null);
            }
        }

        [Test]
        public void AutomationsForm_RunNowStartsSelectedAutomationAndShowsRunHistory()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "苹果新闻采集",
                            Kind = AutomationKind.Standalone,
                            Enabled = true,
                            Context = new AutomationContextSnapshot
                            {
                                WorkspacePath = @"E:\MyCodex-Automations"
                            }
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>()
                }
            };
            var runner = new RecordingAutomationRunner(store);

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations", null, runner))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var automationsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations"));
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                var runNowButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.RunNow", "Run now"));

                tabs.SelectedTab = tabs.TabPages[0];
                System.Windows.Forms.Application.DoEvents();
                automationsListView.Focus();
                automationsListView.Items[0].Selected = true;
                automationsListView.Items[0].Focused = true;
                runNowButton.PerformClick();

                Assert.That(runner.Request, Is.Not.Null);
                Assert.That(runner.Request.AutomationId, Is.EqualTo("automation-1"));
                Assert.That(runner.Request.TriggerReason, Is.EqualTo(AutomationTriggerReason.Manual));
                Assert.That(runner.Request.ScheduledFor, Is.Not.Null);
                Assert.That(tabs.SelectedTab, Is.EqualTo(tabs.TabPages[1]));
                Assert.That(runsListView.Items.Count, Is.EqualTo(1));
                Assert.That(runsListView.Items[0].SubItems[0].Text, Is.EqualTo("苹果新闻采集"));
                Assert.That(runsListView.Items[0].SubItems[1].Text, Is.EqualTo(UiLocalization.Text("Automation.RunStatus.Running", "Running")));
                Assert.That(runsListView.Items[0].SubItems[2].Text, Is.EqualTo("2026-06-06 08:00"));
            }
        }

        [Test]
        public void AutomationsForm_RefreshAutomationStateReloadsRunStatus()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "苹果新闻采集",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Running,
                            StartedAt = new DateTime(2026, 6, 6, 11, 8, 0)
                        }
                    }
                }
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations"))
            {
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                Assert.That(runsListView.Items[0].SubItems[1].Text, Is.EqualTo(UiLocalization.Text("Automation.RunStatus.Running", "Running")));

                store.Snapshot.Runs[0].Status = AutomationRunStatus.Completed;
                store.Snapshot.Runs[0].CompletedAt = new DateTime(2026, 6, 6, 11, 12, 0);

                form.RefreshAutomationState();

                Assert.That(runsListView.Items[0].SubItems[1].Text, Is.EqualTo(UiLocalization.Text("Automation.RunStatus.Completed", "Completed")));
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void AutomationsForm_PassesActivityCallbacksToRunDetailsForm()
        {
            var source = RepositorySource.ReadSourceFile("AutomationsForm.cs");

            Assert.That(source, Does.Contain("private readonly AutomationRunActivityCallbacks _runActivityCallbacks;"));
            Assert.That(source, Does.Contain("AutomationRunActivityCallbacks runActivityCallbacks = null"));
            Assert.That(source, Does.Contain("_runActivityCallbacks = runActivityCallbacks;"));
            Assert.That(source, Does.Contain("new AutomationRunDetailsForm("));
            Assert.That(source, Does.Contain("_runActivityCallbacks"));
        }

        [Test]
        public void ShowRunResults_SelectsRunResultsTabAndRefreshesState()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Completed,
                            StartedAt = new DateTime(2026, 6, 14, 8, 0, 0),
                            NeedsAttention = true,
                            Archived = false,
                            Summary = "Found changes"
                        }
                    }
                }
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations"))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                tabs.SelectedIndex = 0;

                form.ShowRunResults();

                Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results")).Items.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void AutomationsForm_F5RefreshesAutomationState()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>()
                }
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations"))
            {
                form.Show();
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                Assert.That(runsListView.Items.Count, Is.EqualTo(0));

                store.Snapshot.Runs.Add(new AutomationRunSnapshot
                {
                    Id = "run-1",
                    AutomationId = "automation-1",
                    Status = AutomationRunStatus.Completed,
                    StartedAt = new DateTime(2026, 6, 6, 12, 0, 0)
                });

                var handled = FormTestDriver.ProcessCmdKey(form, Keys.F5);

                Assert.That(handled, Is.True);
                Assert.That(runsListView.Items.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void AutomationsForm_ShowsContextualActionButtonsForSelectedTab()
        {
            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations"))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var newButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.New", "New"));
                var runNowButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.RunNow", "Run now"));
                var stopButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.StopRun", "Stop run"));
                var detailsButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.Details", "Details"));
                var openThreadButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.OpenThread", "Open thread"));
                var refreshButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("Common.Accessible.Refresh", "Refresh"));

                tabs.SelectedTab = tabs.TabPages[0];
                System.Windows.Forms.Application.DoEvents();
                Assert.That(newButton.Visible, Is.True);
                Assert.That(runNowButton.Visible, Is.True);
                Assert.That(stopButton.Visible, Is.False);
                Assert.That(detailsButton.Visible, Is.False);
                Assert.That(openThreadButton.Visible, Is.False);
                Assert.That(refreshButton, Is.Null);

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                Assert.That(newButton.Visible, Is.False);
                Assert.That(runNowButton.Visible, Is.False);
                Assert.That(stopButton.Visible, Is.True);
                Assert.That(detailsButton.Visible, Is.True);
                Assert.That(openThreadButton.Visible, Is.True);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Mark read"), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Archive result"), Is.Null);
            }
        }

        [Test]
        public void AutomationsForm_PutsEditEnableAndDeleteActionsInListContextMenus()
        {
            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations"))
            {
                var automationsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations"));
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));

                Assert.That(automationsListView.ContextMenu, Is.Not.Null);
                Assert.That(FindMenuItem(automationsListView.ContextMenu.MenuItems, UiLocalization.Text("AutomationsForm.Menu.EditAutomation", "Edit(&E)...")), Is.Not.Null);
                Assert.That(FindMenuItem(automationsListView.ContextMenu.MenuItems, UiLocalization.Text("AutomationsForm.Menu.DisableAutomation", "Disable(&B)")), Is.Not.Null);
                Assert.That(FindMenuItem(automationsListView.ContextMenu.MenuItems, UiLocalization.Text("AutomationsForm.Menu.DeleteAutomation", "Delete(&D)")), Is.Not.Null);
                Assert.That(runsListView.ContextMenu, Is.Not.Null);
                Assert.That(FindMenuItem(runsListView.ContextMenu.MenuItems, UiLocalization.Text("AutomationsForm.Menu.DeleteRun", "Delete result(&D)")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Menu.EditAutomation", "Edit(&E)...")), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Menu.DisableAutomation", "Disable(&B)")), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Menu.EnableAutomation", "Enable(&N)")), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Menu.DeleteAutomation", "Delete(&D)")), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Menu.DeleteRun", "Delete result(&D)")), Is.Null);
            }
        }

        [Test]
        public void AutomationsForm_EnableMenuItemTogglesSelectedAutomation()
        {
            var now = new DateTime(2026, 6, 6, 8, 0, 0);
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Kind = AutomationKind.Standalone,
                            Enabled = true,
                            Schedule = new AutomationScheduleSnapshot
                            {
                                Kind = AutomationScheduleKind.EveryMinutes,
                                Interval = 10
                            },
                            Context = new AutomationContextSnapshot
                            {
                                WorkspacePath = @"E:\MyCodex-Automations"
                            },
                            NextRunAt = now.AddMinutes(10)
                        }
                    }
                }
            };
            var service = new AutomationService(store, new AutomationScheduleCalculator(), () => now, () => "unused");

            using (var form = new AutomationsForm(store, service, @"E:\MyCodex-Automations"))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var automationsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                automationsListView.Focus();
                automationsListView.Items[0].Selected = true;
                automationsListView.Items[0].Focused = true;
                var disableMenuItem = FindMenuItem(
                    automationsListView.ContextMenu.MenuItems,
                    UiLocalization.Text("AutomationsForm.Menu.DisableAutomation", "Disable(&B)"));

                disableMenuItem.PerformClick();

                Assert.That(store.Snapshot.Definitions[0].Enabled, Is.False);
                Assert.That(store.Snapshot.Definitions[0].NextRunAt, Is.Null);
                Assert.That(automationsListView.Items[0].SubItems[2].Text, Is.EqualTo(UiLocalization.Text("AutomationsForm.Value.Disabled", "Disabled")));

                automationsListView.Items[0].Selected = true;
                automationsListView.Items[0].Focused = true;
                var enableMenuItem = FindMenuItem(
                    automationsListView.ContextMenu.MenuItems,
                    UiLocalization.Text("AutomationsForm.Menu.EnableAutomation", "Enable(&N)"));

                enableMenuItem.PerformClick();

                Assert.That(store.Snapshot.Definitions[0].Enabled, Is.True);
                Assert.That(store.Snapshot.Definitions[0].NextRunAt, Is.EqualTo(now.AddMinutes(10)));
                Assert.That(automationsListView.Items[0].SubItems[2].Text, Is.EqualTo(UiLocalization.Text("AutomationsForm.Value.Enabled", "Enabled")));
            }
        }

        [Test]
        public void AutomationsForm_DeleteAutomationRemovesDefinitionAndRuns()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Enabled = true
                        },
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-2",
                            Name = "Code review",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot { Id = "run-1", AutomationId = "automation-1" },
                        new AutomationRunSnapshot { Id = "run-2", AutomationId = "automation-2" }
                    }
                }
            };
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            using (var form = new AutomationsForm(store, service, @"E:\MyCodex-Automations"))
            {
                form.ConfirmDeleteAutomation = definition => true;
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var automationsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "Automations"));
                var deleteMenuItem = FindMenuItem(
                    automationsListView.ContextMenu.MenuItems,
                    UiLocalization.Text("AutomationsForm.Menu.DeleteAutomation", "Delete(&D)"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                automationsListView.Focus();
                automationsListView.Items[0].Selected = true;
                automationsListView.Items[0].Focused = true;
                deleteMenuItem.PerformClick();

                Assert.That(store.Snapshot.Definitions.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "automation-2" }));
                Assert.That(store.Snapshot.Runs.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "run-2" }));
                Assert.That(automationsListView.Items.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void AutomationsForm_DeleteRunRemovesSelectedRunFromLocalRecords()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Failed,
                            NeedsAttention = true,
                            StartedAt = new DateTime(2026, 6, 6, 10, 0, 0),
                            Summary = "Needs review"
                        },
                        new AutomationRunSnapshot
                        {
                            Id = "run-2",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Completed,
                            StartedAt = new DateTime(2026, 6, 6, 9, 0, 0)
                        }
                    }
                }
            };
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            using (var form = new AutomationsForm(store, service, @"E:\MyCodex-Automations"))
            {
                form.ConfirmDeleteRun = run => true;
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                var deleteMenuItem = FindMenuItem(
                    runsListView.ContextMenu.MenuItems,
                    UiLocalization.Text("AutomationsForm.Menu.DeleteRun", "Delete result(&D)"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;
                deleteMenuItem.PerformClick();

                Assert.That(store.Snapshot.Runs.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "run-2" }));

                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;
                deleteMenuItem.PerformClick();

                Assert.That(store.Snapshot.Runs, Is.Empty);
                Assert.That(runsListView.Items.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void AutomationsForm_DeleteRunWithLocalHistoryInvokesRunDeletionCallback()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            ThreadId = "thread-1",
                            Status = AutomationRunStatus.Completed,
                            StartedAt = new DateTime(2026, 6, 6, 9, 0, 0)
                        }
                    }
                }
            };
            var service = new AutomationService(store, new AutomationScheduleCalculator());
            AutomationRunSnapshot deletedRun = null;
            var callbacks = new AutomationRunActionCallbacks
            {
                DeleteLocalHistoryAsync = run =>
                {
                    deletedRun = run;
                    return Task.CompletedTask;
                }
            };

            using (var form = new AutomationsForm(
                store,
                service,
                @"E:\MyCodex-Automations",
                null,
                null,
                callbacks))
            {
                form.ConfirmDeleteRunOptions = run => new AutomationRunDeleteConfirmation(true, true);
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                var deleteMenuItem = FindMenuItem(
                    runsListView.ContextMenu.MenuItems,
                    UiLocalization.Text("AutomationsForm.Menu.DeleteRun", "Delete result(&D)"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;
                deleteMenuItem.PerformClick();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(deletedRun, Is.Not.Null);
                Assert.That(deletedRun.Id, Is.EqualTo("run-1"));
                Assert.That(deletedRun.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(store.Snapshot.Runs, Is.Empty);
                Assert.That(runsListView.Items.Count, Is.EqualTo(0));
            }
        }

        [Test]
        public void AutomationsForm_StopRunInvokesCallbackForSelectedRunningRun()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "苹果新闻采集",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Running,
                            ThreadId = "thread-1",
                            TurnId = "turn-1",
                            StartedAt = new DateTime(2026, 6, 6, 11, 8, 0)
                        }
                    }
                }
            };
            AutomationRunSnapshot stoppedRun = null;
            var callbacks = new AutomationRunActionCallbacks
            {
                CanStopRun = run => run != null && run.Status == AutomationRunStatus.Running,
                StopRunAsync = run =>
                {
                    stoppedRun = run;
                    return Task.CompletedTask;
                }
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations", null, null, callbacks))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                var stopButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.StopRun", "Stop run"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;
                Assert.That(stopButton.Enabled, Is.True);

                stopButton.PerformClick();

                Assert.That(stoppedRun, Is.Not.Null);
                Assert.That(stoppedRun.Id, Is.EqualTo("run-1"));
            }
        }

        [Test]
        public void AutomationsForm_StopRunDisabledForCompletedRun()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "苹果新闻采集",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Completed,
                            ThreadId = "thread-1",
                            TurnId = "turn-1",
                            StartedAt = new DateTime(2026, 6, 6, 11, 8, 0)
                        }
                    }
                }
            };
            var callbacks = new AutomationRunActionCallbacks
            {
                CanStopRun = run => run != null && run.Status == AutomationRunStatus.Running,
                StopRunAsync = run => Task.CompletedTask
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations", null, null, callbacks))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));
                var stopButton = FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationsForm.Accessible.StopRun", "Stop run"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;

                Assert.That(stopButton.Enabled, Is.False);
            }
        }

        [Test]
        public void AutomationsForm_RunHistoryEnterOpensRunDetails()
        {
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Name = "Apple news",
                            Enabled = true
                        }
                    },
                    Runs = new List<AutomationRunSnapshot>
                    {
                        new AutomationRunSnapshot
                        {
                            Id = "run-1",
                            AutomationId = "automation-1",
                            Status = AutomationRunStatus.Completed,
                            Summary = "Collected Apple news.",
                            CompletedAt = new DateTime(2026, 6, 6, 8, 0, 0)
                        }
                    }
                }
            };

            using (var form = new AutomationsForm(store, null, @"E:\MyCodex-Automations"))
            {
                form.Show();
                var tabs = FindControlByName<TabControl>(form, "automationsTabs");
                var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "Run results"));

                tabs.SelectedTab = tabs.TabPages[1];
                System.Windows.Forms.Application.DoEvents();
                runsListView.Focus();
                runsListView.Items[0].Selected = true;
                runsListView.Items[0].Focused = true;

                InvokeRunListKeyDown(form, runsListView, Keys.Enter);

                Assert.That(form.OwnedForms.OfType<AutomationRunDetailsForm>().Count(), Is.EqualTo(1));
            }
        }

        [Test]
        public void AutomationsForm_EscapeClosesForm()
        {
            using (var form = new AutomationsForm(new InMemoryAutomationStore(), null, @"E:\MyCodex-Automations"))
            {
                form.Show();

                var handled = FormTestDriver.ProcessCmdKey(form, Keys.Escape);

                Assert.That(handled, Is.True);
                Assert.That(form.IsDisposed, Is.True);
            }
        }

        [Test]
        public void AutomationsForm_LocalizesAutomationKindAndRunStatusInChinese()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                using (var form = new AutomationsForm(new InMemoryAutomationStore
                {
                    Snapshot = new AutomationStateSnapshot
                    {
                        Definitions = new List<AutomationDefinitionSnapshot>
                        {
                            new AutomationDefinitionSnapshot
                            {
                                Id = "automation-1",
                                Name = "每日审阅",
                                Kind = AutomationKind.Thread,
                                Enabled = true
                            }
                        },
                        Runs = new List<AutomationRunSnapshot>
                        {
                            new AutomationRunSnapshot
                            {
                                Id = "run-1",
                                AutomationId = "automation-1",
                                Status = AutomationRunStatus.Failed,
                                NeedsAttention = true,
                                Summary = "运行失败"
                            }
                        }
                    }
                }, null, @"E:\MyCodex-Automations"))
                {
                    var automationsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.Automations", "自动化"));
                    var runsListView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationsForm.Accessible.RunHistory", "运行结果"));

                    Assert.That(automationsListView.Items[0].SubItems[1].Text, Is.EqualTo(UiLocalization.Text("Automation.Kind.Thread", "会话线程")));
                    Assert.That(runsListView.Items[0].SubItems[1].Text, Is.EqualTo(UiLocalization.Text("Automation.RunStatus.Failed", "失败")));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        private static T FindControlByName<T>(Control root, string name) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && child.Name == name)
                {
                    return typed;
                }

                var nested = FindControlByName<T>(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static MenuItem FindMenuItem(Menu.MenuItemCollection items, string text)
        {
            foreach (MenuItem item in items)
            {
                if (item.Text == text)
                {
                    return item;
                }
            }

            return null;
        }

        private static void InvokeRunListKeyDown(AutomationsForm form, ListView listView, Keys keyCode)
        {
            typeof(AutomationsForm)
                .GetMethod("RunListView_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { listView, new KeyEventArgs(keyCode) });
            System.Windows.Forms.Application.DoEvents();
        }

        private sealed class RecordingAutomationRunner : IAutomationRunner
        {
            private readonly InMemoryAutomationStore _store;

            public RecordingAutomationRunner(InMemoryAutomationStore store)
            {
                _store = store;
            }

            public AutomationRunRequest Request { get; private set; }

            public Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request)
            {
                Request = request;
                var snapshot = _store.Load() ?? new AutomationStateSnapshot();
                snapshot.Runs = snapshot.Runs ?? new List<AutomationRunSnapshot>();
                var run = new AutomationRunSnapshot
                {
                    Id = "run-1",
                    AutomationId = request.AutomationId,
                    Status = AutomationRunStatus.Running,
                    TriggerReason = request.TriggerReason,
                    ScheduledFor = request.ScheduledFor,
                    StartedAt = new DateTime(2026, 6, 6, 8, 0, 0),
                    WorkspacePath = @"E:\MyCodex-Automations"
                };
                snapshot.Runs.Add(run);
                _store.Save(snapshot);
                return Task.FromResult(run);
            }

            public void CancelRunning(string reason)
            {
            }

            public bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion)
            {
                return false;
            }
        }

        private sealed class RecordingAutomationService : IAutomationService
        {
            public string MarkReadRunId { get; private set; }

            public string ArchiveRunId { get; private set; }

            public string DeleteRunId { get; private set; }

            public IReadOnlyList<AutomationDefinitionSnapshot> ListDefinitions()
            {
                throw new NotSupportedException();
            }

            public IReadOnlyList<AutomationRunSnapshot> ListRuns()
            {
                throw new NotSupportedException();
            }

            public AutomationDefinitionSnapshot Create(AutomationCreationRequest request)
            {
                throw new NotSupportedException();
            }

            public AutomationDefinitionSnapshot Update(AutomationUpdateRequest request)
            {
                throw new NotSupportedException();
            }

            public void Disable(string automationId)
            {
                throw new NotSupportedException();
            }

            public void Enable(string automationId)
            {
                throw new NotSupportedException();
            }

            public void Delete(string automationId)
            {
                throw new NotSupportedException();
            }

            public void MarkRunRead(string runId)
            {
                MarkReadRunId = runId;
            }

            public void ArchiveRun(string runId)
            {
                ArchiveRunId = runId;
            }

            public void DeleteRun(string runId)
            {
                DeleteRunId = runId;
            }
        }

        private sealed class InMemoryAutomationStore : IAutomationStore
        {
            public AutomationStateSnapshot Snapshot = new AutomationStateSnapshot();

            public AutomationStateSnapshot Load()
            {
                return Snapshot;
            }

            public void Save(AutomationStateSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation)
            {
                Snapshot = mutation(Snapshot) ?? throw new InvalidOperationException();
                return Snapshot;
            }
        }
    }
}
