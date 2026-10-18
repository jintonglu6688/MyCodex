using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private HashSet<string> _confirmedDeletionThreadIds;
        private bool _permanentDeletionInProgress;
        private AppServerProtocolCapabilities _permanentDeletionCapabilities;
        private async Task DeleteSubAgentsAsync(IReadOnlyList<ChatSession> sessions)
        {
            var targets = NormalizeSubAgentDeletionTargets(sessions);
            if (targets.Count > 0) await DeleteSessionFilesAsync(targets);
        }

        private async Task<SessionPermanentDeletionPlan> ResolvePermanentDeletionPlanAsync(List<ChatSession> sessions, int version)
        {
            var client = _appServerClient as IAppServerSubAgentClient;
            var summaries = new List<AppServerThreadSummary>();
            var missing = new HashSet<string>(StringComparer.Ordinal);
            var catalog = _appServerClient as IAppServerLocalThreadCatalogClient;
            var targets = sessions.Where(session => !string.IsNullOrWhiteSpace(session.ThreadId)).ToList();
            foreach (var session in targets)
            {
                if (client == null || _permanentDeletionCapabilities?.SupportsSubagentThreadFilters != true || (!string.IsNullOrWhiteSpace(session.RuntimeProfileId) &&
                    !_runtimeProfileService.IsSameRuntimeProfile(session.RuntimeProfileId, GetConfiguredRuntimeProfileId())))
                    throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The complete deletion scope is unavailable. Nothing was deleted."));
                var descendants = await client.ListSubAgentThreadsAsync(session.ThreadId, CancellationToken.None, true);
                var root = await SessionPermanentDeletionPlanner.ReadDeletionRootAsync(session, descendants,
                    _appServerClient.ReadThreadAsync, catalog == null ? (Func<string, string, Task<bool>>)null : catalog.ConfirmThreadMissingAsync);
                if (!IsPermanentDeletionContextCurrent(version)) throw new OperationCanceledException();
                if (root == null)
                {
                    missing.Add(session.ThreadId);
                    root = new AppServerThreadDetails { ThreadId = session.ThreadId, Title = session.Title,
                        ThreadStatus = new AppServerThreadStatus { Type = "notLoaded" } };
                }
                if (!string.Equals(root.ThreadId, session.ThreadId, StringComparison.Ordinal))
                    throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The complete deletion scope is unavailable. Nothing was deleted."));
                try { SessionPermanentDeletionPlanner.EnsureCompleteDescendants(root, descendants); }
                catch (InvalidOperationException)
                {
                    throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The complete deletion scope is unavailable. Nothing was deleted."));
                }
                summaries.AddRange(descendants);
                summaries.Add(root);
            }
            try
            {
                var plan = SessionPermanentDeletionPlanner.Build(targets, summaries);
                plan.MissingThreadIds.UnionWith(missing);
                return plan;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The complete deletion scope is unavailable or contains active/unknown threads. Nothing was deleted."));
            }
        }

        private bool IsPermanentDeletionContextCurrent(int version)
        {
            return !IsDisposed && !_isRuntimeWindowTransitionInProgress &&
                version == _subAgentRuntimeVersion && IsConnectedStatus() &&
                ReferenceEquals(_permanentDeletionCapabilities, _appServerClient.ProtocolCapabilities);
        }

        private async Task<int> DeleteSessionFilesAsync(List<ChatSession> sessions)
        {
            if (_permanentDeletionInProgress) throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The deletion scope is unavailable or busy. Nothing was deleted."));
            _permanentDeletionInProgress = true;
            try { return await DeleteSessionFilesCoreAsync(sessions); }
            finally { _permanentDeletionInProgress = false; _permanentDeletionCapabilities = null; }
        }

        private async Task<int> DeleteSessionFilesCoreAsync(List<ChatSession> sessions)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.DeleteSession", "Delete session")))
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.AppServerDisconnectedCannotDeleteSession", "App Server is not connected. Unable to delete session."));
            var targets = (sessions ?? new List<ChatSession>()).Where(session => session != null).ToList();
            if (targets.Count == 0) throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.NoSessionFilesToDeleteInWorkspace", "No session files available for permanent deletion in current workspace."));
            if (targets.Any(session => !string.IsNullOrWhiteSpace(session.RuntimeProfileId) &&
                !_runtimeProfileService.IsSameRuntimeProfile(session.RuntimeProfileId, GetConfiguredRuntimeProfileId())))
                throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The deletion scope is unavailable or busy. Nothing was deleted."));
            _permanentDeletionCapabilities = _appServerClient.ProtocolCapabilities;
            var version = _subAgentRuntimeVersion;
            var plan = await ResolvePermanentDeletionPlanAsync(targets, version);
            var files = SessionStorageOperationPlanner.SelectFileDeletionSessions(targets);
            if (plan.Threads.Count == 0 && files.Count == 0)
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.NoSessionFilesToDeleteInWorkspace", "No session files available for permanent deletion in current workspace."));
            var names = plan.Threads.Select(thread => (thread.Title ?? thread.AgentNickname ?? thread.ThreadId) + " [" + thread.ThreadId + "]")
                .Concat(files.Select(session => session.Title + " [" + session.SessionPath + "]"));
            var confirmation = string.Format(UiLocalization.Text("PermanentDeletion.Confirm.Template",
                "Permanently delete these {0} threads/files, including all spawned descendants? This cannot be undone.{1}{1}{2}"),
                plan.Threads.Count + files.Count, Environment.NewLine, string.Join(Environment.NewLine, names));
            if (MessageBox.Show(this, confirmation, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return 0;
            var verified = await ResolvePermanentDeletionPlanAsync(targets, version);
            if (!SessionPermanentDeletionPlanner.SameScope(plan, verified))
                throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeChanged", "The deletion scope changed. Review it again before deleting."));
            var confirmedIds = new HashSet<string>(StringComparer.Ordinal);
            _confirmedDeletionThreadIds = confirmedIds;
            SessionPermanentDeletionResult result;
            try
            {
                result = await SessionPermanentDeletionPlanner.ExecuteAsync(verified, _appServerClient.DeleteThreadAsync,
                    ids => { foreach (var id in ids) HandleConfirmedThreadDeletion(id); },
                    () => IsPermanentDeletionContextCurrent(version), confirmedIds.Contains);
            }
            finally
            {
                if (ReferenceEquals(_confirmedDeletionThreadIds, confirmedIds)) _confirmedDeletionThreadIds = null;
            }
            var deletedFiles = new List<string>();
            var failedFiles = new List<string>();
            foreach (var session in files)
            {
                try
                {
                    if (!IsPermanentDeletionContextCurrent(version)) throw new OperationCanceledException();
                    await DeleteSessionFileAsync(session);
                    if (!IsPermanentDeletionContextCurrent(version)) throw new OperationCanceledException();
                    RemoveConfirmedSessionLocally(session);
                    deletedFiles.Add(session.SessionPath);
                }
                catch { failedFiles.Add(session.SessionPath); }
            }
            if (IsPermanentDeletionContextCurrent(version)) ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
            var deleted = result.DeletedThreadIds.Count + deletedFiles.Count;
            var failed = result.FailedThreadIds.Count + failedFiles.Count;
            var status = LocalSessionRemovalTextPlanner.BuildPermanentDeletionResultText(result, deletedFiles, failedFiles);
            if (IsPermanentDeletionContextCurrent(version)) SetHighPriorityStatus(status);
            if (failed > 0) throw new InvalidOperationException(status);
            return deleted;
        }

        private Task DeleteSessionFileAsync(ChatSession session)
        {
            if (session == null || !string.IsNullOrWhiteSpace(session.ThreadId) || string.IsNullOrWhiteSpace(session.SessionPath))
                throw new InvalidOperationException(UiLocalization.Text("PermanentDeletion.ScopeUnavailable", "The complete deletion scope is unavailable or contains active/unknown threads. Nothing was deleted."));
            return _appServerClient.DeleteThreadFileAsync(session.SessionPath);
        }

        private void HandleConfirmedThreadDeletion(string threadId)
        {
            _confirmedDeletionThreadIds?.Add(threadId);
            RemoveConfirmedThreadLocally(threadId);
        }

        private void RemoveConfirmedThreadLocally(string threadId)
        {
            var deletedCurrentSession = _currentSession != null && string.Equals(_currentSession.ThreadId, threadId, StringComparison.Ordinal);
            foreach (var session in _sessions.Where(item => string.Equals(item.ThreadId, threadId, StringComparison.Ordinal)).ToArray())
                RemoveConfirmedSessionLocally(session);
            _subAgentSummaries.Remove(threadId);
            _subAgentRecentWork.Remove(threadId);
            _subAgentStatuses.Remove(threadId);
            _subAgentPrimedParents.Remove(threadId);
            if (deletedCurrentSession) FinalizeDeletionUiState();
            else ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
        }

        private void RemoveConfirmedSessionLocally(ChatSession session)
        {
            ApplyLocalRemovalPlan(LocalSessionRemovalPlanner.BuildSessionRemoval(session, _recentWorkspaces,
                _sessions, _inactiveRuntimeSessions, _currentWorkspace, _currentSession, false));
        }
    }
}
