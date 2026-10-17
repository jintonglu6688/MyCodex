using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.RemoteHost
{
    public sealed class RemoteHostServiceController : IDisposable
    {
        private readonly IRemoteServiceConfigStore _configStore;
        private readonly IRelayHostRealtimeClient _realtimeClient;
        private readonly IEmbeddedRelayService _embeddedRelayService;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly Action<RemoteServiceConfigSnapshot> _prepareSnapshot;
        private RemoteServiceConfigSnapshot _observedConfiguration;

        public RemoteHostServiceController(IRemoteServiceConfigStore configStore, IRelayHostRealtimeClient realtimeClient)
            : this(configStore, realtimeClient, null)
        {
        }

        public RemoteHostServiceController(
            IRemoteServiceConfigStore configStore,
            IRelayHostRealtimeClient realtimeClient,
            IEmbeddedRelayService embeddedRelayService,
            Action<RemoteServiceConfigSnapshot> prepareSnapshot = null)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _realtimeClient = realtimeClient ?? throw new ArgumentNullException(nameof(realtimeClient));
            _embeddedRelayService = embeddedRelayService;
            _prepareSnapshot = prepareSnapshot;
        }

        public async Task RefreshConfigurationAsync(CancellationToken cancellationToken)
        {
            if (!await _lifecycleGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
            try
            {
                var snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
                _prepareSnapshot?.Invoke(snapshot);
                if (!RemoteServiceConfigurationComparer.ConnectionChanged(_observedConfiguration, snapshot)) return;
                await _realtimeClient.StopAsync().ConfigureAwait(false);
                await StartCoreAsync(cancellationToken, true).ConfigureAwait(false);
            }
            finally { _lifecycleGate.Release(); }
        }

        public async Task StartIfConfiguredAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StartCoreAsync(cancellationToken, false).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task RestartIfConfiguredAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _realtimeClient.StopAsync().ConfigureAwait(false);
                await StartCoreAsync(cancellationToken, false).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public Task StopAsync()
        {
            return _realtimeClient.StopAsync();
        }

        public async Task ApplyConfiguredModeAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _realtimeClient.StopAsync().ConfigureAwait(false);
                await StartCoreAsync(cancellationToken, true).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _realtimeClient.StopAsync().ConfigureAwait(false);
                if (_embeddedRelayService == null)
                {
                    return;
                }

                await _embeddedRelayService.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public void Dispose()
        {
            _realtimeClient.Dispose();
            (_embeddedRelayService as IDisposable)?.Dispose();
            _lifecycleGate.Dispose();
        }

        private async Task StartCoreAsync(CancellationToken cancellationToken, bool startEmbeddedRegardless)
        {
            var snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
            _prepareSnapshot?.Invoke(snapshot);
            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, Environment.MachineName);
            if (snapshot.RemoteEnabled == false)
            {
                if (_embeddedRelayService != null)
                {
                    await _embeddedRelayService.StopAsync(cancellationToken).ConfigureAwait(false);
                }

                RememberConfiguration(snapshot);
                return;
            }

            if (RemoteServiceConfigPlanner.IsEmbeddedMode(snapshot.RemoteServiceMode))
            {
                if (_embeddedRelayService == null)
                {
                    return;
                }

                var status = _embeddedRelayService.GetStatus(snapshot);
                var running = status != null && status.State == EmbeddedRelayRunState.Running;
                // Start also acquires this target's lease on an already running shared service.
                if (running || startEmbeddedRegardless || snapshot.StartEmbeddedRelayWithApplication)
                {
                    var result = await _embeddedRelayService.StartAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    if (result == null || !result.Success)
                    {
                        throw new InvalidOperationException(
                            result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                                ? "The built-in remote service could not be started."
                                : result.ErrorMessage);
                    }

                    snapshot = _configStore.Load() ?? snapshot;
                    RemoteServiceConfigPlanner.EnsureDefaults(snapshot, Environment.MachineName);
                    running = true;
                }

                if (!running)
                {
                    RememberConfiguration(snapshot);
                    return;
                }
            }
            else if (_embeddedRelayService != null)
            {
                await _embeddedRelayService.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!IsConfigured(snapshot))
            {
                RememberConfiguration(snapshot);
                return;
            }

            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, _configStore.UnprotectAccessSecret(snapshot));
            await _realtimeClient.StartAsync(config, snapshot.HostId, cancellationToken).ConfigureAwait(false);
            RememberConfiguration(snapshot);
        }

        private void RememberConfiguration(RemoteServiceConfigSnapshot snapshot)
        {
            _observedConfiguration = RemoteServiceConfigProjection.Compose(
                RemoteServiceConfigProjection.SplitShared(snapshot), RemoteServiceConfigProjection.SplitTargetIdentity(snapshot));
        }

        private static bool IsConfigured(RemoteServiceConfigSnapshot snapshot)
        {
            return snapshot != null &&
                   !string.IsNullOrWhiteSpace(snapshot.RelayHost) &&
                   snapshot.RelayPort > 0 &&
                   !string.IsNullOrWhiteSpace(snapshot.TenantId) &&
                   !string.IsNullOrWhiteSpace(snapshot.HostId) &&
                   !string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret);
        }
    }
}
