using System;
using System.Globalization;

namespace MyCodex.Shared.Localization
{
    /// <summary>
    /// Display names for hook events. The official name stays the hooks.json key; English shows it as is, and other
    /// languages show a translation. An event without a translation falls back to its official name.
    /// </summary>
    public static class HookEventNames
    {
        /// <summary>The PascalCase key used in hooks.json, from the protocol's camelCase name (sessionStart → SessionStart).</summary>
        public static string GetOfficialName(string eventName)
        {
            return string.IsNullOrEmpty(eventName) ? eventName : char.ToUpperInvariant(eventName[0]) + eventName.Substring(1);
        }

        public static string Describe(string eventName)
        {
            var official = GetOfficialName(eventName);
            switch (official)
            {
                case "PreToolUse":
                    return SharedLocalization.Text("HookEvent.PreToolUse", official);
                case "PostToolUse":
                    return SharedLocalization.Text("HookEvent.PostToolUse", official);
                case "PermissionRequest":
                    return SharedLocalization.Text("HookEvent.PermissionRequest", official);
                case "UserPromptSubmit":
                    return SharedLocalization.Text("HookEvent.UserPromptSubmit", official);
                case "SessionStart":
                    return SharedLocalization.Text("HookEvent.SessionStart", official);
                case "SessionEnd":
                    return SharedLocalization.Text("HookEvent.SessionEnd", official);
                case "Stop":
                    return SharedLocalization.Text("HookEvent.Stop", official);
                case "Interrupt":
                    return SharedLocalization.Text("HookEvent.Interrupt", official);
                case "SubagentStart":
                    return SharedLocalization.Text("HookEvent.SubagentStart", official);
                case "SubagentStop":
                    return SharedLocalization.Text("HookEvent.SubagentStop", official);
                case "PreCompact":
                    return SharedLocalization.Text("HookEvent.PreCompact", official);
                case "PostCompact":
                    return SharedLocalization.Text("HookEvent.PostCompact", official);
                case "PreModelRequest":
                    return SharedLocalization.Text("HookEvent.PreModelRequest", official);
                case "PostModelRequest":
                    return SharedLocalization.Text("HookEvent.PostModelRequest", official);
                default:
                    return official;
            }
        }

        /// <summary>The translation followed by the official name, for places where the user matches it against files.</summary>
        public static string DescribeWithOfficialName(string eventName)
        {
            var name = Describe(eventName);
            var official = GetOfficialName(eventName);
            return string.Equals(name, official, StringComparison.Ordinal)
                ? official
                : string.Format(CultureInfo.CurrentCulture, SharedLocalization.Text("HookEvent.WithOfficialName", "{0} ({1})"), name, official);
        }
    }
}
