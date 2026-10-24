using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerCollaborationParameters
    {
        internal static void AddTo(IDictionary<string, object> target, AppServerCollaborationMode collaborationMode)
        {
            if (target == null ||
                collaborationMode == null ||
                string.IsNullOrWhiteSpace(collaborationMode.Mode) ||
                string.IsNullOrWhiteSpace(collaborationMode.ModelId))
            {
                return;
            }

            var settings = new Dictionary<string, object>
            {
                { "model", collaborationMode.ModelId },
                { "reasoning_effort", string.IsNullOrWhiteSpace(collaborationMode.ReasoningEffort) ? null : collaborationMode.ReasoningEffort },
                { "developer_instructions", string.IsNullOrWhiteSpace(collaborationMode.DeveloperInstructions) ? null : collaborationMode.DeveloperInstructions }
            };
            target["collaborationMode"] = new Dictionary<string, object>
            {
                { "mode", collaborationMode.Mode },
                { "settings", settings }
            };
        }
    }
}
