using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationRunnerTests
    {
        [Test]
        public async Task StartAsync_ThreadAutomation_ReusesExistingThreadAndSendsPrompt()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildThreadDefinition());
            var appServer = new FakeThreadClient();
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1");

            var run = await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-1",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });

            Assert.That(run.Status, Is.EqualTo(AutomationRunStatus.Running));
            Assert.That(run.ThreadId, Is.EqualTo("thread-existing"));
            Assert.That(appServer.CreatedThreadCount, Is.EqualTo(0));
            Assert.That(appServer.SentMessages, Has.Count.EqualTo(1));
            Assert.That(appServer.SentMessages[0].ThreadId, Is.EqualTo("thread-existing"));
            Assert.That(appServer.SentMessages[0].InputItems.Single().Text, Does.Contain("Check deployment"));
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task StartAsync_StandaloneAutomation_CreatesBackgroundThreadAndSendsPrompt()
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildStandaloneDefinition();
            definition.Name = "Apple news";
            store.Snapshot.Definitions.Add(definition);
            var appServer = new FakeThreadClient { NextThreadId = "thread-new" };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1");

            var run = await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-2",
                TriggerReason = AutomationTriggerReason.Scheduled,
                ScheduledFor = Now
            });

            Assert.That(appServer.CreatedThreadCount, Is.EqualTo(1));
            Assert.That(run.ThreadId, Is.EqualTo("thread-new"));
            Assert.That(appServer.SetThreadNameCalls, Has.Count.EqualTo(1));
            Assert.That(appServer.SetThreadNameCalls[0].ThreadId, Is.EqualTo("thread-new"));
            Assert.That(appServer.SetThreadNameCalls[0].Name, Is.EqualTo("Apple news"));
            Assert.That(appServer.SentMessages[0].ThreadId, Is.EqualTo("thread-new"));
            Assert.That(appServer.SentMessages[0].ApprovalMode, Is.EqualTo(ApprovalMode.ReadOnly));
        }

        [Test]
        public async Task StartAsync_UsesResolvedModelAndRecordsResolutionOnRun()
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildStandaloneDefinition();
            definition.ModelId = "gpt-5.5";
            definition.ReasoningEffort = "high";
            store.Snapshot.Definitions.Add(definition);
            var appServer = new FakeThreadClient { NextThreadId = "thread-new" };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1")
            {
                ModelResolver = new FakeAutomationModelResolver(new AutomationModelResolution
                {
                    ConfiguredModelId = "gpt-5.5",
                    ConfiguredReasoningEffort = "high",
                    ModelId = "deepseek-chat",
                    ReasoningEffort = null,
                    UsedFallback = true,
                    Message = "Configured model gpt-5.5 is unavailable. This run used deepseek-chat."
                })
            };

            var run = await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-2",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });

            Assert.That(appServer.CreatedThreads[0].ModelId, Is.EqualTo("deepseek-chat"));
            Assert.That(appServer.CreatedThreads[0].ReasoningEffort, Is.Null);
            Assert.That(appServer.SentMessages[0].ModelId, Is.EqualTo("deepseek-chat"));
            Assert.That(appServer.SentMessages[0].ReasoningEffort, Is.Null);
            Assert.That(run.ConfiguredModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(run.ActualModelId, Is.EqualTo("deepseek-chat"));
            Assert.That(run.ActualReasoningEffort, Is.Null);
            Assert.That(run.ModelResolutionMessage, Does.Contain("deepseek-chat"));
        }

        [Test]
        public async Task StartAsync_OfficeAutomation_SendsOfficeCollaborationMode()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildOfficeDefinition());
            var appServer = new FakeThreadClient { NextThreadId = "thread-office" };
            var runner = new AutomationRunner(
                store,
                appServer,
                new AutomationResultClassifier(),
                new AutomationCollaborationModePlanner(),
                () => Now,
                () => "run-1");

            await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-office",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });

            Assert.That(appServer.SentMessages, Has.Count.EqualTo(1));
            Assert.That(appServer.SentMessages[0].CollaborationMode, Is.Not.Null);
            Assert.That(appServer.SentMessages[0].CollaborationMode.Mode, Is.EqualTo("default"));
            Assert.That(appServer.SentMessages[0].CollaborationMode.DeveloperInstructions, Does.Contain(@"E:\Documents"));
            Assert.That(appServer.SentMessages[0].CollaborationMode.DeveloperInstructions, Does.Contain(@"E:\Documents\Reports"));
        }

        [Test]
        public async Task StartAsync_ProjectAutomation_SendsNullCollaborationMode()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildProjectDefinition());
            var appServer = new FakeThreadClient { NextThreadId = "thread-project" };
            var runner = new AutomationRunner(
                store,
                appServer,
                new AutomationResultClassifier(),
                new AutomationCollaborationModePlanner(),
                () => Now,
                () => "run-1");

            await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-project",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });

            Assert.That(appServer.SentMessages, Has.Count.EqualTo(1));
            Assert.That(appServer.SentMessages[0].CollaborationMode, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task StartAsync_UserDeletesDefinitionDuringCreateThread_DoesNotRestoreDefinitionOrLoseRunOwnership(bool cancel)
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildStandaloneDefinition();
            definition.Context.RuntimeProfileId = "windows";
            store.Snapshot.Definitions.Add(definition);
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            { Id = "foreign-orphan", AutomationId = "deleted-foreign-task", ThreadId = "foreign-thread", Status = AutomationRunStatus.Running });
            var appServer = new FakeThreadClient
            {
                CreateThreadEntered = NewCompletionSource<object>(),
                CreateThreadCompletion = NewCompletionSource<string>()
            };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1")
            { TargetScope = new AutomationTargetScopePlanner("windows", false) };
            var service = new AutomationService(store, new AutomationScheduleCalculator());

            var runnerTask = runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-2",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });
            Assert.That(
                await Task.WhenAny(appServer.CreateThreadEntered.Task, runnerTask),
                Is.SameAs(appServer.CreateThreadEntered.Task),
                "Runner completed before entering CreateThreadAsync: " + runnerTask.Exception);

            var deleteTask = Task.Run(() => service.Delete("automation-2"));
            Assert.That(await Task.WhenAny(deleteTask, Task.Delay(5000)), Is.SameAs(deleteTask), "Delete was blocked while CreateThreadAsync awaited.");
            await deleteTask;
            appServer.CreateThreadCompletion.SetResult("thread-1");
            await runnerTask;

            var snapshot = store.Load();
            Assert.That(snapshot.Definitions.Any(item => item.Id == "automation-2"), Is.False);
            Assert.That(snapshot.Runs.Single(item => item.Id == "run-1").ThreadId, Is.EqualTo("thread-1"));
            if (cancel) runner.CancelRunning("Owner exited after deleting task.");
            else Assert.That(runner.CompleteByThread("thread-1", "turn-1", new AutomationRunCompletion
            {
                Status = AutomationRunStatus.Completed,
                CompletedAt = Now.AddMinutes(2),
                Summary = "Completed after deletion."
            }), Is.True);
            Assert.That(store.Load().Runs.Single(item => item.Id == "run-1").Status,
                Is.EqualTo(cancel ? AutomationRunStatus.Canceled : AutomationRunStatus.Completed));
            Assert.That(store.Load().Runs.Single(item => item.Id == "foreign-orphan").Status, Is.EqualTo(AutomationRunStatus.Running));
            Assert.That(store.Load().Definitions, Is.Empty);
        }

        [Test]
        public async Task StartAsync_UserUpdatesOfficeDefinitionDuringModelResolution_PreservesUpdateAndOfficeContext()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildOfficeDefinition());
            var resolver = new GatedAutomationModelResolver();
            var runner = new AutomationRunner(
                store,
                new FakeThreadClient { NextThreadId = "thread-office" },
                new AutomationResultClassifier(),
                new AutomationCollaborationModePlanner(),
                () => Now,
                () => "run-1")
            {
                ModelResolver = resolver
            };
            var service = new AutomationService(store, new AutomationScheduleCalculator(), () => Now.AddMinutes(1), () => "unused");

            var runnerTask = runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-office",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });
            Assert.That(
                await Task.WhenAny(resolver.Entered.Task, runnerTask),
                Is.SameAs(resolver.Entered.Task),
                "Runner completed before entering model resolution: " + runnerTask.Exception);

            var updateTask = Task.Run(() => service.Update(new AutomationUpdateRequest
            {
                Id = "automation-office",
                Name = "Updated office report",
                Kind = AutomationKind.Office,
                Prompt = "Review updated quarterly reports.",
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly },
                Context = new AutomationContextSnapshot
                {
                    WorkspaceId = "office-updated",
                    WorkspacePath = @"E:\Documents\Updated Reports",
                    OfficeRootPath = @"E:\Documents",
                    RuntimeProfileId = "windows",
                    OfficeRuntimeScopeId = "remote-unix|reports",
                    InteractionMode = AppInteractionMode.Office
                },
                ApprovalMode = ApprovalMode.ReadOnly
            }));
            Assert.That(await Task.WhenAny(updateTask, Task.Delay(5000)), Is.SameAs(updateTask), "Update was blocked while model resolution awaited.");
            await updateTask;
            resolver.Completion.SetResult(new AutomationModelResolution { ModelId = "gpt-5.5" });
            await runnerTask;

            var definition = store.Load().Definitions.Single(item => item.Id == "automation-office");
            Assert.That(definition.Name, Is.EqualTo("Updated office report"));
            Assert.That(definition.Context.InteractionMode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(definition.Context.WorkspaceId, Is.EqualTo("office-updated"));
            Assert.That(definition.Context.WorkspacePath, Is.EqualTo(@"E:\Documents\Updated Reports"));
            Assert.That(definition.Context.OfficeRootPath, Is.EqualTo(@"E:\Documents"));
            Assert.That(definition.Context.RuntimeProfileId, Is.EqualTo("windows"));
            Assert.That(definition.Context.OfficeRuntimeScopeId, Is.EqualTo("remote-unix|reports"));
            Assert.That(store.Load().Runs.Single().ActualModelId, Is.EqualTo("gpt-5.5"));
        }

        [Test]
        public async Task StartAsync_UserCreatesAutomationDuringSendMessage_PreservesCreatedDefinition()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildThreadDefinition());
            var appServer = new FakeThreadClient
            {
                SendMessageEntered = NewCompletionSource<object>(),
                SendMessageCompletion = NewCompletionSource<object>()
            };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1");
            var service = new AutomationService(store, new AutomationScheduleCalculator(), () => Now, () => "automation-created");

            var runnerTask = runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-1",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });
            Assert.That(
                await Task.WhenAny(appServer.SendMessageEntered.Task, runnerTask),
                Is.SameAs(appServer.SendMessageEntered.Task),
                "Runner completed before entering SendUserMessageAsync: " + runnerTask.Exception);

            var createTask = Task.Run(() => service.Create(new AutomationCreationRequest
            {
                Name = "Created while another automation runs",
                Kind = AutomationKind.Project,
                Prompt = "Review another project.",
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly },
                Context = new AutomationContextSnapshot
                {
                    WorkspaceId = "project-2",
                    WorkspacePath = @"E:\Project2",
                    InteractionMode = AppInteractionMode.Coding
                }
            }));
            Assert.That(await Task.WhenAny(createTask, Task.Delay(5000)), Is.SameAs(createTask), "Create was blocked while SendUserMessageAsync awaited.");
            await createTask;
            appServer.SendMessageCompletion.SetResult(null);
            await runnerTask;

            Assert.That(store.Load().Definitions.Any(item => item.Id == "automation-created"), Is.True);
        }

        [Test]
        public void CancelRunning_CancelsEveryRunningRunAndPreservesDefinitionsAndHistory()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildOfficeDefinition());
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            {
                Id = "running-1",
                AutomationId = "automation-office",
                Status = AutomationRunStatus.Running,
                StartedAt = Now
            });
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            {
                Id = "completed-1",
                AutomationId = "automation-office",
                Status = AutomationRunStatus.Completed,
                StartedAt = Now.AddMinutes(-2),
                CompletedAt = Now.AddMinutes(-1),
                Summary = "Completed result"
            });
            var runner = new AutomationRunner(
                store,
                new FakeThreadClient(),
                new AutomationResultClassifier(),
                () => Now,
                () => "unused");

            runner.CancelRunning("Runtime target changed.");

            var snapshot = store.Load();
            Assert.That(snapshot.Definitions, Has.Count.EqualTo(1));
            Assert.That(snapshot.Runs, Has.Count.EqualTo(2));
            var canceled = snapshot.Runs.Single(item => item.Id == "running-1");
            Assert.That(canceled.Status, Is.EqualTo(AutomationRunStatus.Canceled));
            Assert.That(canceled.CompletedAt, Is.EqualTo(Now));
            Assert.That(canceled.Summary, Is.EqualTo("Runtime target changed."));
            var completed = snapshot.Runs.Single(item => item.Id == "completed-1");
            Assert.That(completed.Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(completed.Summary, Is.EqualTo("Completed result"));
        }

        [Test]
        public async Task StartAsync_SendsSkillPluginAndPromptInOrder()
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildThreadDefinition();
            definition.InputItems = new List<AutomationInputItemSnapshot>
            {
                new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Skill,
                    Name = "news-digest",
                    DisplayName = "News Digest",
                    Path = @"E:\skills\news-digest"
                },
                new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Plugin,
                    Name = "mycodex-office",
                    DisplayName = "MyCodex Office",
                    Path = @"E:\plugins\mycodex-office"
                }
            };
            store.Snapshot.Definitions.Add(definition);
            var appServer = new FakeThreadClient();
            var runner = new AutomationRunner(
                store,
                appServer,
                new AutomationResultClassifier(),
                new AutomationScheduleCalculator(),
                new AutomationInputItemPlanner(path => true),
                () => Now,
                () => "run-1");

            await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-1",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            });

            var inputItems = appServer.SentMessages[0].InputItems.ToArray();
            Assert.That(inputItems, Has.Length.EqualTo(3));
            Assert.That(inputItems[0].Type, Is.EqualTo("skill"));
            Assert.That(inputItems[0].Name, Is.EqualTo("news-digest"));
            Assert.That(inputItems[1].Type, Is.EqualTo("mention"));
            Assert.That(inputItems[1].Name, Is.EqualTo("mycodex-office"));
            Assert.That(inputItems[2].Type, Is.EqualTo("text"));
            Assert.That(inputItems[2].Text, Is.EqualTo("Check deployment status."));
        }

        [Test]
        public void StartAsync_InvalidInputReference_MarksRunFailedBeforeRethrowing()
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildThreadDefinition();
            definition.InputItems = new List<AutomationInputItemSnapshot>
            {
                new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Skill,
                    Name = "missing-skill",
                    DisplayName = "Missing Skill",
                    Path = @"E:\missing\skill"
                }
            };
            store.Snapshot.Definitions.Add(definition);
            var appServer = new FakeThreadClient();
            var runner = new AutomationRunner(
                store,
                appServer,
                new AutomationResultClassifier(),
                new AutomationScheduleCalculator(),
                new AutomationInputItemPlanner(path => false),
                () => Now,
                () => "run-1");

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-1",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            }));

            Assert.That(exception.Message, Does.Contain("missing-skill"));
            Assert.That(appServer.SentMessages, Is.Empty);
            Assert.That(appServer.CreatedThreadCount, Is.EqualTo(0));
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Failed));
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.True);
            Assert.That(store.Snapshot.Runs[0].Unread, Is.True);
            Assert.That(store.Snapshot.Runs[0].ErrorMessage, Does.Contain("missing-skill"));
        }

        [Test]
        public void StartAsync_SendUserMessageFails_MarksRunFailedBeforeRethrowing()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildThreadDefinition());
            var appServer = new FakeThreadClient
            {
                SendException = new InvalidOperationException("send failed")
            };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1");

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-1",
                TriggerReason = AutomationTriggerReason.Manual,
                ScheduledFor = Now
            }));

            Assert.That(exception.Message, Does.Contain("send failed"));
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Failed));
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.True);
            Assert.That(store.Snapshot.Runs[0].Unread, Is.True);
            Assert.That(store.Snapshot.Runs[0].ErrorMessage, Does.Contain("send failed"));
        }

        [Test]
        public void StartAsync_CreateThreadFails_MarksRunFailedBeforeRethrowing()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildStandaloneDefinition());
            var appServer = new FakeThreadClient
            {
                CreateException = new InvalidOperationException("create failed")
            };
            var runner = new AutomationRunner(store, appServer, new AutomationResultClassifier(), () => Now, () => "run-1");

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.StartAsync(new AutomationRunRequest
            {
                AutomationId = "automation-2",
                TriggerReason = AutomationTriggerReason.Scheduled,
                ScheduledFor = Now
            }));

            Assert.That(exception.Message, Does.Contain("create failed"));
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Failed));
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.True);
            Assert.That(store.Snapshot.Runs[0].Unread, Is.True);
            Assert.That(store.Snapshot.Runs[0].ThreadId, Is.Null.Or.Empty);
            Assert.That(store.Snapshot.Runs[0].ErrorMessage, Does.Contain("create failed"));
        }

        [Test]
        public void CompleteAsync_UpdatesRunningRunByThreadAndTurn()
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildThreadDefinition());
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            {
                Id = "run-1",
                AutomationId = "automation-1",
                Status = AutomationRunStatus.Running,
                ThreadId = "thread-existing"
            });
            var runner = new AutomationRunner(store, new FakeThreadClient(), new AutomationResultClassifier(), () => Now, () => "run-2");

            var updated = runner.CompleteByThread("thread-existing", "turn-1", new AutomationRunCompletion
            {
                Status = AutomationRunStatus.Completed,
                CompletedAt = Now.AddMinutes(2),
                Summary = "Found one issue."
            });

            Assert.That(updated, Is.True);
            Assert.That(store.Snapshot.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(store.Snapshot.Runs[0].TurnId, Is.EqualTo("turn-1"));
            Assert.That(store.Snapshot.Runs[0].NeedsAttention, Is.True);
            Assert.That(store.Snapshot.Definitions[0].LastRunAt, Is.EqualTo(Now.AddMinutes(2)));
        }

        [TestCase("windows", false, AppInteractionMode.Coding, @"C:\project")]
        [TestCase("windows", false, AppInteractionMode.Office, @"C:\documents")]
        [TestCase("wsl|debian", true, AppInteractionMode.Coding, "/srv/project")]
        [TestCase("wsl|debian", true, AppInteractionMode.Office, "/srv/documents")]
        [TestCase("remote-unix|build", true, AppInteractionMode.Coding, "/home/dev/project")]
        [TestCase("remote-unix|build", true, AppInteractionMode.Office, "/home/dev/documents")]
        public async Task StartAsync_TargetScope_UsesOnlyTheSelectedModesScopeAndHostPath(
            string scope, bool unix, AppInteractionMode mode, string workspace)
        {
            var definition = mode == AppInteractionMode.Office ? BuildOfficeDefinition() : BuildStandaloneDefinition();
            definition.Context.RuntimeProfileId = mode == AppInteractionMode.Coding ? scope : "unrelated-coding-scope";
            definition.Context.OfficeRuntimeScopeId = mode == AppInteractionMode.Office ? scope : "unrelated-office-scope";
            definition.Context.WorkspacePath = workspace;
            definition.Context.OfficeRootPath = mode == AppInteractionMode.Office ? workspace : null;
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(definition);
            var client = new FakeThreadClient();
            var runner = new AutomationRunner(store, client, new AutomationResultClassifier(), () => Now, () => "owned-run")
            { TargetScope = new AutomationTargetScopePlanner(scope, unix) };

            var run = await runner.StartAsync(new AutomationRunRequest { AutomationId = definition.Id });

            Assert.That(run.WorkspacePath, Is.EqualTo(workspace));
            Assert.That(client.CreatedThreads.Single().WorkspacePath, Is.EqualTo(workspace));
            Assert.That(client.SentMessages.Single().WorkspacePath, Is.EqualTo(workspace));
            Assert.That(client.SentMessages.Single().CollaborationMode != null, Is.EqualTo(mode == AppInteractionMode.Office));
        }

        [TestCase(AppInteractionMode.Coding, null, @"C:\project")]
        [TestCase(AppInteractionMode.Coding, "wsl|debian", @"C:\project")]
        [TestCase(AppInteractionMode.Coding, "windows", "/wrong-host/project")]
        [TestCase(AppInteractionMode.Office, null, @"C:\documents")]
        [TestCase(AppInteractionMode.Office, "remote-unix|build", @"C:\documents")]
        public void StartAsync_TargetScope_RejectsWrongOrMissingScopeBeforeRunModelOrClientSideEffects(
            AppInteractionMode mode, string scope, string workspace)
        {
            var definition = mode == AppInteractionMode.Office ? BuildOfficeDefinition() : BuildStandaloneDefinition();
            definition.Context.RuntimeProfileId = mode == AppInteractionMode.Coding ? scope : "windows";
            definition.Context.OfficeRuntimeScopeId = scope;
            definition.Context.WorkspacePath = workspace;
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(definition);
            var client = new FakeThreadClient();
            var resolver = new FakeAutomationModelResolver(new AutomationModelResolution());
            var idsAllocated = 0;
            var runner = new AutomationRunner(store, client, new AutomationResultClassifier(), () => Now,
                () => "run-" + (++idsAllocated))
            { TargetScope = new AutomationTargetScopePlanner("windows", false), ModelResolver = resolver };

            Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(new AutomationRunRequest { AutomationId = definition.Id }));

            Assert.That(store.Load().Runs, Is.Empty);
            Assert.That(idsAllocated, Is.Zero);
            Assert.That(resolver.Calls, Is.Zero);
            Assert.That(client.CreatedThreadCount, Is.Zero);
            Assert.That(client.SentMessages, Is.Empty);
            Assert.That(client.SetThreadNameCalls, Is.Empty);
        }

        [TestCase(AutomationTriggerReason.Manual, AutomationTriggerReason.Scheduled)]
        [TestCase(AutomationTriggerReason.Scheduled, AutomationTriggerReason.Manual)]
        public async Task StartAsync_ConcurrentManualAndScheduled_ReservesOneRunWhileThreadCreationIsPending(
            AutomationTriggerReason firstReason, AutomationTriggerReason secondReason)
        {
            var definition = BuildStandaloneDefinition();
            definition.Context.RuntimeProfileId = "windows";
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(definition);
            var client = new FakeThreadClient
            { CreateThreadEntered = NewCompletionSource<object>(), CreateThreadCompletion = NewCompletionSource<string>() };
            var id = 0;
            var runner = new AutomationRunner(store, client, new AutomationResultClassifier(), () => Now,
                () => "run-" + Interlocked.Increment(ref id))
            { TargetScope = new AutomationTargetScopePlanner("windows", false) };
            var first = runner.StartAsync(new AutomationRunRequest { AutomationId = definition.Id, TriggerReason = firstReason });
            try
            {
                Assert.That(await Task.WhenAny(client.CreateThreadEntered.Task, first, Task.Delay(5000)),
                    Is.SameAs(client.CreateThreadEntered.Task));
                Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(new AutomationRunRequest
                { AutomationId = definition.Id, TriggerReason = secondReason }));
                Assert.That(store.Load().Runs.Count, Is.EqualTo(1));
                Assert.That(client.CreatedThreadCount, Is.EqualTo(1));
            }
            finally
            {
                client.CreateThreadCompletion.TrySetResult("owned-thread");
                await first;
            }
            Assert.That(client.SentMessages.Count, Is.EqualTo(1));
            Assert.That(store.Load().Runs.Single().TriggerReason, Is.EqualTo(firstReason));
        }

        [TestCase(AutomationRunStatus.Queued)]
        [TestCase(AutomationRunStatus.Running)]
        [TestCase(AutomationRunStatus.WaitingForApproval)]
        [TestCase(AutomationRunStatus.WaitingForInput)]
        public void StartAsync_ActiveRunStatesPreventDuplicateClientRequests(AutomationRunStatus status)
        {
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.Add(BuildStandaloneDefinition());
            store.Snapshot.Runs.Add(new AutomationRunSnapshot
            { Id = "active", AutomationId = "automation-2", Status = status });
            var client = new FakeThreadClient();
            var runner = new AutomationRunner(store, client, new AutomationResultClassifier());
            Assert.ThrowsAsync<InvalidOperationException>(() => runner.StartAsync(new AutomationRunRequest { AutomationId = "automation-2" }));
            Assert.That(store.Load().Runs.Single().Status, Is.EqualTo(status));
            Assert.That(client.CreatedThreadCount, Is.Zero);
            Assert.That(client.SentMessages, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CancelRunning_DuringThreadCreationOrModelResolution_DoesNotSendOrResurrectCanceledRun(
            bool duringModelResolution, bool lateFailure)
        {
            var store = new InMemoryAutomationStore();
            var definition = BuildStandaloneDefinition();
            definition.Context.RuntimeProfileId = "windows";
            store.Snapshot.Definitions.Add(definition);
            var resolver = new GatedAutomationModelResolver();
            var client = new FakeThreadClient();
            if (!duringModelResolution)
            {
                client.CreateThreadEntered = NewCompletionSource<object>();
                client.CreateThreadCompletion = NewCompletionSource<string>();
            }
            var runner = new AutomationRunner(store, client, new AutomationResultClassifier(), () => Now, () => "canceled-run")
            {
                TargetScope = new AutomationTargetScopePlanner("windows", false),
                ModelResolver = duringModelResolution ? resolver : null
            };
            var starting = runner.StartAsync(new AutomationRunRequest { AutomationId = definition.Id });
            AutomationRunSnapshot result = null;
            try
            {
                var entered = duringModelResolution ? resolver.Entered.Task : client.CreateThreadEntered.Task;
                Assert.That(await Task.WhenAny(entered, starting, Task.Delay(5000)), Is.SameAs(entered));
                runner.CancelRunning("This target stopped while startup awaited.");
                Assert.That(store.Load().Runs.Single().Status, Is.EqualTo(AutomationRunStatus.Canceled));
            }
            finally
            {
                if (duringModelResolution)
                {
                    if (lateFailure) resolver.Completion.TrySetException(new InvalidOperationException("Late model failure"));
                    else resolver.Completion.TrySetResult(new AutomationModelResolution { ModelId = "gpt-5.5" });
                }
                else
                {
                    if (lateFailure) client.CreateThreadCompletion.TrySetException(new InvalidOperationException("Late thread failure"));
                    else client.CreateThreadCompletion.TrySetResult("canceled-thread");
                }
                try { result = await starting; }
                catch (InvalidOperationException) { if (!lateFailure) throw; }
            }
            Assert.That(client.SentMessages, Is.Empty);
            Assert.That(client.CreatedThreadCount, Is.EqualTo(duringModelResolution ? 0 : 1));
            Assert.That(store.Load().Runs.Single().Status, Is.EqualTo(AutomationRunStatus.Canceled));
            Assert.That(store.Load().Runs.Single().Summary, Is.EqualTo("This target stopped while startup awaited."));
            Assert.That(store.Load().Runs.Single().ErrorMessage, Is.Null);
            if (result != null) Assert.That(result.Status, Is.EqualTo(AutomationRunStatus.Canceled));
        }

        [Test]
        public void CompleteAndCancel_TargetScope_LeaveForeignRunsAndSchedulesUnchangedEvenForTheSameThread()
        {
            var owned = BuildStandaloneDefinition();
            owned.Context.RuntimeProfileId = "windows";
            owned.Enabled = true;
            var foreign = BuildOfficeDefinition();
            foreign.Context.OfficeRuntimeScopeId = "wsl|debian";
            foreign.Context.WorkspacePath = "/srv/documents";
            foreign.Context.OfficeRootPath = "/srv/documents";
            foreign.NextRunAt = Now.AddHours(3);
            var store = new InMemoryAutomationStore();
            store.Snapshot.Definitions.AddRange(new[] { owned, foreign });
            store.Snapshot.Runs.AddRange(new[]
            {
                new AutomationRunSnapshot { Id = "owned", AutomationId = owned.Id, ThreadId = "same-thread", Status = AutomationRunStatus.Running, StartedAt = Now },
                new AutomationRunSnapshot { Id = "foreign", AutomationId = foreign.Id, ThreadId = "same-thread", Status = AutomationRunStatus.Running, StartedAt = Now.AddMinutes(1) }
            });
            var runner = new AutomationRunner(store, new FakeThreadClient(), new AutomationResultClassifier(), () => Now, () => "unused")
            { TargetScope = new AutomationTargetScopePlanner("windows", false) };

            Assert.That(runner.CompleteByThread("same-thread", "own-turn", new AutomationRunCompletion
            { Status = AutomationRunStatus.Completed, CompletedAt = Now.AddMinutes(2), Summary = "Own target completed" }), Is.True);
            Assert.That(runner.CompleteByThread("same-thread", "foreign-turn", new AutomationRunCompletion
            { Status = AutomationRunStatus.Completed, CompletedAt = Now.AddMinutes(3) }), Is.False);
            store.Mutate(snapshot =>
            {
                snapshot.Runs.Add(new AutomationRunSnapshot { Id = "owned-cancel", AutomationId = owned.Id, Status = AutomationRunStatus.Running });
                return snapshot;
            });
            runner.CancelRunning("Only this target exited.");

            var saved = store.Load();
            Assert.That(saved.Runs.Single(run => run.Id == "owned").TurnId, Is.EqualTo("own-turn"));
            Assert.That(saved.Runs.Single(run => run.Id == "owned-cancel").Status, Is.EqualTo(AutomationRunStatus.Canceled));
            Assert.That(saved.Runs.Single(run => run.Id == "foreign").Status, Is.EqualTo(AutomationRunStatus.Running));
            Assert.That(saved.Runs.Single(run => run.Id == "foreign").CompletedAt, Is.Null);
            Assert.That(saved.Definitions.Single(item => item.Id == foreign.Id).LastRunAt, Is.Null);
            Assert.That(saved.Definitions.Single(item => item.Id == foreign.Id).NextRunAt, Is.EqualTo(Now.AddHours(3)));
        }

        private static readonly DateTime Now = new DateTime(2026, 6, 5, 8, 0, 0);

        private static TaskCompletionSource<T> NewCompletionSource<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static AutomationDefinitionSnapshot BuildThreadDefinition()
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-1",
                Kind = AutomationKind.Thread,
                Prompt = "Check deployment status.",
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.ReadOnly,
                CreatedAt = Now,
                UpdatedAt = Now,
                Context = new AutomationContextSnapshot
                {
                    ThreadId = "thread-existing",
                    WorkspacePath = @"E:\MyCodex",
                    WorkspaceId = "workspace-1"
                },
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly }
            };
        }

        private static AutomationDefinitionSnapshot BuildStandaloneDefinition()
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-2",
                Kind = AutomationKind.Standalone,
                Prompt = "Summarize news.",
                ApprovalMode = ApprovalMode.ReadOnly,
                CreatedAt = Now,
                UpdatedAt = Now,
                Context = new AutomationContextSnapshot
                {
                    WorkspacePath = @"E:\MyCodex\automations\workspace"
                },
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.EveryHours, Interval = 1 }
            };
        }

        private static AutomationDefinitionSnapshot BuildOfficeDefinition()
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-office",
                Kind = AutomationKind.Office,
                Prompt = "Summarize quarterly reports.",
                ModelId = "gpt-5.5",
                ReasoningEffort = "medium",
                ApprovalMode = ApprovalMode.ReadOnly,
                CreatedAt = Now,
                UpdatedAt = Now,
                Context = new AutomationContextSnapshot
                {
                    WorkspaceId = "office-reports",
                    WorkspacePath = @"E:\Documents\Reports",
                    OfficeRootPath = @"E:\Documents",
                    InteractionMode = AppInteractionMode.Office
                },
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly }
            };
        }

        private static AutomationDefinitionSnapshot BuildProjectDefinition()
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-project",
                Kind = AutomationKind.Project,
                Prompt = "Review repository changes.",
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.ReadOnly,
                CreatedAt = Now,
                UpdatedAt = Now,
                Context = new AutomationContextSnapshot
                {
                    WorkspaceId = "project-workspace",
                    WorkspacePath = @"E:\Project",
                    InteractionMode = AppInteractionMode.Coding
                },
                Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.ManualOnly }
            };
        }

        private sealed class InMemoryAutomationStore : IAutomationStore
        {
            private readonly object _syncRoot = new object();

            public AutomationStateSnapshot Snapshot = new AutomationStateSnapshot();

            public AutomationStateSnapshot Load()
            {
                return Clone(Snapshot);
            }

            public void Save(AutomationStateSnapshot snapshot)
            {
                Snapshot = Clone(snapshot);
            }

            public AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation)
            {
                lock (_syncRoot)
                {
                    var updated = mutation(Clone(Snapshot)) ?? throw new InvalidOperationException();
                    Snapshot = Clone(updated);
                    return Clone(Snapshot);
                }
            }

            private static AutomationStateSnapshot Clone(AutomationStateSnapshot snapshot)
            {
                var serializer = new DataContractJsonSerializer(typeof(AutomationStateSnapshot));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, snapshot);
                    stream.Position = 0;
                    return (AutomationStateSnapshot)serializer.ReadObject(stream);
                }
            }
        }

        private sealed class FakeThreadClient : UnsupportedAppServerThreadClient
        {
            public string NextThreadId = "thread-created";
            public int CreatedThreadCount;
            public Exception CreateException;
            public Exception SendException;
            public TaskCompletionSource<object> CreateThreadEntered;
            public TaskCompletionSource<string> CreateThreadCompletion;
            public TaskCompletionSource<object> SendMessageEntered;
            public TaskCompletionSource<object> SendMessageCompletion;
            public readonly List<CreatedThread> CreatedThreads = new List<CreatedThread>();
            public readonly List<SentMessage> SentMessages = new List<SentMessage>();
            public readonly List<SetThreadNameCall> SetThreadNameCalls = new List<SetThreadNameCall>();

            public override Task<string> CreateThreadAsync(string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerThreadToolContext toolContext = null)
            {
                CreatedThreadCount++;
                if (CreateException != null)
                {
                    throw CreateException;
                }

                CreatedThreads.Add(new CreatedThread
                {
                    WorkspacePath = workspacePath,
                    ApprovalMode = approvalMode,
                    ModelId = modelId,
                    ReasoningEffort = reasoningEffort
                });
                if (CreateThreadCompletion != null)
                {
                    CreateThreadEntered.SetResult(null);
                    return CreateThreadCompletion.Task;
                }

                return Task.FromResult(NextThreadId);
            }

            public override Task SendUserMessageAsync(string threadId, string workspacePath, IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null)
            {
                if (SendException != null)
                {
                    throw SendException;
                }

                SentMessages.Add(new SentMessage
                {
                    ThreadId = threadId,
                    WorkspacePath = workspacePath,
                    InputItems = inputItems,
                    ApprovalMode = approvalMode,
                    ModelId = modelId,
                    ReasoningEffort = reasoningEffort,
                    CollaborationMode = collaborationMode
                });
                if (SendMessageCompletion != null)
                {
                    SendMessageEntered.SetResult(null);
                    return SendMessageCompletion.Task;
                }

                return Task.CompletedTask;
            }

            public override Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult<AppServerConversationSummary>(null); }
            public override Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null) { return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double")); }
            public override Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = true }); }
            public override Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.CompletedTask; }
            public override Task SetThreadNameAsync(string threadId, string name)
            {
                SetThreadNameCalls.Add(new SetThreadNameCall { ThreadId = threadId, Name = name });
                return Task.CompletedTask;
            }
        }

        private sealed class SentMessage
        {
            public string ThreadId;
            public string WorkspacePath;
            public IReadOnlyCollection<AppServerUserInputItem> InputItems;
            public ApprovalMode? ApprovalMode;
            public string ModelId;
            public string ReasoningEffort;
            public AppServerCollaborationMode CollaborationMode;
        }

        private sealed class CreatedThread
        {
            public string WorkspacePath;
            public ApprovalMode? ApprovalMode;
            public string ModelId;
            public string ReasoningEffort;
        }

        private sealed class SetThreadNameCall
        {
            public string ThreadId;
            public string Name;
        }

        private sealed class FakeAutomationModelResolver : IAutomationModelResolver
        {
            private readonly AutomationModelResolution _resolution;
            public int Calls;

            public FakeAutomationModelResolver(AutomationModelResolution resolution)
            {
                _resolution = resolution;
            }

            public Task<AutomationModelResolution> ResolveAsync(AutomationDefinitionSnapshot definition)
            {
                Calls++;
                return Task.FromResult(_resolution);
            }
        }

        private sealed class GatedAutomationModelResolver : IAutomationModelResolver
        {
            public readonly TaskCompletionSource<object> Entered = NewCompletionSource<object>();
            public readonly TaskCompletionSource<AutomationModelResolution> Completion = NewCompletionSource<AutomationModelResolution>();

            public Task<AutomationModelResolution> ResolveAsync(AutomationDefinitionSnapshot definition)
            {
                Entered.SetResult(null);
                return Completion.Task;
            }
        }
    }
}
