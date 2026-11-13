using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Hook keys are "&lt;source&gt;:&lt;event&gt;:&lt;group&gt;:&lt;handler&gt;"; the source part may itself contain colons.</summary>
        public static bool TryParseKey(string key, out string prefix, out int groupIndex, out int handlerIndex)
        {
            prefix = null;
            groupIndex = handlerIndex = -1;
            var parts = (key ?? string.Empty).Split(':');
            if (parts.Length < 4 ||
                !int.TryParse(parts[parts.Length - 2], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex) ||
                !int.TryParse(parts[parts.Length - 1], NumberStyles.None, CultureInfo.InvariantCulture, out handlerIndex))
            {
                return false;
            }

            prefix = string.Join(":", parts, 0, parts.Length - 2);
            return true;
        }

        /// <summary>
        /// Hook state is keyed by position, so removing a handler moves every later handler of that event in that
        /// file onto its predecessor's key. Rewrites their known state under the new keys (disabled stays disabled,
        /// trusted stays trusted for its current hash) and drops keys nothing occupies any more.
        /// </summary>
        public static List<AppServerConfigWriteEdit> BuildRemapAfterRemoval(AppServerHookInfo removed, IEnumerable<AppServerHookInfo> listed)
        {
            string prefix;
            int group;
            int handler;
            var edits = new List<AppServerConfigWriteEdit>();
            if (removed == null || !TryParseKey(removed.Key, out prefix, out group, out handler))
            {
                return edits;
            }

            var siblings = new List<Tuple<AppServerHookInfo, int, int>>();
            foreach (var hook in listed ?? Enumerable.Empty<AppServerHookInfo>())
            {
                string siblingPrefix;
                int siblingGroup;
                int siblingHandler;
                if (hook != null && !string.Equals(hook.Key, removed.Key, StringComparison.Ordinal) &&
                    TryParseKey(hook.Key, out siblingPrefix, out siblingGroup, out siblingHandler) &&
                    string.Equals(siblingPrefix, prefix, StringComparison.Ordinal))
                {
                    siblings.Add(Tuple.Create(hook, siblingGroup, siblingHandler));
                }
            }

            // A group that keeps other handlers only renumbers them; an emptied group is removed and later groups move up.
            var groupKept = siblings.Any(sibling => sibling.Item2 == group);
            var oldKeys = new HashSet<string>(StringComparer.Ordinal) { removed.Key };
            var newKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sibling in siblings.Where(sibling => groupKept
                ? sibling.Item2 == group && sibling.Item3 > handler
                : sibling.Item2 > group))
            {
                var hook = sibling.Item1;
                var newKey = prefix + ":" +
                    (groupKept ? group : sibling.Item2 - 1).ToString(CultureInfo.InvariantCulture) + ":" +
                    (groupKept ? sibling.Item3 - 1 : sibling.Item3).ToString(CultureInfo.InvariantCulture);
                oldKeys.Add(hook.Key);
                newKeys.Add(newKey);
                var state = new Dictionary<string, object>();
                if (!hook.Enabled)
                {
                    state["enabled"] = false;
                }

                if (string.Equals(hook.TrustStatus, "trusted", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(hook.CurrentHash))
                {
                    state["trusted_hash"] = hook.CurrentHash;
                }

                edits.Add(AppServerConfigWriteEdit.CreateReplace("hooks.state." + QuoteKeySegment(newKey), state.Count == 0 ? null : state));
            }

            edits.AddRange(oldKeys.Where(key => !newKeys.Contains(key))
                .Select(key => AppServerConfigWriteEdit.CreateReplace("hooks.state." + QuoteKeySegment(key), null)));
            return edits;
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
