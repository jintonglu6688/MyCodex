using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationRunner : IAutomationRunner
    {
        private readonly IAutomationStore _store;
        private readonly IAppServerThreadClient _threadClient;
        private readonly AutomationResultClassifier _classifier;
        private readonly AutomationScheduleCalculator _scheduleCalculator;
        private readonly AutomationInputItemPlanner _inputItemPlanner;
        private readonly AutomationCollaborationModePlanner _collaborationModePlanner;
        private readonly Func<DateTime> _now;
        private readonly Func<string> _newId;
        private readonly HashSet<string> _startedRunIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IAutomationModelResolver ModelResolver { get; set; }

        public AutomationTargetScopePlanner TargetScope { get; set; }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier)
            : this(store, threadClient, classifier, new AutomationScheduleCalculator(), () => DateTime.Now, () => Guid.NewGuid().ToString("N"))
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            Func<DateTime> now,
            Func<string> newId)
            : this(store, threadClient, classifier, new AutomationScheduleCalculator(), now, newId)
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            AutomationCollaborationModePlanner collaborationModePlanner)
            : this(store, threadClient, classifier, new AutomationScheduleCalculator(), new AutomationInputItemPlanner(), collaborationModePlanner, () => DateTime.Now, () => Guid.NewGuid().ToString("N"))
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            AutomationCollaborationModePlanner collaborationModePlanner,
            Func<DateTime> now,
            Func<string> newId)
            : this(store, threadClient, classifier, new AutomationScheduleCalculator(), new AutomationInputItemPlanner(), collaborationModePlanner, now, newId)
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            AutomationScheduleCalculator scheduleCalculator,
            Func<DateTime> now,
            Func<string> newId)
            : this(store, threadClient, classifier, scheduleCalculator, new AutomationInputItemPlanner(), now, newId)
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            AutomationScheduleCalculator scheduleCalculator,
            AutomationInputItemPlanner inputItemPlanner,
            Func<DateTime> now,
            Func<string> newId)
            : this(store, threadClient, classifier, scheduleCalculator, inputItemPlanner, new AutomationCollaborationModePlanner(), now, newId)
        {
        }

        public AutomationRunner(
            IAutomationStore store,
            IAppServerThreadClient threadClient,
            AutomationResultClassifier classifier,
            AutomationScheduleCalculator scheduleCalculator,
            AutomationInputItemPlanner inputItemPlanner,
            AutomationCollaborationModePlanner collaborationModePlanner,
            Func<DateTime> now,
            Func<string> newId)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            _scheduleCalculator = scheduleCalculator ?? throw new ArgumentNullException(nameof(scheduleCalculator));
            _inputItemPlanner = inputItemPlanner ?? throw new ArgumentNullException(nameof(inputItemPlanner));
            _collaborationModePlanner = collaborationModePlanner ?? throw new ArgumentNullException(nameof(collaborationModePlanner));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _newId = newId ?? throw new ArgumentNullException(nameof(newId));
        }

        public async Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                return await StartForAuthenticationGateAsync(request).ConfigureAwait(false);
            }
        }

        private async Task<AutomationRunSnapshot> StartForAuthenticationGateAsync(AutomationRunRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.AutomationId))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationRunner.Error.AutomationIdRequired", "Automation ID cannot be empty."));
            }

            AutomationDefinitionSnapshot definition = null;
            AutomationRunSnapshot run = null;
            string workspacePath = null;
            string threadId = null;
            var shouldCreateThread = false;
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                definition = FindDefinition(snapshot, request.AutomationId);
                TargetScope?.Validate(definition);
                if (snapshot.Runs.Any(item => item != null &&
                    string.Equals(item.AutomationId, definition.Id, StringComparison.OrdinalIgnoreCase) &&
                    (item.Status == AutomationRunStatus.Running || item.Status == AutomationRunStatus.Queued ||
                        item.Status == AutomationRunStatus.WaitingForApproval || item.Status == AutomationRunStatus.WaitingForInput)))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "Automation.Target.AlreadyRunning", "This automation is already running."));
                }
                workspacePath = ResolveWorkspacePath(definition);
                threadId = definition.Context != null ? definition.Context.ThreadId : null;
                shouldCreateThread = definition.Kind == AutomationKind.Standalone || string.IsNullOrWhiteSpace(threadId);
                run = new AutomationRunSnapshot
                {
                    Id = _newId(),
                    AutomationId = definition.Id,
                    Status = AutomationRunStatus.Running,
                    TriggerReason = request.TriggerReason,
                    ScheduledFor = request.ScheduledFor,
                    StartedAt = _now(),
                    ThreadId = shouldCreateThread ? null : threadId,
                    WorkspaceId = definition.Context != null ? definition.Context.WorkspaceId : null,
                    WorkspacePath = workspacePath
                };
                snapshot.Runs.Add(run);
                return snapshot;
            });

            lock (_startedRunIds)
            {
                _startedRunIds.Add(run.Id);
            }

            try
            {
                var inputPlan = _inputItemPlanner.Build(definition);
                if (!inputPlan.Succeeded)
                {
                    throw new InvalidOperationException(inputPlan.ErrorMessage);
                }

                var modelResolution = await ResolveModelAsync(definition).ConfigureAwait(false);
                var currentRun = ReadRun(run.Id);
                if (currentRun == null || currentRun.Status != AutomationRunStatus.Running) return currentRun ?? run;
                ApplyModelResolution(run, modelResolution);
                MutateRun(run.Id, recordedRun => ApplyModelResolution(recordedRun, modelResolution));

                if (shouldCreateThread)
                {
                    threadId = await _threadClient.CreateThreadAsync(
                        workspacePath,
                        ResolveApprovalMode(definition),
                        modelResolution.ModelId,
                        modelResolution.ReasoningEffort).ConfigureAwait(false);
                    run.ThreadId = threadId;
                    MutateRun(run.Id, recordedRun => recordedRun.ThreadId = threadId);
                    await TrySetThreadNameAsync(threadId, definition.Name).ConfigureAwait(false);
                }

                var collaborationMode = _collaborationModePlanner.Build(definition);

                currentRun = ReadRun(run.Id);
                if (currentRun == null || currentRun.Status != AutomationRunStatus.Running) return currentRun ?? run;

                await _threadClient.SendUserMessageAsync(
                    threadId,
                    workspacePath,
                    inputPlan.InputItems,
                    ResolveApprovalMode(definition),
                    modelResolution.ModelId,
                    modelResolution.ReasoningEffort,
                    collaborationMode).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MarkRunFailedAfterStartError(run.Id, ex);
                ForgetStartedRun(run.Id);
                throw;
            }

            return run;
        }

        public void CancelRunning(string reason)
        {
            var canceledIds = new List<string>();
            var completedAt = _now();
            var summary = string.IsNullOrWhiteSpace(reason)
                ? SharedLocalization.Text("AutomationRunner.Status.Canceled", "Automation canceled.")
                : reason.Trim();
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                foreach (var run in snapshot.Runs.Where(item =>
                    item != null && item.Status == AutomationRunStatus.Running && IsOwnedRun(snapshot, item)))
                {
                    run.Status = AutomationRunStatus.Canceled;
                    run.CompletedAt = completedAt;
                    run.Summary = summary;
                    canceledIds.Add(run.Id);

                    var definition = snapshot.Definitions.FirstOrDefault(item =>
                        item != null &&
                        string.Equals(item.Id, run.AutomationId, StringComparison.OrdinalIgnoreCase));
                    if (definition != null)
                    {
                        definition.LastRunAt = completedAt;
                        definition.NextRunAt = definition.Enabled
                            ? _scheduleCalculator.CalculateNextRun(definition, completedAt)
                            : null;
                    }
                }

                return snapshot;
            });
            foreach (var id in canceledIds) ForgetStartedRun(id);
        }

        private async Task<AutomationModelResolution> ResolveModelAsync(AutomationDefinitionSnapshot definition)
        {
            if (ModelResolver != null)
            {
                var resolved = await ModelResolver.ResolveAsync(definition).ConfigureAwait(false);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            return new AutomationModelResolution
            {
                ConfiguredModelId = TrimToNull(definition != null ? definition.ModelId : null),
                ConfiguredReasoningEffort = TrimToNull(definition != null ? definition.ReasoningEffort : null),
                ModelId = TrimToNull(definition != null ? definition.ModelId : null),
                ReasoningEffort = TrimToNull(definition != null ? definition.ReasoningEffort : null)
            };
        }

        private static void ApplyModelResolution(AutomationRunSnapshot run, AutomationModelResolution modelResolution)
        {
            if (run == null || modelResolution == null)
            {
                return;
            }

            run.ConfiguredModelId = TrimToNull(modelResolution.ConfiguredModelId);
            run.ConfiguredReasoningEffort = TrimToNull(modelResolution.ConfiguredReasoningEffort);
            run.ActualModelId = TrimToNull(modelResolution.ModelId);
            run.ActualReasoningEffort = TrimToNull(modelResolution.ReasoningEffort);
            run.ModelResolutionMessage = TrimToNull(modelResolution.Message);
        }

        public bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion)
        {
            if (string.IsNullOrWhiteSpace(threadId) || completion == null)
            {
                return false;
            }

            var completed = false;
            string completedRunId = null;
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var run = snapshot.Runs
                    .Where(item => item != null &&
                                   item.Status == AutomationRunStatus.Running &&
                                   IsOwnedRun(snapshot, item) &&
                                   string.Equals(item.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.StartedAt ?? DateTime.MinValue)
                    .FirstOrDefault();
                if (run == null)
                {
                    return snapshot;
                }

                var definition = snapshot.Definitions.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.Id, run.AutomationId, StringComparison.OrdinalIgnoreCase));
                completion.TurnId = string.IsNullOrWhiteSpace(completion.TurnId) ? turnId : completion.TurnId;
                _classifier.ApplyCompletion(definition, run, completion);
                if (definition != null)
                {
                    definition.LastRunAt = run.CompletedAt;
                    definition.NextRunAt = definition.Enabled
                        ? _scheduleCalculator.CalculateNextRun(definition, run.CompletedAt ?? _now())
                        : null;
                }

                completed = true;
                completedRunId = run.Id;
                return snapshot;
            });
            if (completed) ForgetStartedRun(completedRunId);
            return completed;
        }

        private void MarkRunFailedAfterStartError(string runId, Exception exception)
        {
            MutateRun(runId, (snapshot, run) =>
            {
                if (run.Status != AutomationRunStatus.Running) return;
                var definition = snapshot.Definitions.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.Id, run.AutomationId, StringComparison.OrdinalIgnoreCase));
                var completedAt = _now();
                var message = exception == null ? null : exception.Message;
                _classifier.ApplyCompletion(definition, run, new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.Failed,
                    CompletedAt = completedAt,
                    Summary = message,
                    ErrorMessage = message
                });
                if (definition != null)
                {
                    definition.LastRunAt = run.CompletedAt;
                    definition.NextRunAt = definition.Enabled
                        ? _scheduleCalculator.CalculateNextRun(definition, run.CompletedAt ?? completedAt)
                        : null;
                }
            });
        }

        private void MutateRun(string runId, Action<AutomationRunSnapshot> mutation)
        {
            MutateRun(runId, (snapshot, run) => mutation(run));
        }

        private void MutateRun(string runId, Action<AutomationStateSnapshot, AutomationRunSnapshot> mutation)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var run = snapshot.Runs.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.Id, runId, StringComparison.OrdinalIgnoreCase));
                if (run != null)
                {
                    mutation(snapshot, run);
                }

                return snapshot;
            });
        }

        private static AutomationStateSnapshot Normalize(AutomationStateSnapshot snapshot)
        {
            snapshot = snapshot ?? new AutomationStateSnapshot();
            snapshot.Definitions = snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>();
            snapshot.Runs = snapshot.Runs ?? new List<AutomationRunSnapshot>();
            return snapshot;
        }

        private AutomationRunSnapshot ReadRun(string runId)
        {
            var snapshot = _store.Load();
            return snapshot?.Runs?.FirstOrDefault(item => item != null &&
                string.Equals(item.Id, runId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsOwnedRun(AutomationStateSnapshot snapshot, AutomationRunSnapshot run)
        {
            if (TargetScope == null) return true;
            var definition = snapshot.Definitions.FirstOrDefault(item => item != null &&
                string.Equals(item.Id, run.AutomationId, StringComparison.OrdinalIgnoreCase));
            if (definition != null) return TargetScope.Matches(definition);
            lock (_startedRunIds)
            {
                return _startedRunIds.Contains(run.Id);
            }
        }

        private void ForgetStartedRun(string id)
        {
            lock (_startedRunIds) _startedRunIds.Remove(id);
        }

        private static AutomationDefinitionSnapshot FindDefinition(AutomationStateSnapshot snapshot, string automationId)
        {
            var definition = snapshot.Definitions.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.Id, automationId, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text("AutomationRunner.Error.NotFound", "Automation was not found."));
            }

            return definition;
        }

        private static string ResolveWorkspacePath(AutomationDefinitionSnapshot definition)
        {
            var workspacePath = definition != null && definition.Context != null
                ? definition.Context.WorkspacePath
                : null;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new InvalidOperationException(SharedLocalization.Text("AutomationRunner.Error.WorkspacePathRequired", "Automation workspace path cannot be empty."));
            }

            return workspacePath.Trim();
        }

        private static ApprovalMode? ResolveApprovalMode(AutomationDefinitionSnapshot definition)
        {
            if (definition == null)
            {
                return ApprovalMode.ReadOnly;
            }

            return definition.ApprovalMode ?? ApprovalMode.ReadOnly;
        }

        private async Task TrySetThreadNameAsync(string threadId, string name)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                await _threadClient.SetThreadNameAsync(threadId, name.Trim()).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static string TrimToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
