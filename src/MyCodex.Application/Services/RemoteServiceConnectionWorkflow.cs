using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public enum RemoteServiceConnectionStage
    {
        None,
        Validation,
        Saved,
        TestConnection,
        Registered
    }

    public sealed class RemoteServiceConnectionResult
    {
        public bool Success { get; set; }

        public RemoteServiceConnectionStage Stage { get; set; }

        public RemoteServiceConfigSnapshot Snapshot { get; set; }

        public Exception Error { get; set; }
    }

    public sealed class RemoteServiceConnectionWorkflow
    {
        private readonly IRemoteServiceConfigStore _configStore;
        private readonly IRelayHostClient _relayClient;

        public RemoteServiceConnectionWorkflow(IRemoteServiceConfigStore configStore, IRelayHostClient relayClient)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
        }

        public Task<RemoteServiceConnectionResult> SaveOnlyAsync(
            RemoteServiceConfigSnapshot snapshot,
            string rawAccessSecret,
            string machineName,
            CancellationToken cancellationToken)
        {
            SaveSnapshot(snapshot, rawAccessSecret, machineName);
            return Task.FromResult(new RemoteServiceConnectionResult
            {
                Success = true,
                Stage = RemoteServiceConnectionStage.Saved,
                Snapshot = snapshot
            });
        }

        public async Task<RemoteServiceConnectionResult> ConnectAndRegisterAsync(
            RemoteServiceConfigSnapshot snapshot,
            string rawAccessSecret,
            string machineName,
            RemoteEndpointIdentity identity,
            CancellationToken cancellationToken)
        {
            try
            {
                SaveSnapshot(snapshot, rawAccessSecret, machineName);
                var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, _configStore.UnprotectAccessSecret(snapshot));
                var metadata = await _relayClient.GetMetadataAsync(config, cancellationToken).ConfigureAwait(false);
                config.RelaySigningPublicKey = metadata.SigningPublicKey;
                config.RelaySigningKeyFingerprint = metadata.SigningKeyFingerprint;
                snapshot.RelaySigningPublicKey = metadata.SigningPublicKey;
                snapshot.RelaySigningKeyFingerprint = metadata.SigningKeyFingerprint;
                _configStore.Save(snapshot);

                await _relayClient.EnrollHostAsync(config, identity, cancellationToken).ConfigureAwait(false);
                await _relayClient.ListDevicesAsync(config, snapshot.HostId, cancellationToken).ConfigureAwait(false);

                snapshot.LastRegisteredAt = DateTime.UtcNow;
                _configStore.Save(snapshot);
                return new RemoteServiceConnectionResult
                {
                    Success = true,
                    Stage = RemoteServiceConnectionStage.Registered,
                    Snapshot = snapshot
                };
            }
            catch (Exception ex)
            {
                return new RemoteServiceConnectionResult
                {
                    Success = false,
                    Stage = RemoteServiceConnectionStage.TestConnection,
                    Snapshot = snapshot,
                    Error = ex
                };
            }
        }

        private void SaveSnapshot(RemoteServiceConfigSnapshot snapshot, string rawAccessSecret, string machineName)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, machineName);
            if (!string.IsNullOrWhiteSpace(rawAccessSecret))
            {
                _configStore.ProtectAccessSecret(snapshot, rawAccessSecret);
            }

            _configStore.Save(snapshot);
        }
    }
}
