using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class AutomationRunCompletionBuilder
    {
        internal static AutomationRunCompletion Build(
            AutomationRunStatus status,
            string content,
            string errorMessage,
            TurnProgressSummaryState state)
        {
            return new AutomationRunCompletion
            {
                Status = status,
                CompletedAt = DateTime.Now,
                TurnId = state == null ? null : state.TurnId,
                Summary = content,
                ErrorMessage = errorMessage,
                GeneratedArtifacts = new List<string>(),
                ChangedFiles = CopyChangedFiles(state)
            };
        }

        private static IReadOnlyList<string> CopyChangedFiles(TurnProgressSummaryState state)
        {
            var result = new List<string>();
            if (state == null || state.ChangedFiles == null)
            {
                return result;
            }

            for (var i = 0; i < state.ChangedFiles.Count; i++)
            {
                var path = state.ChangedFiles[i];
                if (!string.IsNullOrWhiteSpace(path))
                {
                    result.Add(path.Trim());
                }
            }

            return result;
        }
    }
}
