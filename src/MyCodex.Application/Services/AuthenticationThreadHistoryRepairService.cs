using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    // Repairs a refused thread history through the authentication backend, which reads
    // every session file form on its target; without it the local repair runs instead.
    public sealed class AuthenticationThreadHistoryRepairService : IThreadHistoryRepairService
    {
        private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(1);
        private readonly Func<IAuthenticationCenterClient> _createClient;
        private readonly IThreadHistoryRepairService _fallback;
        private readonly Func<int, Task<bool>> _confirmCompactionReplacement;

        public AuthenticationThreadHistoryRepairService(Func<IAuthenticationCenterClient> createClient,
            IThreadHistoryRepairService fallback, Func<int, Task<bool>> confirmCompactionReplacement)
        {
            _createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
            _fallback = fallback;
            _confirmCompactionReplacement = confirmCompactionReplacement;
        }

        public async Task<ThreadHistoryRepairResult> TryRepairInvalidEncryptedContentAsync(ChatSession session)
        {
            var threadId = session != null ? session.ThreadId : null;
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return await FallbackAsync(session).ConfigureAwait(false);
            }

            AuthenticationSessionRepairResult result;
            try
            {
                result = await RepairAsync(threadId, AuthenticationSessionRepairMode.Rejected).ConfigureAwait(false);
            }
            catch (AuthenticationCenterException error) when (error.Code == "session_changed")
            {
                // The thread is still loaded and writing; a restart unloads it.
                return ThreadHistoryRepairResult.NotRepaired("session file in use");
            }
            catch (Exception)
            {
                return await FallbackAsync(session).ConfigureAwait(false);
            }

            if (result.Repaired)
            {
                return ThreadHistoryRepairResult.CreateRepaired(result.Changes);
            }

            if (!string.Equals(result.Skipped, "nothing_to_repair", StringComparison.Ordinal))
            {
                return await FallbackAsync(session).ConfigureAwait(false);
            }

            // Still refused after the lossless repair: only the compaction items another
            // provider issued remain, and replacing them loses the conversation they carry.
            if (result.RemainingCompactions > 0 &&
                _confirmCompactionReplacement != null &&
                await _confirmCompactionReplacement(result.RemainingCompactions).ConfigureAwait(false))
            {
                try
                {
                    var replaced = await RepairAsync(threadId, AuthenticationSessionRepairMode.Compaction).ConfigureAwait(false);
                    if (replaced.Repaired)
                    {
                        return ThreadHistoryRepairResult.CreateRepaired(replaced.Changes);
                    }
                }
                catch (Exception)
                {
                    return ThreadHistoryRepairResult.Final("repair failed");
                }
            }

            return ThreadHistoryRepairResult.Final("nothing to repair");
        }

        private async Task<AuthenticationSessionRepairResult> RepairAsync(string threadId, AuthenticationSessionRepairMode mode)
        {
            var client = _createClient() ?? throw new InvalidOperationException("Authentication backend unavailable.");
            using (var timeout = new CancellationTokenSource(RepairTimeout))
            {
                return await client.RepairSessionHistoryAsync(threadId, mode, timeout.Token).ConfigureAwait(false);
            }
        }

        private Task<ThreadHistoryRepairResult> FallbackAsync(ChatSession session)
        {
            return _fallback != null
                ? _fallback.TryRepairInvalidEncryptedContentAsync(session)
                : Task.FromResult(ThreadHistoryRepairResult.NotRepaired("repair service unavailable"));
        }
    }
}
