using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private int _subAgentRuntimeVersion;
        private readonly Dictionary<string, int> _subAgentEventVersions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AppServerThreadSummary> _subAgentSummaries =
            new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _subAgentRecentWork = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _subAgentPrimedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _subAgentStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private async Task<AppServerThreadDetails> ReadSubAgentThreadAsync(string threadId)
        {
            var version = _subAgentRuntimeVersion;
            _subAgentEventVersions.TryGetValue(threadId, out var eventVersion);
            var details = await _appServerClient.ReadThreadAsync(threadId);
            if (IsDisposed || version != _subAgentRuntimeVersion) throw new OperationCanceledException();
            if (details != null && !string.Equals(details.ThreadId, threadId, StringComparison.Ordinal)) throw new InvalidOperationException("Thread identity mismatch.");
            _subAgentEventVersions.TryGetValue(threadId, out var currentEventVersion);
            if (details != null && eventVersion == currentEventVersion)
            {
                _subAgentSummaries[threadId] = details;
                UpdateSubAgentBusyState(threadId, details.ThreadStatus);
                var latestInput = details.Messages.LastOrDefault(message => string.Equals(message.Sender, "user", StringComparison.OrdinalIgnoreCase));
                if (latestInput != null) _subAgentRecentWork[threadId] = latestInput.Content;
            }
            return details;
        }

        private async Task RefreshSubAgentParentAsync(ChatSession parent)
        {
            var client = _appServerClient as IAppServerSubAgentClient;
            if (client == null) throw new NotSupportedException();
            var version = _subAgentRuntimeVersion;
            var threads = await client.ListSubAgentThreadsAsync(parent.ThreadId, CancellationToken.None);
            if (IsDisposed || version != _subAgentRuntimeVersion || !_sessions.Contains(parent)) throw new OperationCanceledException();
            var changed = MergeSubAgentSessions(parent, LocalSubAgentThreadSyncPlanner.FilterCurrentParentSubAgents(
                threads, parent.ThreadId, _projectInstructionsHelperThreadIds));
            if (!_subAgentPrimedParents.Contains(parent.ThreadId))
            {
                var history = await _appServerClient.ReadThreadAsync(parent.ThreadId);
                if (IsDisposed || version != _subAgentRuntimeVersion) throw new OperationCanceledException();
                if (history != null)
                {
                    foreach (var activity in history.Activities.AsEnumerable().Reverse()) TrackSubAgentWork(activity, true);
                }
                _subAgentPrimedParents.Add(parent.ThreadId);
            }
            if (changed) RefreshSessionList();
            else RefreshOpenSubAgentWorkbenchLists();
        }

        private void TrackSubAgentWork(AppServerEventArgs e, bool historical = false)
        {
            if (e == null) return;
            historical |= e.IsHistorical;
            var route = AppServerEventRouting.Resolve(e);
            if (!historical && (e.SubAgentActivity != null || e.ThreadStatus != null || e.Collaboration != null ||
                route == AppServerEventRoute.TurnStarted || AppServerEventRouting.IsTurnTerminal(route)))
            {
                var affectedThreads = new[] { e.ThreadId, e.SubAgentActivity?.AgentThreadId }
                    .Concat(e.Collaboration?.ReceiverThreadIds ?? Array.Empty<string>())
                    .Concat(e.Collaboration?.AgentsStates?.Keys ?? Enumerable.Empty<string>());
                foreach (var threadId in affectedThreads.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    _subAgentEventVersions.TryGetValue(threadId, out var eventVersion);
                    _subAgentEventVersions[threadId] = eventVersion + 1;
                }
            }
            var agentActivity = e.SubAgentActivity;
            if (agentActivity != null && !string.IsNullOrWhiteSpace(agentActivity.AgentThreadId) &&
                (!historical || !_subAgentStatuses.ContainsKey(agentActivity.AgentThreadId)))
            {
                // Interaction may only deliver a message; the refreshed thread decides whether work started.
                if (agentActivity.Kind != "interacted")
                    _subAgentStatuses[agentActivity.AgentThreadId] = agentActivity.Kind == "started" ? "running" : agentActivity.Kind;
                if (!historical)
                {
                    if (agentActivity.Kind == "started") MarkThreadBusy(agentActivity.AgentThreadId);
                    else if (agentActivity.Kind == "completed" || agentActivity.Kind == "interrupted") ClearThreadBusy(agentActivity.AgentThreadId);
                    _ = RefreshAgentWorkAsync(agentActivity.AgentThreadId);
                }
            }
            if (e.ThreadStatus != null && !string.IsNullOrWhiteSpace(e.ThreadId) &&
                _subAgentSummaries.TryGetValue(e.ThreadId, out var summary)) summary.ThreadStatus = e.ThreadStatus;
            if (!historical && e.ThreadStatus != null && !string.IsNullOrWhiteSpace(e.ThreadId) &&
                (_subAgentStatuses.ContainsKey(e.ThreadId) || FindSessionByThreadId(e.ThreadId)?.IsSubAgent == true))
                UpdateSubAgentBusyState(e.ThreadId, e.ThreadStatus);
            if (LocalSubAgentThreadSyncPlanner.IsUserSubAgent(e.ThreadSummary) && !string.IsNullOrWhiteSpace(e.ThreadSummary.ParentThreadId) &&
                !_projectInstructionsHelperThreadIds.Contains(e.ThreadSummary.ThreadId))
            {
                var parent = FindSessionByThreadId(e.ThreadSummary.ParentThreadId);
                if (parent != null) MergeSubAgentSessions(parent, new[] { e.ThreadSummary });
            }
            if (e.Collaboration == null) return;
            if (e.Collaboration.AgentsStates != null)
            {
                foreach (var pair in e.Collaboration.AgentsStates)
                    if (pair.Value != null && (!historical || !_subAgentStatuses.ContainsKey(pair.Key)))
                        _subAgentStatuses[pair.Key] = pair.Value.Status;
            }
            if (string.IsNullOrWhiteSpace(e.Collaboration.Prompt)) return;
            foreach (var receiver in e.Collaboration.ReceiverThreadIds)
            {
                if (!string.IsNullOrWhiteSpace(receiver) && (!historical || !_subAgentRecentWork.ContainsKey(receiver)))
                    _subAgentRecentWork[receiver] = e.Collaboration.Prompt;
            }
        }

        private async Task RefreshAgentWorkAsync(string threadId)
        {
            try { await ReadSubAgentThreadAsync(threadId); }
            catch (Exception ex) { Trace.TraceWarning("Subagent work refresh failed: {0}", ex.Message); }
        }

        private void UpdateSubAgentBusyState(string threadId, AppServerThreadStatus status)
        {
            switch (status?.Type)
            {
                case "active":
                    MarkThreadBusy(threadId);
                    break;
                case "idle":
                case "notLoaded":
                case "systemError":
                    ClearThreadBusy(threadId);
                    break;
            }
        }

        private async Task RunLocalCurrentSubAgentThreadSyncAsync()
        {
            if (_isLocalSubAgentThreadSyncRunning || _isRuntimeWindowTransitionInProgress ||
                _currentInteractionMode == AppInteractionMode.Office) return;
            var client = _appServerClient as IAppServerSubAgentClient;
            if (client == null) return;
            var parents = _sessions.Where(session => session.Mode == AppInteractionMode.Coding &&
                    !string.IsNullOrWhiteSpace(session.ThreadId) &&
                    ReferenceEquals(session, _currentSession) && !_subAgentWorkbenches.ContainsKey(session.Id))
                .ToArray();
            if (parents.Length == 0) return;
            var version = _subAgentRuntimeVersion;
            _isLocalSubAgentThreadSyncRunning = true;
            try
            {
                var changed = false;
                foreach (var parent in parents)
                {
                    var threads = await client.ListSubAgentThreadsAsync(parent.ThreadId, CancellationToken.None);
                    if (IsDisposed || version != _subAgentRuntimeVersion || _isRuntimeWindowTransitionInProgress ||
                        !_sessions.Contains(parent)) return;
                    var descendants = LocalSubAgentThreadSyncPlanner.FilterCurrentParentSubAgents(
                        threads, parent.ThreadId, _projectInstructionsHelperThreadIds);
                    changed |= MergeSubAgentSessions(parent, descendants);
                }
                if (changed) RefreshSessionList();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Subagent discovery failed: {0}", ex.Message);
            }
            finally
            {
                if (version == _subAgentRuntimeVersion) _isLocalSubAgentThreadSyncRunning = false;
            }
        }

        private bool MergeSubAgentSessions(ChatSession parent, IEnumerable<AppServerThreadSummary> threads)
        {
            var workspace = FindWorkspaceById(parent.WorkspaceId);
            if (workspace == null) return false;
            var changed = false;
            foreach (var thread in threads)
            {
                (_appServerClient as IAppServerSubAgentClient)?.RememberSubAgentParent(thread.ThreadId, thread.ParentThreadId);
                _subAgentSummaries[thread.ThreadId] = thread;
                var session = _sessions.FirstOrDefault(candidate =>
                    string.Equals(candidate.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase));
                if (session == null)
                {
                    // Group under the parent workspace; never use this grouping to override the agent's runtime cwd.
                    session = _sessionService.CreateImportedSession(workspace, thread);
                    session.Mode = AppInteractionMode.Coding;
                    _sessions.Add(session);
                    changed = true;
                }
                changed |= (thread.UpdatedAt != default(DateTime) && session.LastActiveAt != thread.UpdatedAt) || session.Preview != thread.Preview ||
                    session.ParentThreadId != thread.ParentThreadId || session.SubAgentNickname != thread.AgentNickname ||
                    session.SubAgentRole != thread.AgentRole;
                session.IsSubAgent = true;
                session.ParentThreadId = thread.ParentThreadId;
                session.SubAgentDepth = thread.SubagentDepth;
                session.SubAgentNickname = thread.AgentNickname;
                session.SubAgentRole = thread.AgentRole;
                session.Preview = thread.Preview;
                if (thread.UpdatedAt != default(DateTime)) session.LastActiveAt = thread.UpdatedAt;
                if (!string.IsNullOrWhiteSpace(thread.SessionPath)) session.SessionPath = thread.SessionPath;
            }
            if (changed) PersistState();
            return changed;
        }
    }
}
