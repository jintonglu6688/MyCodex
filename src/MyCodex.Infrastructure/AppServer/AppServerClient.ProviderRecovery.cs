using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient
    {
        public Func<string, string, AppServerModelProviderException, Task<bool>> ThreadProviderRecovery { get; set; }
        public Func<Task> SessionProviderRecoveryBeforeConnect { get; set; }

        private async Task<T> WithProviderRecoveryAsync<T>(string threadId, string cwd, Func<Task<T>> action)
        {
            try { return await action().ConfigureAwait(false); }
            catch (AppServerModelProviderException error)
            {
                var recover = ThreadProviderRecovery;
                if (recover == null || !await recover(threadId, cwd, error).ConfigureAwait(false)) throw;
            }
            // Only the rejected resume is retried; never retry an accepted turn or tool call.
            return await action().ConfigureAwait(false);
        }

        public async Task RunSessionProviderMaintenanceAsync(Func<Task> repair)
        {
            using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
            {
                if (transition == null || HasAuthenticationTransitionWork)
                    throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.Busy",
                        "Finish the running tasks before repairing this conversation. No task was stopped."));
                await _connectionLifecycleLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    ThrowIfDisposed();
                    System.Diagnostics.Process process;
                    System.IO.StreamWriter writer;
                    lock (_syncRoot)
                    {
                        process = _process;
                        writer = _inputWriter;
                        _connectionLostHandled = true;
                        _isConnected = false;
                    }
                    await _writeLock.WaitAsync().ConfigureAwait(false);
                    try { writer?.Close(); }
                    finally { _writeLock.Release(); }
                    if (process != null && !await Task.Run(() => process.WaitForExit(10000)).ConfigureAwait(false))
                        throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.StopTimeout",
                            "Codex has not finished shutting down. No conversation files were changed; reconnect after it exits."));
                    DisconnectForRuntimeChange();
                    await repair().ConfigureAwait(false);
                    await ConnectCoreAsync().ConfigureAwait(false);
                    OnEventReceived(new AppServerEventArgs("status", SharedLocalization.Text("SessionProvider.Status.Repaired",
                        "The old conversation provider was repaired. Its history and thread ID were preserved, and a backup was kept.")));
                }
                finally { _connectionLifecycleLock.Release(); }
            }
        }
    }
}
