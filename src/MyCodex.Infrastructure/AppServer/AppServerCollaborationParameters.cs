using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerCollaborationParameters
    {
        internal static void AddTo(IDictionary<string, object> target, AppServerCollaborationMode mode)
        {
            if (target == null || mode == null || string.IsNullOrWhiteSpace(mode.Mode) || string.IsNullOrWhiteSpace(mode.ModelId)) return;
            target["collaborationMode"] = new Dictionary<string, object>
            {
                { "mode", mode.Mode },
                { "settings", new Dictionary<string, object>
                    {
                        { "model", mode.ModelId },
                        { "reasoning_effort", string.IsNullOrWhiteSpace(mode.ReasoningEffort) ? null : mode.ReasoningEffort },
                        { "developer_instructions", string.IsNullOrWhiteSpace(mode.DeveloperInstructions) ? null : mode.DeveloperInstructions }
                    }
                }
            };
        }
    }
}
