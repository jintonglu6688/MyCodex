using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private readonly object _dispatchSync = new object();
        private int _activeDispatches;
        private bool _exitPrepared;

        public bool TryPauseForExit()
        {
            lock (_dispatchSync)
            {
                if (_activeDispatches != 0) return false;
                _exitPrepared = true;
                return true;
            }
        }

        public void ResumeAfterCancelledExit()
        {
            lock (_dispatchSync) _exitPrepared = false;
        }

        public async Task<RemoteRelayEvent> DispatchAsync(RemoteRelayCommand command, CancellationToken cancellationToken)
        {
            lock (_dispatchSync)
            {
                if (_exitPrepared)
                    return CreateError(command?.RequestId, command?.OperationId, command?.DeviceId,
                        RemoteErrorCodes.OperationFailed, "This target is preparing to exit. Retry after exit is cancelled.");
                _activeDispatches++;
            }
            try { return await DispatchCoreAsync(command, cancellationToken).ConfigureAwait(false); }
            finally { lock (_dispatchSync) _activeDispatches--; }
        }
    }
}
