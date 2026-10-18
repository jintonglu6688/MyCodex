using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    // With unified session history and the official provider in use, a thread another
    // provider worked on is repaired before it loads, so the request is not refused.
    // The decision is read once per App Server connection; Reset on reconnect.
    public sealed class SessionHistoryResumeRepairGate
    {
        private readonly Func<IAuthenticationCenterClient> _createClient;
        private readonly Action<AuthenticationSessionRepairResult> _repaired;
        private readonly TimeSpan _timeout;
        private readonly object _sync = new object();
        private bool? _enabled;
        private int _generation;

        public SessionHistoryResumeRepairGate(Func<IAuthenticationCenterClient> createClient,
            Action<AuthenticationSessionRepairResult> repaired = null, TimeSpan? timeout = null)
        {
            _createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
            _repaired = repaired;
            _timeout = timeout ?? TimeSpan.FromSeconds(15);
        }

        public void Reset()
        {
            lock (_sync)
            {
                _enabled = null;
                _generation++;
            }
        }

        // Never throws: a failed repair must not keep the thread from loading.
        public async Task PrepareAsync(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            try
            {
                using (var timeout = new CancellationTokenSource(_timeout))
                {
                    var client = _createClient();
                    if (client == null || !await IsEnabledAsync(client, timeout.Token).ConfigureAwait(false))
                    {
                        return;
                    }

                    var result = await client.RepairSessionHistoryAsync(threadId, AuthenticationSessionRepairMode.Resume,
                        timeout.Token).ConfigureAwait(false);
                    if (result.Skipped == "not_unified" || result.Skipped == "not_official")
                    {
                        Remember(false, null);
                    }
                    else if (result.Repaired)
                    {
                        _repaired?.Invoke(result);
                    }
                }
            }
            catch (Exception ex)
            {
                // Resume repair is best effort and must not block resuming the session.
                System.Diagnostics.Trace.TraceWarning("[SessionHistoryRepair] Resume repair failed. ExceptionType={0}", ex.GetType().Name);
            }
        }

        private async Task<bool> IsEnabledAsync(IAuthenticationCenterClient client, CancellationToken cancellationToken)
        {
            int generation;
            lock (_sync)
            {
                if (_enabled.HasValue)
                {
                    return _enabled.Value;
                }

                generation = _generation;
            }

            try
            {
                var state = await client.ReadSessionHistoryAsync(cancellationToken).ConfigureAwait(false);
                Remember(state.RepairOnResume, generation);
                return state.RepairOnResume;
            }
            catch (Exception)
            {
                // No backend, or one without unified session history: ask again after the
                // next connection instead of delaying every thread that loads until then.
                Remember(false, generation);
                return false;
            }
        }

        private void Remember(bool enabled, int? generation)
        {
            lock (_sync)
            {
                if (!generation.HasValue || generation.Value == _generation)
                {
                    _enabled = enabled;
                }
            }
        }
    }
}
