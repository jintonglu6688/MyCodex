using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionHeaderSummary
    {
        public SessionHeaderSummary(string titleText, string contextText, string statusText)
        {
            TitleText = titleText ?? string.Empty;
            ContextText = contextText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
        }

        public string TitleText { get; }

        public string ContextText { get; }

        public string StatusText { get; }
    }

    internal static class SessionHeaderSummaryBuilder
    {
        internal static SessionHeaderSummary Build(
            ChatSession session,
            WorkspaceInfo workspace,
            string modelSummary,
            string syncSummary,
            string notSelectedText,
            string notSyncedText)
        {
            var titleText = session != null
                ? SessionDisplayFormatter.BuildDisplayTitle(session)
                : (notSelectedText ?? string.Empty);

            var contextParts = new List<string>();
            AddPart(contextParts, workspace != null ? workspace.Name : null);
            AddPart(contextParts, workspace != null ? workspace.GitBranch : null);
            AddPart(contextParts, modelSummary);
            var contextText = contextParts.Count > 0
                ? string.Join(UiLocalization.Text("MainForm.Header.Summary.Separator", "  |  "), contextParts.ToArray())
                : (notSelectedText ?? string.Empty);

            var normalizedSyncSummary = string.IsNullOrWhiteSpace(syncSummary)
                ? notSyncedText
                : syncSummary.Trim();
            var statusText = string.Format(
                UiLocalization.Text("MainForm.Header.Summary.Sync.Template", "Sync {0}"),
                normalizedSyncSummary);

            return new SessionHeaderSummary(titleText, contextText, statusText);
        }

        internal static string CombineDetails(string contextText, string statusText, string separator)
        {
            var parts = new List<string>();
            AddPart(parts, contextText);
            AddPart(parts, statusText);
            return string.Join(separator, parts.ToArray());
        }

        private static void AddPart(ICollection<string> parts, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            parts.Add(value.Trim());
        }
    }
}
