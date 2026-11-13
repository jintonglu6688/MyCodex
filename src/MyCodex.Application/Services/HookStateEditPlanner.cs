using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    /// <summary>Builds user-config edits for hooks.state, matching the official CLI's trust write.</summary>
    public static class HookStateEditPlanner
    {
        public static bool NeedsReview(AppServerHookInfo hook)
        {
            // Unknown future statuses are treated as needing review, never as trusted.
            return hook != null && !hook.IsManaged &&
                !string.Equals(hook.TrustStatus, "trusted", StringComparison.Ordinal) &&
                !string.Equals(hook.TrustStatus, "managed", StringComparison.Ordinal);
        }

        public static bool CanModify(AppServerHookInfo hook)
        {
            return hook != null && !hook.IsManaged && !string.IsNullOrWhiteSpace(hook.Key);
        }

        public static List<AppServerConfigWriteEdit> BuildTrust(IEnumerable<AppServerHookInfo> hooks)
        {
            // The server splits a plugin's composite hash into trusted_hash/trusted_content_hash itself.
            return BuildStateUpsert(hooks.Where(hook => !string.IsNullOrWhiteSpace(hook.CurrentHash)),
                hook => new Dictionary<string, object> { { "trusted_hash", hook.CurrentHash } });
        }

        public static List<AppServerConfigWriteEdit> BuildEnabled(AppServerHookInfo hook, bool enabled)
        {
            return BuildStateUpsert(new[] { hook },
                value => new Dictionary<string, object> { { "enabled", enabled } });
        }

        public static List<AppServerConfigWriteEdit> BuildRevoke(AppServerHookInfo hook)
        {
            if (!CanModify(hook))
            {
                return new List<AppServerConfigWriteEdit>();
            }

            var prefix = "hooks.state." + QuoteKeySegment(hook.Key) + ".";
            return new List<AppServerConfigWriteEdit>
            {
                AppServerConfigWriteEdit.CreateReplace(prefix + "trusted_hash", null),
                AppServerConfigWriteEdit.CreateReplace(prefix + "trusted_content_hash", null)
            };
        }

        /// <summary>Hook keys embed paths and colons, so they go in a quoted keyPath segment.</summary>
        public static string QuoteKeySegment(string key)
        {
            return "\"" + (key ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static List<AppServerConfigWriteEdit> BuildStateUpsert(
            IEnumerable<AppServerHookInfo> hooks,
            Func<AppServerHookInfo, Dictionary<string, object>> buildState)
        {
            var value = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var hook in hooks.Where(CanModify))
            {
                value[hook.Key] = buildState(hook);
            }

            return value.Count == 0
                ? new List<AppServerConfigWriteEdit>()
                : new List<AppServerConfigWriteEdit>
                {
                    new AppServerConfigWriteEdit { KeyPath = "hooks.state", MergeStrategy = "upsert", Value = value }
                };
        }
    }
}
