using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public sealed class AutomationResultClassifier
    {
        public AutomationRunSnapshot ApplyCompletion(
            AutomationDefinitionSnapshot definition,
            AutomationRunSnapshot run,
            AutomationRunCompletion completion)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }

            run.Status = completion.Status;
            run.CompletedAt = completion.CompletedAt == default(DateTime) ? DateTime.Now : completion.CompletedAt;
            if (!string.IsNullOrWhiteSpace(completion.TurnId))
            {
                run.TurnId = completion.TurnId;
            }

            run.Summary = TextValueHelper.TrimToNull(completion.Summary);
            run.ErrorMessage = TextValueHelper.TrimToNull(completion.ErrorMessage);
            run.GeneratedArtifacts = CopyList(completion.GeneratedArtifacts);
            run.ChangedFiles = CopyList(completion.ChangedFiles);

            var requiresAttention = RequiresAttention(completion.Status);
            var autoArchiveNoFindings =
                definition != null &&
                definition.AllowNoFindingsAutoArchive &&
                completion.Status == AutomationRunStatus.Completed &&
                IsNoFindingsSummary(completion.Summary);

            run.HasFindings = completion.Status == AutomationRunStatus.Completed && !autoArchiveNoFindings;
            run.NeedsAttention = requiresAttention || (completion.Status == AutomationRunStatus.Completed && !autoArchiveNoFindings);
            run.Unread = run.NeedsAttention;
            run.Archived = !run.NeedsAttention;
            return run;
        }

        private static bool RequiresAttention(AutomationRunStatus status)
        {
            return status == AutomationRunStatus.Failed ||
                   status == AutomationRunStatus.WaitingForApproval ||
                   status == AutomationRunStatus.WaitingForInput ||
                   status == AutomationRunStatus.Canceled;
        }

        private static bool IsNoFindingsSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return false;
            }

            return summary.IndexOf("no important", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("no findings", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("nothing to report", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("无重要", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("无发现", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("没有重要", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> CopyList(IReadOnlyList<string> values)
        {
            var result = new List<string>();
            if (values == null)
            {
                return result;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    result.Add(values[i].Trim());
                }
            }

            return result;
        }
    }
}
