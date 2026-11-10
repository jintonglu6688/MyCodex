using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class SessionProviderMigrationCoordinator
    {
        private readonly Func<ISessionProviderMigrationClient> _client;
        private readonly Func<string, CancellationToken, Task<AppServerConfigSnapshot>> _readConfig;
        private readonly Func<Func<Task>, Task> _maintain;

        public SessionProviderMigrationCoordinator(Func<ISessionProviderMigrationClient> client,
            Func<string, CancellationToken, Task<AppServerConfigSnapshot>> readConfig,
            Func<Func<Task>, Task> maintain)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _readConfig = readConfig ?? throw new ArgumentNullException(nameof(readConfig));
            _maintain = maintain ?? throw new ArgumentNullException(nameof(maintain));
        }

        public async Task<bool> TryRepairAsync(string threadId, string cwd, AppServerModelProviderException error)
        {
            try { return await TryRepairCoreAsync(threadId, cwd, error).ConfigureAwait(false); }
            catch (AuthenticationCenterException ex)
            {
                if (ex.Code == "runtime_busy" || ex.Code == "route_requests_active" || ex.Code == "provider_index_busy")
                    throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.Busy",
                        "Finish the running tasks before repairing this conversation. No task was stopped."), ex);
                throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.Incomplete",
                    "The conversation provider repair did not complete. Reconnect and try again; the message was not sent."), ex);
            }
        }

        public async Task RecoverBeforeConnectAsync()
        {
            var client = _client();
            if (client == null) return;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                await client.RecoverSessionProvidersAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        private async Task<bool> TryRepairCoreAsync(string threadId, string cwd, AppServerModelProviderException error)
        {
            // Only resume admission failed: the user's turn has not been sent yet.
            if (error.Operation != "thread/resume" ||
                (error.ErrorType != "modelProviderNotConfigured" && error.ErrorType != "modelProviderMismatch")) return false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                var token = timeout.Token;
                var config = await _readConfig(cwd, token).ConfigureAwait(false);
                if (config.ModelProviderId != "custom") return false;
                var client = _client();
                if (client == null) return false;
                var preview = await client.PrepareSessionProviderAsync(threadId, "custom", false, token).ConfigureAwait(false);
                if (!preview.Supported)
                    throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.BackendUpdate",
                        "Update the authentication backend to repair this conversation's old provider configuration."));
                if (!preview.NeedsMigration) return false;
                await _maintain(async () =>
                {
                    var result = await client.PrepareSessionProviderAsync(threadId, "custom", true, token).ConfigureAwait(false);
                    if (!result.Supported || result.NeedsMigration || !result.Changed)
                        throw new InvalidOperationException(SharedLocalization.Text("SessionProvider.Error.Incomplete",
                            "The conversation provider repair did not complete. Reconnect and try again; the message was not sent."));
                }).ConfigureAwait(false);
                return true;
            }
        }
    }
}
