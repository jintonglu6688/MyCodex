using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class SessionForkService
    {
        private readonly IAppServerThreadClient _client;

        public SessionForkService(IAppServerThreadClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<AppServerThreadSummary> ForkAsync(ChatSession source, WorkspaceInfo workspace,
            string lastTurnId, ApprovalMode? approvalMode, string modelId, string effort,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (source == null || string.IsNullOrWhiteSpace(source.ThreadId))
            {
                throw new ArgumentException("Source thread is required.", nameof(source));
            }
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                throw new ArgumentException("Workspace is required.", nameof(workspace));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(lastTurnId))
            {
                var connection = _client as IAppServerConnectionClient;
                if (connection == null || connection.ProtocolCapabilities == null ||
                    !connection.ProtocolCapabilities.SupportsThreadForkLastTurnId)
                {
                    throw new NotSupportedException(SharedLocalization.Text("SessionFork.UnsupportedAnchor", "This runtime does not support forking through a selected turn. Update the runtime or use a full fork."));
                }

                var details = await _client.ReadThreadAsync(source.ThreadId, cancellationToken).ConfigureAwait(false);
                string status;
                if (details == null || !string.Equals(details.ThreadId, source.ThreadId, StringComparison.Ordinal) ||
                    !details.TurnStatuses.TryGetValue(lastTurnId, out status) ||
                    !(string.Equals(status, "completed", StringComparison.Ordinal) ||
                      string.Equals(status, "interrupted", StringComparison.Ordinal) ||
                      string.Equals(status, "failed", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(SharedLocalization.Text("SessionFork.InvalidAnchor", "The selected turn is missing or has not finished. Refresh the conversation and choose a finished turn."));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var summary = await _client.ForkThreadAsync(source.ThreadId, lastTurnId, workspace.RootPath,
                approvalMode, modelId, effort).ConfigureAwait(false);
            if (summary == null || string.IsNullOrWhiteSpace(summary.ThreadId) ||
                string.Equals(summary.ThreadId, source.ThreadId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(SharedLocalization.Text("SessionFork.MissingThread", "The fork did not return a new thread ID."));
            }
            return summary;
        }

        public async Task<string> TrySetNameAsync(string threadId, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }
            try
            {
                await _client.SetThreadNameAsync(threadId, title).ConfigureAwait(false);
                return null;
            }
            catch (Exception)
            {
                return string.Format(SharedLocalization.Text("SessionFork.NameFailed", "Fork {0} was created, but its remote title could not be saved. The new session is available; retry renaming it later."), threadId);
            }
        }
    }
}
