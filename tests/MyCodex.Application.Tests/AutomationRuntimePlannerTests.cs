using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationRuntimePlannerTests
    {
        [Test]
        public void BuildDueRunIds_ReturnsEnabledDueDefinitionsOnly()
        {
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator());
            var now = new DateTime(2026, 6, 5, 8, 0, 0);
            var state = new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    new AutomationDefinitionSnapshot { Id = "due", Enabled = true, NextRunAt = now },
                    new AutomationDefinitionSnapshot { Id = "future", Enabled = true, NextRunAt = now.AddMinutes(1) },
                    new AutomationDefinitionSnapshot { Id = "paused", Enabled = false, NextRunAt = now }
                }
            };

            var result = planner.BuildDueRunIds(state, now);

            CollectionAssert.AreEqual(new[] { "due" }, result);
        }

        [Test]
        public void BuildDueRunIds_SkipsDefinitionWithRunningRun()
        {
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator());
            var now = new DateTime(2026, 6, 5, 8, 0, 0);
            var state = new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    new AutomationDefinitionSnapshot { Id = "due", Enabled = true, NextRunAt = now }
                },
                Runs = new List<AutomationRunSnapshot>
                {
                    new AutomationRunSnapshot { Id = "run-1", AutomationId = "due", Status = AutomationRunStatus.Running }
                }
            };

            var result = planner.BuildDueRunIds(state, now);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SkipMissedRunsOnStartup_RecordsSkippedRunAndAdvancesNextRun()
        {
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator());
            var now = new DateTime(2026, 6, 5, 9, 0, 0);
            var state = new AutomationStateSnapshot
            {
                Definitions = new List<AutomationDefinitionSnapshot>
                {
                    new AutomationDefinitionSnapshot
                    {
                        Id = "missed",
                        Enabled = true,
                        NextRunAt = new DateTime(2026, 6, 5, 8, 0, 0),
                        Schedule = new AutomationScheduleSnapshot
                        {
                            Kind = AutomationScheduleKind.EveryHours,
                            Interval = 2,
                            SkipMissedRuns = true
                        }
                    }
                }
            };

            var skipped = planner.SkipMissedRunsOnStartup(state, now, () => "skip-1");

            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(state.Runs, Has.Count.EqualTo(1));
            Assert.That(state.Runs[0].Id, Is.EqualTo("skip-1"));
            Assert.That(state.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Skipped));
            Assert.That(state.Runs[0].TriggerReason, Is.EqualTo(AutomationTriggerReason.StartupMissedRun));
            Assert.That(state.Runs[0].ScheduledFor, Is.EqualTo(new DateTime(2026, 6, 5, 8, 0, 0)));
            Assert.That(state.Runs[0].Summary, Is.EqualTo(SharedLocalization.Text(
                "AutomationRuntimePlanner.Summary.SkippedMissedRun",
                "Skipped missed automation run.")));
            Assert.That(state.Definitions[0].NextRunAt, Is.EqualTo(new DateTime(2026, 6, 5, 11, 0, 0)));
        }

        [Test]
        public void Start_StartupSkipMutatesCurrentSnapshotWithoutOverwritingConcurrentDefinition()
        {
            var missed = new AutomationDefinitionSnapshot
            {
                Id = "missed",
                Enabled = true,
                NextRunAt = DateTime.Now.AddHours(-2),
                Schedule = new AutomationScheduleSnapshot
                {
                    Kind = AutomationScheduleKind.EveryHours,
                    Interval = 1,
                    SkipMissedRuns = true
                }
            };
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        missed,
                        new AutomationDefinitionSnapshot { Id = "concurrent-update" }
                    }
                },
                LoadSnapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot> { missed }
                }
            };

            using (var host = new AutomationRuntimeHost(
                store,
                new ThrowingAutomationRunner(),
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                host.Start();
                host.Stop();
            }

            Assert.That(store.MutateCount, Is.EqualTo(1));
            Assert.That(store.SaveCount, Is.EqualTo(0));
            Assert.That(store.Snapshot.Definitions.Exists(item => item.Id == "concurrent-update"), Is.True);
            Assert.That(store.Snapshot.Runs, Has.Count.EqualTo(1));
            Assert.That(store.Snapshot.Runs[0].Status, Is.EqualTo(AutomationRunStatus.Skipped));
        }

        [Test]
        public void RunDueAutomationsAsync_ContinuesAfterRunnerFailureAndAllowsNextTick()
        {
            var now = DateTime.Now;
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot { Id = "first", Enabled = true, NextRunAt = now.AddMinutes(-1) },
                        new AutomationDefinitionSnapshot { Id = "second", Enabled = true, NextRunAt = now.AddMinutes(-1) }
                    }
                }
            };
            var runner = new ThrowingAutomationRunner();
            using (var host = new AutomationRuntimeHost(
                store,
                runner,
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                Assert.DoesNotThrowAsync(async () => await host.RunDueAutomationsAsync());
                Assert.DoesNotThrowAsync(async () => await host.RunDueAutomationsAsync());
            }

            CollectionAssert.AreEqual(
                new[] { "first", "second", "first", "second" },
                runner.AttemptedAutomationIds);
        }

        [Test]
        public async Task RunTimerTickAsync_PersistenceFailureInvokesRecoveryHandler()
        {
            var failure = new System.IO.IOException("automations file locked");
            var store = new InMemoryAutomationStore { LoadException = failure };
            Exception handled = null;
            using (var host = new AutomationRuntimeHost(
                store,
                new ThrowingAutomationRunner(),
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                host.SetPersistenceFailureHandler(ex => handled = ex);

                await host.RunTimerTickAsync();
            }

            Assert.That(handled, Is.SameAs(failure));
        }

        [Test]
        public async Task RunTimerTickAsync_RunnerPersistenceFailureInvokesRecoveryHandler()
        {
            var now = DateTime.Now;
            var failure = new System.IO.IOException("automation save failed");
            var store = new InMemoryAutomationStore
            {
                Snapshot = new AutomationStateSnapshot
                {
                    Definitions = new List<AutomationDefinitionSnapshot>
                    {
                        new AutomationDefinitionSnapshot
                        {
                            Id = "automation-1",
                            Enabled = true,
                            NextRunAt = now.AddMinutes(-1)
                        }
                    }
                }
            };
            Exception handled = null;
            using (var host = new AutomationRuntimeHost(
                store,
                new PersistenceFailingAutomationRunner(failure),
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                host.SetPersistenceFailureHandler(ex => handled = ex);

                await host.RunTimerTickAsync();
            }

            Assert.That(handled, Is.SameAs(failure));
        }

        [Test]
        public void HandleTurnTerminal_ReturnsTrueWhenRunnerCompletesAutomationRun()
        {
            var runner = new CompletingAutomationRunner(true);
            using (var host = new AutomationRuntimeHost(
                new InMemoryAutomationStore(),
                runner,
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                var updated = host.HandleTurnTerminal(
                    "thread-1",
                    "turn-1",
                    AutomationRunStatus.Completed,
                    "done",
                    null);

                Assert.That(updated, Is.True);
                Assert.That(runner.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(runner.TurnId, Is.EqualTo("turn-1"));
                Assert.That(runner.Completion.Status, Is.EqualTo(AutomationRunStatus.Completed));
                Assert.That(runner.Completion.Summary, Is.EqualTo("done"));
            }
        }

        [Test]
        public void HandleTurnTerminal_CompletionOverloadPassesFullCompletion()
        {
            var runner = new CompletingAutomationRunner(true);
            using (var host = new AutomationRuntimeHost(
                new InMemoryAutomationStore(),
                runner,
                new AutomationRuntimePlanner(new AutomationScheduleCalculator())))
            {
                var completion = new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.Completed,
                    Summary = "done",
                    ChangedFiles = new[] { "src/MainForm.cs" },
                    GeneratedArtifacts = new[] { @"E:\Reports\result.docx" }
                };

                var updated = host.HandleTurnTerminal("thread-1", "turn-1", completion);

                Assert.That(updated, Is.True);
                Assert.That(runner.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(runner.TurnId, Is.EqualTo("turn-1"));
                Assert.That(runner.Completion.TurnId, Is.EqualTo("turn-1"));
                Assert.That(runner.Completion.CompletedAt, Is.Not.EqualTo(default(DateTime)));
                CollectionAssert.AreEqual(new[] { "src/MainForm.cs" }, runner.Completion.ChangedFiles);
                CollectionAssert.AreEqual(new[] { @"E:\Reports\result.docx" }, runner.Completion.GeneratedArtifacts);
            }
        }

        [TestCase("windows", false)]
        [TestCase("wsl|debian", true)]
        [TestCase("remote-unix|build", true)]
        public void BuildDueRunIds_TargetScope_RequiresOwnCodingOrOfficeScopeAndCompatiblePaths(string scope, bool unix)
        {
            var now = new DateTime(2026, 6, 5, 9, 0, 0);
            var state = ScopedState(scope, unix, now);
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner(scope, unix) };

            Assert.That(planner.BuildDueRunIds(state, now), Is.EqualTo(new[] { "own-coding", "own-office" }));
            Assert.That(state.Runs, Is.Empty);
            Assert.That(state.Definitions.Single(item => item.Id == "foreign").NextRunAt, Is.EqualTo(now));
        }

        [TestCase("windows", false)]
        [TestCase("wsl|debian", true)]
        [TestCase("remote-unix|build", true)]
        public void StartupSkip_TargetScope_AdvancesOnlyOwnTasksAndRestartDoesNotDuplicateSkippedHistory(string scope, bool unix)
        {
            var now = new DateTime(2026, 6, 5, 9, 0, 0);
            var state = ScopedState(scope, unix, now.AddHours(-1));
            state.Runs.Add(new AutomationRunSnapshot { Id = "foreign-history", AutomationId = "foreign", Status = AutomationRunStatus.Completed });
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner(scope, unix) };
            var ids = 0;

            Assert.That(planner.SkipMissedRunsOnStartup(state, now, () => "skip-" + (++ids)), Is.EqualTo(2));
            var restarted = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner(scope, unix) };
            Assert.That(restarted.SkipMissedRunsOnStartup(state, now.AddMinutes(1), () => "skip-" + (++ids)), Is.Zero);

            Assert.That(ids, Is.EqualTo(2));
            Assert.That(state.Runs.Count, Is.EqualTo(3));
            Assert.That(state.Runs.Where(run => run.Status == AutomationRunStatus.Skipped).Select(run => run.AutomationId),
                Is.EquivalentTo(new[] { "own-coding", "own-office" }));
            foreach (var definition in state.Definitions)
                Assert.That(definition.NextRunAt, Is.EqualTo(definition.Id.StartsWith("own-", StringComparison.Ordinal)
                    ? now.AddHours(2) : now.AddHours(-1)), definition.Id);
            Assert.That(state.Runs.Single(run => run.Id == "foreign-history").Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(restarted.BuildDueRunIds(state, now.AddMinutes(1)), Is.Empty);
        }

        [Test]
        public void RuntimeHost_RestartAndDueTick_OnlyTouchOwnTargetTasks()
        {
            var now = DateTime.Now;
            var state = ScopedState("wsl|debian", true, now.AddHours(-1));
            var store = new InMemoryAutomationStore { Snapshot = state };
            var runner = new ThrowingAutomationRunner();
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner("wsl|debian", true) };
            using (var host = new AutomationRuntimeHost(store, runner, planner))
            {
                host.Start();
                host.Stop();
                host.Start();
                host.Stop();
                Assert.That(store.Snapshot.Runs.Count, Is.EqualTo(2));
                store.Snapshot.Definitions.Single(item => item.Id == "own-coding").NextRunAt = now.AddMinutes(-1);
                Assert.DoesNotThrowAsync(async () => await host.RunDueAutomationsAsync());
            }

            Assert.That(runner.AttemptedAutomationIds, Is.EqualTo(new[] { "own-coding" }));
            Assert.That(store.Snapshot.Definitions.Single(item => item.Id == "foreign").NextRunAt, Is.EqualTo(now.AddHours(-1)));
            Assert.That(store.SaveCount, Is.Zero);
            Assert.That(store.MutateCount, Is.EqualTo(2));
        }

        [Test]
        public async Task RuntimeHost_StopDuringAwaitedDueRun_DoesNotLaunchRemainingTasks()
        {
            var store = new InMemoryAutomationStore { Snapshot = ScopedState("windows", false, DateTime.Now.AddMinutes(-1)) };
            var runner = new ThrowingAutomationRunner
            {
                StartEntered = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously),
                StartCompletion = new TaskCompletionSource<AutomationRunSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner("windows", false) };
            using (var host = new AutomationRuntimeHost(store, runner, planner))
            {
                var tick = host.RunDueAutomationsAsync();
                try
                {
                    Assert.That(await Task.WhenAny(runner.StartEntered.Task, tick, Task.Delay(5000)), Is.SameAs(runner.StartEntered.Task));
                    host.Stop();
                }
                finally
                {
                    runner.StartCompletion.TrySetResult(new AutomationRunSnapshot { Id = "first-run", Status = AutomationRunStatus.Running });
                    await tick;
                }
                Assert.That(runner.AttemptedAutomationIds, Is.EqualTo(new[] { "own-coding" }));
            }
        }

        [Test]
        public async Task RuntimeHost_ExitPreparationPausesNewRunsAndCancelRestoresScheduler()
        {
            var store = new InMemoryAutomationStore { Snapshot = ScopedState("windows", false, DateTime.Now.AddMinutes(-1)) };
            var runner = new ThrowingAutomationRunner();
            var planner = new AutomationRuntimePlanner(new AutomationScheduleCalculator())
            { TargetScope = new AutomationTargetScopePlanner("windows", false) };
            using (var host = new AutomationRuntimeHost(store, runner, planner))
            {
                Assert.That(host.TryPauseForExit(), Is.True);
                await host.RunDueAutomationsAsync();
                Assert.That(runner.AttemptedAutomationIds, Is.Empty);
                host.ResumeAfterCancelledExit();
                await host.RunDueAutomationsAsync();
                Assert.That(runner.AttemptedAutomationIds, Is.EqualTo(new[] { "own-coding", "own-office" }));
            }
        }

        private static AutomationStateSnapshot ScopedState(string scope, bool unix, DateTime due)
        {
            var state = new AutomationStateSnapshot();
            foreach (var id in new[] { "own-coding", "own-office", "foreign", "wrong-path", "missing-scope" })
            {
                var office = id == "own-office";
                var definitionScope = id == "foreign" ? "other-target" : id == "missing-scope" ? null : scope;
                var path = unix ? "/srv/project" : @"C:\project";
                if (id == "wrong-path") path = unix ? @"C:\foreign-host" : "/foreign-host";
                state.Definitions.Add(new AutomationDefinitionSnapshot
                {
                    Id = id, Enabled = true, NextRunAt = due,
                    Schedule = new AutomationScheduleSnapshot { Kind = AutomationScheduleKind.EveryHours, Interval = 2, SkipMissedRuns = true },
                    Context = new AutomationContextSnapshot
                    {
                        InteractionMode = office ? AppInteractionMode.Office : AppInteractionMode.Coding,
                        RuntimeProfileId = office ? "unrelated-coding" : definitionScope,
                        OfficeRuntimeScopeId = office ? definitionScope : "unrelated-office",
                        WorkspacePath = path, OfficeRootPath = office ? path : null
                    }
                });
            }
            return state;
        }

        private sealed class InMemoryAutomationStore : IAutomationStore
        {
            public AutomationStateSnapshot Snapshot = new AutomationStateSnapshot();

            public AutomationStateSnapshot LoadSnapshot;

            public Exception LoadException;

            public int SaveCount;

            public int MutateCount;

            public AutomationStateSnapshot Load()
            {
                if (LoadException != null)
                {
                    throw LoadException;
                }

                return LoadSnapshot ?? Snapshot;
            }

            public void Save(AutomationStateSnapshot snapshot)
            {
                SaveCount++;
                Snapshot = snapshot;
            }

            public AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation)
            {
                MutateCount++;
                Snapshot = mutation(Snapshot) ?? throw new InvalidOperationException();
                return Snapshot;
            }
        }

        private sealed class ThrowingAutomationRunner : IAutomationRunner
        {
            public readonly List<string> AttemptedAutomationIds = new List<string>();
            public TaskCompletionSource<object> StartEntered;
            public TaskCompletionSource<AutomationRunSnapshot> StartCompletion;

            public Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request)
            {
                AttemptedAutomationIds.Add(request.AutomationId);
                if (StartCompletion != null)
                {
                    StartEntered.TrySetResult(null);
                    return StartCompletion.Task;
                }
                throw new InvalidOperationException("runner failed");
            }

            public void CancelRunning(string reason)
            {
            }

            public bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion)
            {
                return false;
            }
        }

        private sealed class PersistenceFailingAutomationRunner : IAutomationRunner
        {
            private readonly Exception _failure;

            public PersistenceFailingAutomationRunner(Exception failure)
            {
                _failure = failure;
            }

            public Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request)
            {
                throw _failure;
            }

            public void CancelRunning(string reason)
            {
            }

            public bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion)
            {
                return false;
            }
        }

        private sealed class CompletingAutomationRunner : IAutomationRunner
        {
            private readonly bool _completionResult;

            public CompletingAutomationRunner(bool completionResult)
            {
                _completionResult = completionResult;
            }

            public string ThreadId { get; private set; }

            public string TurnId { get; private set; }

            public AutomationRunCompletion Completion { get; private set; }

            public Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request)
            {
                throw new NotSupportedException();
            }

            public void CancelRunning(string reason)
            {
            }

            public bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion)
            {
                ThreadId = threadId;
                TurnId = turnId;
                Completion = completion;
                return _completionResult;
            }
        }
    }
}
