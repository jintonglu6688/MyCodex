using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionPathResolutionExecutor
    {
        internal static async Task<string> ResolveAsync(
            ChatSession session,
            Func<Task<List<AppServerThreadSummary>>> listThreadsAsync,
            Func<string, Task<AppServerThreadDetails>> readThreadAsync)
        {
            if (session == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(session.SessionPath))
            {
                return session.SessionPath;
            }

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return null;
            }

            if (listThreadsAsync != null)
            {
                var threads = await listThreadsAsync();
                var summaryPath = SessionStorageOperationPlanner.ResolveSessionPathFromThreadSummaries(threads, session.ThreadId);
                if (!string.IsNullOrWhiteSpace(summaryPath))
                {
                    session.SessionPath = summaryPath;
                    return summaryPath;
                }
            }

            if (readThreadAsync == null)
            {
                return null;
            }

            try
            {
                var details = await readThreadAsync(session.ThreadId);
                var detailsPath = SessionStorageOperationPlanner.ResolveSessionPathFromThreadDetails(details);
                if (!string.IsNullOrWhiteSpace(detailsPath))
                {
                    session.SessionPath = detailsPath;
                }
            }
            catch (InvalidOperationException ex)
            {
                if (!SessionStorageOperationPlanner.ShouldIgnoreThreadReadInvalidOperation(ex))
                {
                    throw;
                }
            }

            return session.SessionPath;
        }
    }
}
