using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public sealed partial class SessionThreadService
    {
        private readonly Dictionary<string, MetadataUpdate> _metadataUpdates = new Dictionary<string, MetadataUpdate>(StringComparer.Ordinal);
        private readonly object _metadataSync = new object();

        // Called after send acknowledgement. Coalesce updates while retaining the newest title/context.
        public Task SynchronizeMetadataAsync(ChatSession session, WorkspaceInfo workspace, string connectionScope, Func<bool> isCurrent)
        {
            if (session == null || workspace == null || string.IsNullOrWhiteSpace(session.ThreadId)) return Task.CompletedTask;
            var key = connectionScope + "\n" + session.ThreadId;
            var request = new MetadataRequest
            {
                Thread = session.ThreadId, Title = session.Title, Workspace = workspace.RootPath, IsCurrent = isCurrent
            };
            lock (_metadataSync)
            {
                MetadataUpdate update;
                if (_metadataUpdates.TryGetValue(key, out update))
                {
                    update.Next = request;
                    return update.Completion.Task;
                }
                update = new MetadataUpdate { Next = request };
                _metadataUpdates.Add(key, update);
                _ = RunMetadataUpdatesAsync(key, update);
                return update.Completion.Task;
            }
        }

        private async Task RunMetadataUpdatesAsync(string key, MetadataUpdate update)
        {
            await Task.Yield();
            try
            {
                for (;;)
                {
                    MetadataRequest request;
                    lock (_metadataSync)
                    {
                        request = update.Next;
                        update.Next = null;
                        if (request == null) { _metadataUpdates.Remove(key); break; }
                    }
                    try
                    {
                        if (!request.IsCurrent()) continue;
                        // Read off the UI thread; recheck target ownership after the await.
                        var git = await Task.Run(() => BuildThreadGitInfoUpdate(request.Workspace));
                        if (!request.IsCurrent()) continue;
                        if (!string.IsNullOrWhiteSpace(request.Title))
                            await _appServerClient.SetThreadNameAsync(request.Thread, request.Title);
                        if (request.IsCurrent()) await _appServerClient.UpdateThreadGitMetadataAsync(request.Thread, git);
                    }
                    catch (Exception error)
                    {
                        System.Diagnostics.Trace.TraceWarning("[SendMetadata] Background synchronization failed. ExceptionType={0}", error.GetType().Name);
                    }
                }
            }
            finally { update.Completion.TrySetResult(true); }
        }

        private sealed class MetadataRequest
        {
            public string Thread;
            public string Title;
            public string Workspace;
            public Func<bool> IsCurrent;
        }

        private sealed class MetadataUpdate
        {
            public MetadataRequest Next;
            public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
