using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly GitDiffService _gitDiffService =
            new GitDiffService(new MyCodex.Infrastructure.Git.GitDiffCommandRunner());
        private readonly GitRepositoryService _gitRepositoryService =
            new GitRepositoryService(new MyCodex.Infrastructure.Git.GitCommandRunner());
        private readonly EncodingSafetyService _encodingSafetyService = new EncodingSafetyService();
        private readonly Dictionary<string, GitWorkbenchForm> _gitWorkbenchForms =
            new Dictionary<string, GitWorkbenchForm>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _encodingSafetyWarningTurnKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Button _diffWorkbenchButton;

        private void OnDiffWorkbenchButtonClick(object sender, EventArgs e)
        {
            ShowGitWorkbenchForCurrentContext();
        }

        private void ShowGitWorkbenchForCurrentContext()
        {
            ShowGitWorkbenchForWorkspace(_currentWorkspace);
        }

        private void ShowGitWorkbenchForWorkspace(WorkspaceInfo workspace)
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                return;
            }

            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath) || !IsWorkspaceAvailable(workspace))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.DiffWorkbench.Message.OpenWorkspaceFirst", "Please open an available workspace first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var workspacePath = workspace.RootPath;
            var key = BuildWorkspaceAvailabilityCacheKey(workspacePath.Trim());
            GitWorkbenchForm existing;
            if (_gitWorkbenchForms.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
            {
                existing.Show();
                existing.ActivateAndFocusFiles();
                return;
            }

            var gitService = CreateGitRepositoryService(workspacePath);
            var form = new GitWorkbenchForm(
                workspacePath,
                workspace.GitBranch,
                gitService,
                ResolveActiveGitHubCliService(),
                _screenReaderService,
                OpenWorktreeWorkspaceInGitWorkbenchAsync,
                gitDiffReader: CreateGitDiffReader(workspacePath, gitService),
                textFileOpenPreferenceProvider: BuildTextFileOpenPreference);
            _gitWorkbenchForms[key] = form;
            form.RepositoryMetadataChanged += async (changedSender, args) =>
            {
                var changedForm = changedSender as GitWorkbenchForm;
                if (changedForm == null || changedForm.IsDisposed || string.IsNullOrWhiteSpace(changedForm.WorkspacePath))
                {
                    return;
                }

                await RefreshWorkspaceGitMetadataAsync(changedForm.WorkspacePath).ConfigureAwait(true);
            };
            form.WorkspacePathChanged += (changedSender, args) =>
            {
                var changedForm = changedSender as GitWorkbenchForm;
                if (changedForm == null || changedForm.IsDisposed || string.IsNullOrWhiteSpace(changedForm.WorkspacePath))
                {
                    return;
                }

                var changedKey = BuildWorkspaceAvailabilityCacheKey(changedForm.WorkspacePath.Trim());
                var staleKeys = new List<string>();
                foreach (var pair in _gitWorkbenchForms)
                {
                    if (ReferenceEquals(pair.Value, changedForm) &&
                        !string.Equals(pair.Key, changedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        staleKeys.Add(pair.Key);
                    }
                }

                for (var i = 0; i < staleKeys.Count; i++)
                {
                    _gitWorkbenchForms.Remove(staleKeys[i]);
                }

                _gitWorkbenchForms[changedKey] = changedForm;
            };
            form.FormClosed += (closedSender, args) =>
            {
                var closedKeys = new List<string>();
                foreach (var pair in _gitWorkbenchForms)
                {
                    if (ReferenceEquals(pair.Value, closedSender))
                    {
                        closedKeys.Add(pair.Key);
                    }
                }

                for (var i = 0; i < closedKeys.Count; i++)
                {
                    _gitWorkbenchForms.Remove(closedKeys[i]);
                }
            };
            form.Show();
        }

        private async Task OpenWorktreeWorkspaceInGitWorkbenchAsync(string workspacePath, GitWorktreeSwitchMode switchMode)
        {
            await LoadWorkspaceAsync(workspacePath, switchMode == GitWorktreeSwitchMode.NewSession);
        }

        private IGitRepositoryService CreateGitRepositoryService(string workspacePath)
        {
            if (IsRemoteMacRuntimeEnabled() && WorkspacePathUtilities.LooksLikeUnixAbsolutePath(workspacePath))
            {
                return UnixGitRepositoryServiceFactory.CreateForRemoteMac(GetCurrentRemoteMacConfig());
            }

            if (IsWslRuntimePath(workspacePath))
            {
                return UnixGitRepositoryServiceFactory.CreateForWsl(GetCurrentWslConfig());
            }

            return _gitRepositoryService;
        }

        private IGitDiffReader CreateGitDiffReader(string workspacePath, IGitRepositoryService fallback)
        {
            var runtimeSource = _appConfigSnapshot == null ? null : _appConfigSnapshot.CodexRuntimeSource;
            if (_appServerClient == null || fallback == null || !IsAppServerGitDiffRuntime(workspacePath, runtimeSource))
            {
                return fallback;
            }

            return new AppServerGitDiffPreferenceService(_appServerClient, fallback, runtimeSource);
        }

        private bool IsAppServerGitDiffRuntime(string workspacePath, string runtimeSource)
        {
            if (string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                return IsWslRuntimePath(workspacePath);
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource))
            {
                return IsRemoteMacRuntimeEnabled() && WorkspacePathUtilities.LooksLikeUnixAbsolutePath(workspacePath);
            }

            return false;
        }

        private void CloseGitWorkbenchForms()
        {
            if (_gitWorkbenchForms == null || _gitWorkbenchForms.Count == 0)
            {
                return;
            }

            var forms = new List<GitWorkbenchForm>(_gitWorkbenchForms.Values);
            _gitWorkbenchForms.Clear();
            for (var i = 0; i < forms.Count; i++)
            {
                var form = forms[i];
                if (form == null || form.IsDisposed)
                {
                    continue;
                }

                try
                {
                    form.Close();
                }
                catch
                {
                }
            }
        }

        private void CheckTurnEncodingSafety(string threadId, string turnId, string diffText, IReadOnlyList<string> changedFiles)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(diffText) && (changedFiles == null || changedFiles.Count == 0))
            {
                return;
            }

            var workspacePath = ResolveWorkspacePathForThread(threadId);
            var snapshot = _gitDiffService.BuildSnapshotFromTurnDiff(workspacePath, diffText, changedFiles);
            if (snapshot == null || snapshot.Files.Count <= 0)
            {
                return;
            }

            CheckEncodingSafetyForTurn(threadId, turnId, snapshot);
        }

        private ChatHistoryItem AddThreadEventHistoryItem(AppServerEventArgs e, string sender, string message, ChatHistoryCategory category)
        {
            var item = AddChatHistoryItem(sender, message, category);
            if (item == null || e == null)
            {
                return item;
            }

            item.ThreadId = e.ThreadId;
            item.TurnId = e.TurnId;
            return item;
        }

        private void CheckEncodingSafetyForTurn(string threadId, string turnId, GitDiffSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId) || snapshot == null)
            {
                return;
            }

            var key = BuildEncodingSafetyTurnKey(threadId, turnId);
            if (_encodingSafetyWarningTurnKeys.Contains(key))
            {
                return;
            }

            EncodingSafetyScanResult result;
            try
            {
                result = _encodingSafetyService.ScanDiffSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Encoding safety scan failed: {0}", ex.Message);
                return;
            }

            if (result == null || !result.HasIssues)
            {
                return;
            }

            _encodingSafetyWarningTurnKeys.Add(key);
            ReportEncodingSafetyWarning(threadId, turnId, result);
        }

        private bool HasEncodingSafetyWarning(string threadId, string turnId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            {
                return false;
            }

            return _encodingSafetyWarningTurnKeys.Contains(BuildEncodingSafetyTurnKey(threadId, turnId));
        }

        private void ReportEncodingSafetyWarning(string threadId, string turnId, EncodingSafetyScanResult result)
        {
            var affectedFileCount = Math.Max(1, result.AffectedFileCount);
            var summary = string.Format(
                UiLocalization.Text(
                    "MainForm.EncodingSafety.Warning.Template",
                    "Possible text encoding corruption detected in {0} file(s). Review changes before continuing."),
                affectedFileCount);
            var builder = new StringBuilder();
            builder.AppendLine(summary);
            builder.AppendLine(UiLocalization.Text(
                "MainForm.EncodingSafety.Warning.DetailHeader",
                "Possible encoding issues:"));

            var limit = Math.Min(result.Issues.Count, 8);
            for (var i = 0; i < limit; i++)
            {
                var issue = result.Issues[i];
                if (issue == null)
                {
                    continue;
                }

                var lineText = issue.LineNumber.HasValue
                    ? string.Format(
                        UiLocalization.Text("MainForm.EncodingSafety.Warning.Line.Template", " line {0}"),
                        issue.LineNumber.Value)
                    : string.Empty;
                builder.AppendLine(string.Format(
                    UiLocalization.Text(
                        "MainForm.EncodingSafety.Warning.Item.Template",
                        "- {0}{1}: {2} ({3})"),
                    string.IsNullOrWhiteSpace(issue.FilePath) ? "?" : issue.FilePath,
                    lineText,
                    string.IsNullOrWhiteSpace(issue.Reason) ? "unknown" : issue.Reason,
                    string.IsNullOrWhiteSpace(issue.Preview) ? "..." : issue.Preview));
            }

            if (result.Issues.Count > limit)
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text(
                        "MainForm.EncodingSafety.Warning.More.Template",
                        "... and {0} more issue(s)."),
                    result.Issues.Count - limit));
            }

            var message = builder.ToString().TrimEnd();
            var item = AddChatHistoryItem(GetErrorSenderText(), message, ChatHistoryCategory.Error);
            if (item != null)
            {
                item.ThreadId = threadId;
                item.TurnId = turnId;
            }

            AppendLog(message);
            SetHighPriorityStatus(summary);
        }

        private string ResolveWorkspacePathForThread(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return _currentWorkspace != null ? _currentWorkspace.RootPath : null;
            }

            if (_currentSession != null &&
                string.Equals(_currentSession.ThreadId, threadId, StringComparison.OrdinalIgnoreCase) &&
                _currentWorkspace != null)
            {
                return _currentWorkspace.RootPath;
            }

            ChatSession session = null;
            for (var i = 0; i < _sessions.Count; i++)
            {
                if (string.Equals(_sessions[i].ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                {
                    session = _sessions[i];
                    break;
                }
            }

            if (session == null)
            {
                return _currentWorkspace != null ? _currentWorkspace.RootPath : null;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            return workspace != null ? workspace.RootPath : (_currentWorkspace != null ? _currentWorkspace.RootPath : null);
        }

        private static string BuildEncodingSafetyTurnKey(string threadId, string turnId)
        {
            return (threadId ?? string.Empty).Trim() + "|" + ((turnId ?? string.Empty).Trim());
        }
    }
}
