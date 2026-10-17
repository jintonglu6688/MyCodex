using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class TargetRemovalResult
    {
        public bool Removed { get; set; }
        public bool RelayCleanupPending { get; set; }
        public string RetainedArchivePath { get; set; }
    }

    public sealed class TargetRemovalService
    {
        private readonly TargetCatalogStore _catalog;
        private readonly string _channel;
        private readonly IRelayHostClient _relayClient;

        public TargetRemovalService(TargetCatalogStore catalog, string channel, IRelayHostClient relayClient = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _channel = channel;
            _relayClient = relayClient ?? new RelayHostClient();
        }

        public async Task<TargetRemovalResult> RemoveAsync(string id)
        {
            _catalog.ValidateLocalRemoval(id);
            var root = _catalog.GetSharedRoot();
            var operationId = Guid.NewGuid().ToString("N");
            var reservation = await Task.Run(() => TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, id, root));
            if (reservation == null)
            {
                var status = await Task.Run(() => TargetInstanceCoordinator.Send(_channel, id, root,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Status }));
                if (!status.Success || status.Instance == null) return new TargetRemovalResult();
                var owner = status.Instance;
                var committed = false;
                try
                {
                    var deadline = DateTime.UtcNow.AddMinutes(10);
                    TargetInstanceReply prepared;
                    do
                    {
                        prepared = await Task.Run(() => TargetInstanceCoordinator.SendToInstance(owner, root,
                            new TargetInstanceRequest { Operation = TargetInstanceOperation.PrepareExit, OperationId = operationId }));
                        if (!prepared.Success) return new TargetRemovalResult();
                        if (prepared.ExitPreparation != TargetExitPreparation.Pending) break;
                        await Task.Delay(200);
                    } while (DateTime.UtcNow < deadline);
                    if (prepared.ExitPreparation != TargetExitPreparation.Ready) return new TargetRemovalResult();
                    var commit = await Task.Run(() => TargetInstanceCoordinator.SendToInstance(owner, root,
                        new TargetInstanceRequest { Operation = TargetInstanceOperation.CommitExit, OperationId = operationId }));
                    if (!commit.Success) return new TargetRemovalResult();
                    committed = true;
                    deadline = DateTime.UtcNow.AddSeconds(15);
                    do
                    {
                        reservation = await Task.Run(() => TargetInstanceCoordinator.TryReserveOfflineEdit(_channel, id, root));
                        if (reservation != null) break;
                        await Task.Delay(200);
                    } while (DateTime.UtcNow < deadline);
                    if (reservation == null) return new TargetRemovalResult();
                }
                finally
                {
                    if (!committed)
                        await Task.Run(() => TargetInstanceCoordinator.SendToInstance(owner, root,
                            new TargetInstanceRequest { Operation = TargetInstanceOperation.AbortExit, OperationId = operationId }));
                }
            }
            using (reservation)
            {
                // Revalidate under the offline reservation before revoking any retained identity.
                _catalog.ValidateLocalRemoval(id);
                var result = new TargetRemovalResult();
                var paths = _catalog.GetPaths(id);
                var store = new JsonRemoteServiceConfigStore(paths);
                var snapshot = store.Load();
                var bindings = new JsonRemoteBindingStore(paths.GetRemoteBindingsFilePath());
                var active = bindings.ListActive(snapshot.TenantId, snapshot.HostId);
                // Retirement is the local authorization boundary. A failed catalog save must retain pairing.
                result.RetainedArchivePath = _catalog.RemoveLocalData(id);
                result.Removed = true;
                if (active.Count != 0)
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        try
                        {
                            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, store.UnprotectAccessSecret(snapshot));
                            foreach (var binding in active)
                                if (!await _relayClient.RevokeDeviceAsync(config, snapshot.HostId, binding.DeviceId, timeout.Token).ConfigureAwait(false))
                                    result.RelayCleanupPending = true;
                        }
                        catch { result.RelayCleanupPending = true; }
                    }
                }
                return result;
            }
        }
    }
}
