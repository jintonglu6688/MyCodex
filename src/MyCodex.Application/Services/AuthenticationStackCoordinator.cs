using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    // Runs one aggregation change inside the runtime transition and reconnects when the client
    // files changed: Codex reads its model catalog only when its App Server starts.
    public sealed class AuthenticationStackCoordinator
    {
        private readonly Func<IDisposable> _acquireTransition;
        private readonly Func<Task<bool>> _reconnect;

        public AuthenticationStackCoordinator(Func<IDisposable> acquireTransition, Func<Task<bool>> reconnect)
        {
            _acquireTransition = acquireTransition ?? throw new ArgumentNullException(nameof(acquireTransition));
            _reconnect = reconnect ?? throw new ArgumentNullException(nameof(reconnect));
        }

        public async Task<AuthenticationStackResult> RunAsync(Func<CancellationToken, Task<AuthenticationStackState>> change,
            CancellationToken cancellationToken)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            cancellationToken.ThrowIfCancellationRequested();
            using (var lease = _acquireTransition())
            {
                if (lease == null) throw new AuthenticationCenterException("runtime_busy");
                AuthenticationStackState state;
                try
                {
                    state = await change(cancellationToken);
                }
                catch (Exception error) when (IsUncertainWrite(error))
                {
                    throw new AuthenticationCenterException("operation_unknown");
                }
                var reconnected = true;
                if (state.GlobalApplied)
                {
                    try
                    {
                        reconnected = await _reconnect();
                    }
                    catch
                    {
                        reconnected = false;
                    }
                }
                return new AuthenticationStackResult { State = state, Reconnected = reconnected };
            }
        }

        private static bool IsUncertainWrite(Exception error)
        {
            var code = (error as AuthenticationCenterException)?.Code;
            // A partial write is finished by the backend on its next operation; the client may hold either list.
            return code == null || code == "backend_unavailable" || code == "invalid_response" || code == "operation_unknown" ||
                code == "stack_write_partial" || code == "ipc_disconnected" || code == "ipc_timeout";
        }
    }
}
