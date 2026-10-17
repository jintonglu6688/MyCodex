using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationRuntimePlanner
    {
        private readonly AutomationScheduleCalculator _scheduleCalculator;

        public AutomationTargetScopePlanner TargetScope { get; set; }

        public AutomationRuntimePlanner(AutomationScheduleCalculator scheduleCalculator)
        {
            _scheduleCalculator = scheduleCalculator ?? throw new ArgumentNullException(nameof(scheduleCalculator));
        }

        public IReadOnlyList<string> BuildDueRunIds(AutomationStateSnapshot snapshot, DateTime now)
        {
            snapshot = snapshot ?? new AutomationStateSnapshot();
            var runningIds = new HashSet<string>(
                (snapshot.Runs ?? new List<AutomationRunSnapshot>())
                    .Where(IsRunning)
                    .Select(item => item.AutomationId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            return (snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>())
                .Where(item => item != null)
                .Where(item => TargetScope == null || TargetScope.Matches(item))
                .Where(item => _scheduleCalculator.IsDue(item, now))
                .Where(item => !runningIds.Contains(item.Id ?? string.Empty))
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        public int SkipMissedRunsOnStartup(AutomationStateSnapshot snapshot, DateTime now, Func<string> newId)
        {
            if (snapshot == null)
            {
                return 0;
            }

            snapshot.Definitions = snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>();
            snapshot.Runs = snapshot.Runs ?? new List<AutomationRunSnapshot>();
            newId = newId ?? (() => Guid.NewGuid().ToString("N"));

            var skipped = 0;
            foreach (var definition in snapshot.Definitions)
            {
                if (definition == null ||
                    TargetScope != null && !TargetScope.Matches(definition) ||
                    !definition.Enabled ||
                    definition.Schedule == null ||
                    !definition.Schedule.SkipMissedRuns ||
                    !definition.NextRunAt.HasValue ||
                    definition.NextRunAt.Value >= now)
                {
                    continue;
                }

                snapshot.Runs.Add(new AutomationRunSnapshot
                {
                    Id = newId(),
                    AutomationId = definition.Id,
                    Status = AutomationRunStatus.Skipped,
                    TriggerReason = AutomationTriggerReason.StartupMissedRun,
                    ScheduledFor = definition.NextRunAt,
                    StartedAt = now,
                    CompletedAt = now,
                    Summary = SharedLocalization.Text(
                        "AutomationRuntimePlanner.Summary.SkippedMissedRun",
                        "Skipped missed automation run."),
                    Archived = true
                });
                definition.NextRunAt = CalculateNextAfterStartupSkip(definition, now);
                skipped++;
            }

            return skipped;
        }

        private DateTime? CalculateNextAfterStartupSkip(AutomationDefinitionSnapshot definition, DateTime now)
        {
            return definition.Enabled
                ? _scheduleCalculator.CalculateNextRun(definition, now)
                : null;
        }

        private static bool IsRunning(AutomationRunSnapshot run)
        {
            return run != null &&
                   (run.Status == AutomationRunStatus.Running ||
                    run.Status == AutomationRunStatus.Queued ||
                    run.Status == AutomationRunStatus.WaitingForApproval ||
                    run.Status == AutomationRunStatus.WaitingForInput);
        }
    }
}
