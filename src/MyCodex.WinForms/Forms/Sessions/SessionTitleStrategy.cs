using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionTitleStrategy
    {
        private static readonly Regex ForkSuffixRegex = new Regex(@"^(?<base>.+?)\s-\s(?<label>.+?)\s(?<index>\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ForkPrefixRegex = new Regex(@"^(?<label>.+?)\s(?<index>\d+)\s\|\s(?<base>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TypedTitlePrefixRegex = new Regex(@"^(?<label>[^:：|]+)\s*(?:[:：]|\|)\s*(?<base>.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool ShouldReplaceGeneratedTitle(ChatSession session, WorkspaceInfo workspace)
        {
            if (session == null || session.IsImported || workspace == null)
            {
                return false;
            }

            var prefix = workspace.Name + UiLocalization.Text("MainForm.Session.DefaultTitle.PrefixSuffix", " Session ");
            if (string.IsNullOrWhiteSpace(session.Title))
            {
                return false;
            }

            var title = session.Title.Trim();
            return title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, workspace.Name, StringComparison.OrdinalIgnoreCase);
        }

        internal static string BuildForkTitle(string sourceTitle, int forkIndex)
        {
            return ExtractBaseTitle(sourceTitle);
        }

        internal static string BuildUniqueTitle(string title, IEnumerable<ChatSession> sessions, string workspaceId, bool isFork, bool isReview)
        {
            var baseTitle = ExtractBaseTitle(title);
            if (string.IsNullOrWhiteSpace(baseTitle))
            {
                baseTitle = UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session");
            }

            var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    if (session == null ||
                        !string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase) ||
                        !IsSameSessionType(session, isFork, isReview))
                    {
                        continue;
                    }

                    existingTitles.Add(SessionDisplayFormatter.BuildNormalizedTitle(session));
                }
            }

            if (!existingTitles.Contains(baseTitle))
            {
                return baseTitle;
            }

            var suffixTemplate = UiLocalization.Text("MainForm.SessionList.DuplicateSuffix.Template", " ({0})");
            for (var index = 2; index < int.MaxValue; index++)
            {
                var candidate = baseTitle + string.Format(suffixTemplate, index);
                if (!existingTitles.Contains(candidate))
                {
                    return candidate;
                }
            }

            return baseTitle;
        }

        internal static string BuildTitleFromFirstMessage(string text, string previewText)
        {
            var candidate = string.IsNullOrWhiteSpace(text) ? previewText : text.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            candidate = candidate.Replace("\r", " ").Replace("\n", " ").Trim();
            while (candidate.Contains("  "))
            {
                candidate = candidate.Replace("  ", " ");
            }

            return candidate.Length > 40 ? candidate.Substring(0, 40) + "..." : candidate;
        }

        internal static string ExtractBaseTitle(string sourceTitle)
        {
            var candidate = string.IsNullOrWhiteSpace(sourceTitle)
                ? string.Empty
                : sourceTitle.Trim();

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return UiLocalization.Text("ForkSessionForm.Value.UnnamedSession", "Unnamed Session");
            }

            candidate = TrimTypedTitlePrefix(candidate);
            var forkLabelPrefixes = GetForkLabelPrefixes();
            while (TryTrimForkSuffix(candidate, forkLabelPrefixes, out var trimmed))
            {
                candidate = trimmed;
            }

            candidate = TrimTypedTitlePrefix(candidate);
            return string.IsNullOrWhiteSpace(candidate)
                ? UiLocalization.Text("ForkSessionForm.Value.UnnamedSession", "Unnamed Session")
                : candidate;
        }

        private static string TrimTypedTitlePrefix(string title)
        {
            var candidate = title ?? string.Empty;
            var forkPrefixMatch = ForkPrefixRegex.Match(candidate);
            if (forkPrefixMatch.Success && GetForkLabelPrefixes().Contains(forkPrefixMatch.Groups["label"].Value.Trim()))
            {
                return forkPrefixMatch.Groups["base"].Value.Trim();
            }

            var typedPrefixMatch = TypedTitlePrefixRegex.Match(candidate);
            if (!typedPrefixMatch.Success)
            {
                return candidate;
            }

            var label = typedPrefixMatch.Groups["label"].Value.Trim();
            return GetTypeLabels().Contains(label)
                ? typedPrefixMatch.Groups["base"].Value.Trim()
                : candidate;
        }

        private static bool TryTrimForkSuffix(string title, HashSet<string> forkLabelPrefixes, out string trimmed)
        {
            var match = ForkSuffixRegex.Match(title ?? string.Empty);
            if (!match.Success)
            {
                trimmed = title;
                return false;
            }

            var label = match.Groups["label"].Value.Trim();
            if (!forkLabelPrefixes.Contains(label))
            {
                trimmed = title;
                return false;
            }

            trimmed = match.Groups["base"].Value.Trim();
            return true;
        }

        private static HashSet<string> GetForkLabelPrefixes()
        {
            var currentLabel = string.Format(
                UiLocalization.Text("ForkSessionForm.BranchLabel", "Fork {0}"),
                string.Empty).Trim();

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                currentLabel,
                UiLocalization.Text("MainForm.Branch.Fork", "Fork"),
                "Fork",
                "分叉"
            };
        }

        private static HashSet<string> GetTypeLabels()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                UiLocalization.Text("MainForm.Branch.Mainline", "Mainline"),
                UiLocalization.Text("MainForm.Branch.Fork", "Fork"),
                UiLocalization.Text("MainForm.Branch.Review", "Review"),
                "Mainline",
                "Fork",
                "Review",
                "主线",
                "分叉",
                "审核",
                "评审"
            };
        }

        private static bool IsSameSessionType(ChatSession session, bool isFork, bool isReview)
        {
            if (isReview)
            {
                return session.IsReview;
            }

            if (isFork)
            {
                return session.IsFork && !session.IsReview;
            }

            return !session.IsFork && !session.IsReview;
        }
    }
}
