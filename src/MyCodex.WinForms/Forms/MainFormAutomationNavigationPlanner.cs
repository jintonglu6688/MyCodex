using System;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal enum MainFormAutomationNavigationDecisionKind
    {
        MissingPath,
        RemotePathUnsupported,
        OpenLocalPath,
        PathNotFound
    }

    internal sealed class MainFormAutomationNavigationDecision
    {
        public MainFormAutomationNavigationDecision(MainFormAutomationNavigationDecisionKind kind, string path)
        {
            Kind = kind;
            Path = path ?? string.Empty;
        }

        public MainFormAutomationNavigationDecisionKind Kind { get; private set; }

        public string Path { get; private set; }
    }

    internal static class MainFormAutomationNavigationPlanner
    {
        public static bool CanOpenThread(AutomationRunSnapshot run)
        {
            return run != null && !string.IsNullOrWhiteSpace(run.ThreadId);
        }

        public static MainFormAutomationNavigationDecision BuildPathDecision(
            string path,
            Func<string, bool> fileExists,
            Func<string, bool> directoryExists)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new MainFormAutomationNavigationDecision(MainFormAutomationNavigationDecisionKind.MissingPath, string.Empty);
            }

            var normalized = path.Trim();
            if (IsRemotePath(normalized))
            {
                return new MainFormAutomationNavigationDecision(MainFormAutomationNavigationDecisionKind.RemotePathUnsupported, normalized);
            }

            var hasFile = fileExists != null && fileExists(normalized);
            var hasDirectory = directoryExists != null && directoryExists(normalized);
            return hasFile || hasDirectory
                ? new MainFormAutomationNavigationDecision(MainFormAutomationNavigationDecisionKind.OpenLocalPath, normalized)
                : new MainFormAutomationNavigationDecision(MainFormAutomationNavigationDecisionKind.PathNotFound, normalized);
        }

        public static bool IsRemotePath(string path)
        {
            return WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path);
        }
    }
}
