using System;
using System.Collections.Generic;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ActivityTimelineState
    {
        private const int MaxItems = 200;
        internal const string KindThinking = "thinking";
        internal const string KindPlan = "plan";
        internal const string KindTool = "tool";
        internal const string KindNetwork = "network";
        internal const string KindCommand = "command";
        internal const string KindFile = "file";
        internal const string KindNotice = "notice";
        internal const string KindError = "error";
        internal const string KindTurn = "turn";
        internal const string KindApproval = "approval";
        internal const string KindAccount = "account";
        internal const string KindProtocolNotice = "protocol_notice";
        internal const string KindProtocolWarning = "protocol_warning";

        private readonly List<ActivityTimelineItem> _items;
        private readonly HashSet<string> _seenKeys;

        public ActivityTimelineState()
        {
            _items = new List<ActivityTimelineItem>();
            _seenKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        public void Clear()
        {
            _items.Clear();
            _seenKeys.Clear();
        }

        public bool HasItems
        {
            get { return _items.Count > 0; }
        }

        public bool ApplyEvent(AppServerEventArgs e)
        {
            return ApplyEvent(e, null);
        }

        public bool ApplyEvent(AppServerEventArgs e, AppConfigSnapshot config)
        {
            if (e == null)
            {
                return false;
            }

            switch ((e.EventType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "turn_started":
                    if (config != null && config.ClearActivityOnNewTurn)
                    {
                        Clear();
                    }

                    return AppendItem(BuildKey(e, KindTurn, "started"), KindTurn, GetTurnLabel(), UiLocalization.Text("MainForm.ActivityTimeline.NewTurn", "Starting a new conversation turn"));
                case "turn_completed":
                    return AppendTurnSummaryItem(e, e.Content);
                case "turn_interrupted":
                    return AppendTurnSummaryItem(e, e.Content);
                case "turn_retrying":
                case "context_compaction_completed":
                case "thread_started":
                case "thread_status_changed":
                case "thread_goal_updated":
                case "thread_goal_cleared":
                    return AppendSimpleItem(e, KindNotice, GetNoticeLabel(), e.Content);
                case "turn_plan_updated":
                    return AppendPlanItems(e);
                case "reasoning_completed":
                    return AppendReasoningItem(e);
                case "file_change_completed":
                    return AppendFileChangeItem(e);
                case "skills_changed":
                case "external_agent_config_import_completed":
                    return AppendSimpleItem(e, KindFile, GetFilesLabel(), e.Content);
                case "mcp_tool_completed":
                    return AppendToolItem(e);
                case "image_generation_completed":
                    return AppendSimpleItem(e, KindTool, GetToolLabel(), e.Content);
                case "subagent_activity":
                    return AppendSimpleItem(e, KindNotice, GetNoticeLabel(), e.Content);
                case "network_activity_started":
                case "network_activity_completed":
                    return AppendSimpleItem(e, KindNetwork, GetNetworkLabel(), e.Content);
                case "command_completed":
                    return AppendCommandItem(e);
                case "turn_failed":
                case "error":
                case "app_server_disconnected":
                    return AppendSimpleItem(e, KindError, GetErrorLabel(), e.Content);
                case "approval_requested":
                case "approval_resolved":
                    return AppendSimpleItem(e, KindApproval, GetApprovalLabel(), e.Content);
                case "auth_changed":
                case "auth_login_completed":
                    return AppendSimpleItem(e, KindAccount, GetAccountLabel(), e.Content);
                case "protocol_notice":
                    return AppendSimpleItem(e, KindProtocolNotice, GetProtocolNoticeLabel(), e.Content);
                case "protocol_warning":
                    return AppendSimpleItem(e, KindProtocolWarning, GetProtocolWarningLabel(), e.Content);
                default:
                    return false;
            }
        }

        public bool UpsertTurnSummary(string turnId, string text)
        {
            return UpsertItem(BuildTurnSummaryKey(turnId), KindNotice, GetNoticeLabel(), CollapseTextToSingleLine(text));
        }

        public string[] BuildDisplayItems(string emptyText)
        {
            return BuildDisplayItems(emptyText, null);
        }

        public string[] BuildDisplayItems(string emptyText, AppConfigSnapshot config)
        {
            var visibleItems = new List<ActivityTimelineItem>();
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && IsVisible(item.Kind, config))
                {
                    visibleItems.Add(item);
                }
            }

            if (visibleItems.Count == 0)
            {
                return string.IsNullOrWhiteSpace(emptyText)
                    ? new string[0]
                    : new[] { emptyText.Trim() };
            }

            var result = new string[visibleItems.Count];
            for (var i = 0; i < visibleItems.Count; i++)
            {
                result[i] = visibleItems[i].BuildDisplayText();
            }

            return result;
        }

        private bool AppendPlanItems(AppServerEventArgs e)
        {
            if (e.PlanSteps == null || e.PlanSteps.Count == 0)
            {
                return false;
            }

            var changed = false;
            for (var i = 0; i < e.PlanSteps.Count; i++)
            {
                var step = e.PlanSteps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.Step))
                {
                    continue;
                }

                var status = NormalizePlanStatus(step.Status);
                if (status.Length == 0)
                {
                    continue;
                }

                var text = MapPlanStatusText(status) +
                           UiLocalization.Text("MainForm.TurnSummary.Plan.Separator", ":") +
                           " " +
                           step.Step.Trim();
                changed |= AppendItem(
                    BuildKey(e, "plan", status + ":" + step.Step.Trim()),
                    KindPlan,
                    GetPlanLabel(),
                    text);
            }

            return changed;
        }

        private bool AppendReasoningItem(AppServerEventArgs e)
        {
            var text = NormalizeReasoningSummaryForDisplay(e.Content);
            if (IsPlaceholderReasoningSummary(text))
            {
                return false;
            }

            return AppendItem(BuildKey(e, "reasoning", text), KindThinking, GetThinkingLabel(), CollapseTextToSingleLine(text));
        }

        private bool AppendFileChangeItem(AppServerEventArgs e)
        {
            if (e.ChangedFiles == null || e.ChangedFiles.Count == 0)
            {
                var details = CollapseTextToSingleLine(e.Details);
                if (string.IsNullOrWhiteSpace(details))
                {
                    details = CollapseTextToSingleLine(e.Content);
                }

                return AppendSimpleItem(e, KindFile, GetFilesLabel(), details);
            }

            var builder = new StringBuilder();
            builder.Append(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.Prefix", "Modified "));
            builder.Append(e.ChangedFiles.Count);
            builder.Append(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.Suffix", " file(s)"));

            var displayCount = Math.Min(3, e.ChangedFiles.Count);
            if (displayCount > 0)
            {
                builder.Append(UiLocalization.Text("MainForm.ActivityTimeline.FileListSeparator", ": "));
                for (var i = 0; i < displayCount; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(e.ChangedFiles[i]);
                }
            }

            if (e.ChangedFiles.Count > displayCount)
            {
                builder.Append(", ");
                builder.Append(string.Format(
                    UiLocalization.Text("MainForm.ActivityTimeline.MoreFiles.Template", "and {0} more"),
                    e.ChangedFiles.Count - displayCount));
            }

            return AppendItem(BuildKey(e, "file", builder.ToString()), KindFile, GetFilesLabel(), builder.ToString());
        }

        private bool AppendToolItem(AppServerEventArgs e)
        {
            var toolName = BuildToolDisplayName(e);
            var message = string.IsNullOrWhiteSpace(e.Content)
                ? UiLocalization.Text("MainForm.TurnSummary.Mcp.Status.Completed", "Completed")
                : CollapseTextToSingleLine(e.Content);
            return AppendItem(BuildKey(e, "tool", toolName + ":" + message), KindTool, GetToolLabel(), toolName + ": " + message);
        }

        private bool AppendCommandItem(AppServerEventArgs e)
        {
            var text = CollapseTextToSingleLine(ActivityCommandTextFormatter.CompactCompletionText(
                e.Content,
                e.CommandText,
                e.WorkingDirectory));
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(e.CommandText))
            {
                text = ActivityCommandTextFormatter.CompactCommandText(e.CommandText, e.WorkingDirectory);
            }

            return AppendSimpleItem(e, KindCommand, GetCommandLabel(), text);
        }

        private bool AppendSimpleItem(AppServerEventArgs e, string keyPrefix, string label, string text)
        {
            var normalized = CollapseTextToSingleLine(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return AppendItem(BuildKey(e, keyPrefix, normalized), keyPrefix, label, normalized);
        }

        private bool AppendTurnSummaryItem(AppServerEventArgs e, string text)
        {
            return UpsertItem(
                BuildTurnSummaryKey(e != null ? e.TurnId : null),
                KindNotice,
                GetNoticeLabel(),
                CollapseTextToSingleLine(text));
        }

        private bool UpsertItem(string key, string kind, string label, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalizedKey = string.IsNullOrWhiteSpace(key)
                ? label + ":" + text
                : key;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item != null && string.Equals(item.Key, normalizedKey, StringComparison.Ordinal))
                {
                    return item.Update(kind, label, text.Trim());
                }
            }

            return AppendItem(normalizedKey, kind, label, text);
        }

        private bool AppendItem(string key, string kind, string label, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalizedKey = string.IsNullOrWhiteSpace(key)
                ? label + ":" + text
                : key;
            if (!_seenKeys.Add(normalizedKey))
            {
                return false;
            }

            _items.Add(new ActivityTimelineItem(normalizedKey, kind, label, text.Trim()));
            while (_items.Count > MaxItems)
            {
                var removed = _items[0];
                _items.RemoveAt(0);
                if (removed != null)
                {
                    _seenKeys.Remove(removed.Key);
                }
            }

            return true;
        }

        private static bool IsVisible(string kind, AppConfigSnapshot config)
        {
            if (config == null)
            {
                return true;
            }

            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case KindThinking:
                    return config.ShowActivityThinkingMessages;
                case KindPlan:
                    return config.ShowActivityPlanMessages;
                case KindTool:
                    return config.ShowActivityToolMessages;
                case KindCommand:
                    return config.ShowActivityCommandMessages;
                case KindFile:
                    return config.ShowActivityFileMessages;
                case KindNotice:
                    return config.ShowActivityNoticeMessages;
                case KindError:
                    return config.ShowActivityErrorMessages;
                case KindTurn:
                    return true;
                case KindApproval:
                    return config.ShowActivityApprovalMessages;
                case KindAccount:
                    return config.ShowActivityAccountMessages;
                case KindProtocolNotice:
                    return config.ShowActivityProtocolNoticeMessages;
                case KindProtocolWarning:
                    return config.ShowActivityProtocolWarningMessages;
                default:
                    return true;
            }
        }

        private static string BuildKey(AppServerEventArgs e, string prefix, string fallback)
        {
            var builder = new StringBuilder();
            builder.Append(prefix ?? string.Empty);
            builder.Append("|");
            builder.Append(e != null ? e.TurnId : string.Empty);
            builder.Append("|");
            builder.Append(e != null ? e.ItemId : string.Empty);
            builder.Append("|");
            builder.Append(fallback ?? string.Empty);
            return builder.ToString();
        }

        private static string BuildTurnSummaryKey(string turnId)
        {
            return "turn_summary|" + (turnId ?? string.Empty).Trim();
        }

        private static string BuildToolDisplayName(AppServerEventArgs e)
        {
            if (e == null)
            {
                return UiLocalization.Text("MainForm.TurnSummary.Mcp.DefaultToolName", "MCP tool");
            }

            if (!string.IsNullOrWhiteSpace(e.McpServerName) && !string.IsNullOrWhiteSpace(e.McpToolName))
            {
                return e.McpServerName.Trim() + " / " + e.McpToolName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(e.McpToolName))
            {
                return e.McpToolName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(e.McpServerName))
            {
                return e.McpServerName.Trim();
            }

            return UiLocalization.Text("MainForm.TurnSummary.Mcp.DefaultToolName", "MCP tool");
        }

        private static string NormalizePlanStatus(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                case "in_progress":
                case "inprogress":
                case "in progress":
                case "running":
                    return (status ?? string.Empty).Trim().ToLowerInvariant();
                default:
                    return string.Empty;
            }
        }

        private static string MapPlanStatusText(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.Completed", "Completed");
                case "in_progress":
                case "inprogress":
                case "in progress":
                case "running":
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.InProgress", "In Progress");
                default:
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.Step", "Step");
            }
        }

        private static string NormalizeReasoningSummaryForDisplay(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            normalized = normalized.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = NormalizeReasoningSummaryLine(lines[i]);
            }

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private static string NormalizeReasoningSummaryLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            if (text.StartsWith("**", StringComparison.Ordinal))
            {
                var end = text.IndexOf("**", 2, StringComparison.Ordinal);
                if (end > 2)
                {
                    var title = text.Substring(2, end - 2).Trim();
                    var rest = text.Substring(end + 2).Trim();
                    return rest.Length > 0
                        ? title + ": " + rest
                        : title;
                }
            }

            return text.Replace("**", string.Empty).Trim();
        }

        private static bool IsPlaceholderReasoningSummary(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            return normalized.Length == 0 ||
                   string.Equals(normalized, "Analysis completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "\u5206\u6790\u5b8c\u6210", StringComparison.OrdinalIgnoreCase);
        }

        private static string CollapseTextToSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder();
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(line);
            }

            return builder.ToString();
        }

        private static string GetThinkingLabel()
        {
            return TrimLabel(UiLocalization.Text("MainForm.TurnSummary.Section.Reasoning", "Thinking:"));
        }

        private static string GetPlanLabel()
        {
            return TrimLabel(UiLocalization.Text("MainForm.TurnSummary.Section.Plan", "Plan:"));
        }

        private static string GetToolLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Tool", "Tool");
        }

        private static string GetNetworkLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Network", "Network");
        }

        private static string GetFilesLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Files", "Changes");
        }

        private static string GetCommandLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Command", "Command");
        }

        private static string GetNoticeLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Notice", "Notice");
        }

        private static string GetErrorLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Error", "Error");
        }

        private static string GetApprovalLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Approval", "Approval");
        }

        private static string GetAccountLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Account", "Account");
        }

        private static string GetProtocolNoticeLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.ProtocolNotice", "Protocol Notice");
        }

        private static string GetProtocolWarningLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.ProtocolWarning", "Protocol Warning");
        }

        private static string GetTurnLabel()
        {
            return UiLocalization.Text("MainForm.ActivityTimeline.Kind.Turn", "Turn");
        }

        private static string TrimLabel(string label)
        {
            var text = (label ?? string.Empty).Trim();
            while (text.EndsWith(":", StringComparison.Ordinal) ||
                   text.EndsWith("\uff1a", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).TrimEnd();
            }

            return text;
        }

        private sealed class ActivityTimelineItem
        {
            private readonly string _key;
            private string _kind;
            private string _label;
            private string _text;

            public ActivityTimelineItem(string key, string kind, string label, string text)
            {
                _key = key ?? string.Empty;
                _kind = kind ?? string.Empty;
                _label = label ?? string.Empty;
                _text = text ?? string.Empty;
            }

            public string Key
            {
                get { return _key; }
            }

            public string Kind
            {
                get { return _kind; }
            }

            public bool Update(string kind, string label, string text)
            {
                var normalizedKind = kind ?? string.Empty;
                var normalizedLabel = label ?? string.Empty;
                var normalizedText = text ?? string.Empty;
                if (string.Equals(_kind, normalizedKind, StringComparison.Ordinal) &&
                    string.Equals(_label, normalizedLabel, StringComparison.Ordinal) &&
                    string.Equals(_text, normalizedText, StringComparison.Ordinal))
                {
                    return false;
                }

                _kind = normalizedKind;
                _label = normalizedLabel;
                _text = normalizedText;
                return true;
            }

            public string BuildDisplayText()
            {
                return string.IsNullOrWhiteSpace(_label)
                    ? _text
                    : _label + ": " + _text;
            }
        }
    }
}
