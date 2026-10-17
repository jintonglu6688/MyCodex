using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private static void AddOfficialHostSettings(IDictionary<string, object> payload, HostSettingsSnapshot settings)
        {
            var official = settings.Official;
            if (official == null)
            {
                return;
            }

            payload["official"] = new Dictionary<string, object>
            {
                { "modelReasoningSummary", official.ModelReasoningSummary },
                { "modelVerbosity", official.ModelVerbosity },
                { "serviceTier", official.ServiceTier },
                { "webSearchMode", official.WebSearchMode },
                { "webSearchContextSize", official.WebSearchContextSize },
                { "viewImageToolEnabled", official.ViewImageToolEnabled },
                { "toolOutputTokenLimit", official.ToolOutputTokenLimit },
                { "projectDocMaxBytes", official.ProjectDocMaxBytes }
            };
        }

        private static OfficialCodexSettingsSelection ParseOfficialHostSettings(IDictionary<string, object> payload)
        {
            if (!payload.ContainsKey("official"))
            {
                return null;
            }

            var section = GetRequiredSection(payload, "official");
            return new OfficialCodexSettingsSelection
            {
                ModelReasoningSummary = GetRequiredNullableString(section, "modelReasoningSummary"),
                ModelVerbosity = GetRequiredNullableString(section, "modelVerbosity"),
                ServiceTier = GetRequiredNullableString(section, "serviceTier"),
                WebSearchMode = GetRequiredNullableString(section, "webSearchMode"),
                WebSearchContextSize = GetRequiredNullableString(section, "webSearchContextSize"),
                ViewImageToolEnabled = GetRequiredNullableBoolean(section, "viewImageToolEnabled"),
                ToolOutputTokenLimit = GetRequiredNullableLong(section, "toolOutputTokenLimit"),
                ProjectDocMaxBytes = GetRequiredNullableLong(section, "projectDocMaxBytes")
            };
        }

        private static bool? GetRequiredNullableBoolean(IDictionary<string, object> section, string key)
        {
            var value = GetRequiredOfficialValue(section, key);
            if (value == null)
            {
                return null;
            }
            if (value is bool)
            {
                return (bool)value;
            }
            throw InvalidHostSettings(key + " must be a boolean or null.");
        }

        private static long? GetRequiredNullableLong(IDictionary<string, object> section, string key)
        {
            var value = GetRequiredOfficialValue(section, key);
            if (value == null)
            {
                return null;
            }
            if (value is int)
            {
                return (int)value;
            }
            if (value is long)
            {
                return (long)value;
            }
            throw InvalidHostSettings(key + " must be an integer or null.");
        }

        private static object GetRequiredOfficialValue(IDictionary<string, object> section, string key)
        {
            object value;
            if (!section.TryGetValue(key, out value))
            {
                throw InvalidHostSettings(key + " is required.");
            }
            return value;
        }
    }
}
