using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public static class AutomationThreadSessionService
    {
        public static Dictionary<string, string> BuildTitleMap(AutomationStateSnapshot snapshot)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var definitions = ThreadCreatingDefinitions(snapshot, true);
            foreach (var run in OrderedRuns(snapshot))
            {
                AutomationDefinitionSnapshot definition;
                if (!result.ContainsKey(run.ThreadId) && definitions.TryGetValue(run.AutomationId ?? string.Empty, out definition) &&
                    !string.IsNullOrWhiteSpace(definition.Name))
                    result[run.ThreadId] = definition.Name.Trim();
            }
            return result;
        }

        public static bool InitializeModelSettings(IEnumerable<ChatSession> sessions, AutomationStateSnapshot snapshot)
        {
            var definitions = ThreadCreatingDefinitions(snapshot);
            var runs = OrderedRuns(snapshot)
                .Where(run => !string.IsNullOrWhiteSpace(run.ActualModelId) && definitions.ContainsKey(run.AutomationId ?? string.Empty))
                .GroupBy(run => run.ThreadId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var session in sessions ?? Enumerable.Empty<ChatSession>())
            {
                AutomationRunSnapshot run;
                if (session == null || session.AutomationModelSettingsInitialized || string.IsNullOrWhiteSpace(session.ThreadId) ||
                    !runs.TryGetValue(session.ThreadId, out run)) continue;
                var context = definitions[run.AutomationId].Context;
                if (definitions[run.AutomationId].Kind != AutomationKind.Standalone &&
                    !string.IsNullOrWhiteSpace(context?.ThreadId)) continue;
                if (context != null && !string.IsNullOrWhiteSpace(context.RuntimeProfileId) &&
                    !string.IsNullOrWhiteSpace(session.RuntimeProfileId) &&
                    !string.Equals(context.RuntimeProfileId, session.RuntimeProfileId, StringComparison.OrdinalIgnoreCase)) continue;
                if (context != null && context.InteractionMode != session.Mode) continue;

                // A resolved default stays following defaults; existing manual overrides win.
                if (string.IsNullOrWhiteSpace(session.ModelId) && string.IsNullOrWhiteSpace(session.ReasoningEffort) &&
                    (!string.IsNullOrWhiteSpace(run.ConfiguredModelId) || !string.IsNullOrWhiteSpace(run.ConfiguredReasoningEffort)))
                {
                    session.ModelId = TextValueHelper.TrimToNull(run.ActualModelId);
                    session.ReasoningEffort = TextValueHelper.TrimToNull(run.ActualReasoningEffort);
                }
                session.AutomationModelSettingsInitialized = true;
                changed = true;
            }
            return changed;
        }

        private static Dictionary<string, AutomationDefinitionSnapshot> ThreadCreatingDefinitions(AutomationStateSnapshot snapshot, bool requireName = false)
        {
            return (snapshot?.Definitions ?? Enumerable.Empty<AutomationDefinitionSnapshot>())
                .Where(item => item != null && item.Kind != AutomationKind.Thread && !string.IsNullOrWhiteSpace(item.Id) &&
                    (!requireName || !string.IsNullOrWhiteSpace(item.Name)))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<AutomationRunSnapshot> OrderedRuns(AutomationStateSnapshot snapshot)
        {
            return (snapshot?.Runs ?? Enumerable.Empty<AutomationRunSnapshot>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ThreadId))
                .OrderByDescending(item => item.StartedAt ?? item.ScheduledFor ?? DateTime.MinValue);
        }
    }
}
