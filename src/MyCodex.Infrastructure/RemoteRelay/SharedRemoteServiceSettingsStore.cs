using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class SharedRemoteServiceUsage
    {
        public IReadOnlyList<string> TargetNames { get; internal set; }
        internal string[] Owners { get; set; }
        internal string[] LeaseIds { get; set; }
    }

    // The shared editor never saves a target identity or registers a host.
    public sealed class SharedRemoteServiceSettingsStore : IRemoteServiceConfigStore
    {
        private readonly JsonRemoteServiceConfigStore _store;
        private readonly SharedRelayStateStore _ownership;
        private readonly TargetCatalogStore _catalog;
        private readonly string _channel;
        private readonly ConditionalWeakTable<RemoteServiceConfigSnapshot, SharedRelayServiceConfigSnapshot> _drafts =
            new ConditionalWeakTable<RemoteServiceConfigSnapshot, SharedRelayServiceConfigSnapshot>();

        public SharedRemoteServiceSettingsStore(AppConfigService config, TargetCatalogStore catalog, string channel)
        {
            _store = new JsonRemoteServiceConfigStore(config ?? throw new ArgumentNullException(nameof(config)));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _channel = channel;
            _ownership = new SharedRelayStateStore(config.GetSharedDataDirectory());
        }

        public RemoteServiceConfigSnapshot Load()
        {
            var shared = _store.LoadShared();
            var view = RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            _drafts.Add(view, shared);
            return view;
        }

        public SharedRemoteServiceUsage GetUsage()
        {
            var peers = TargetInstanceCoordinator.Discover(_channel, _catalog.GetSharedRoot());
            if (peers.Any(peer => !peer.Success))
                throw new InvalidOperationException("A running target could not be verified. Retry after it finishes starting or closing.");
            var names = new List<string>();
            var owners = new List<string>();
            var records = new Dictionary<int, TargetInstanceRecord>();
            var targetNames = new Dictionary<int, string>();
            foreach (var peer in peers)
            {
                var target = _catalog.GetTarget(peer.Instance.TargetId);
                var identity = new JsonRemoteServiceConfigStore(_catalog.GetPaths(target.Id)).LoadTargetIdentity();
                records[peer.Instance.ProcessId] = peer.Instance;
                targetNames[peer.Instance.ProcessId] = target.Name;
                if (!identity.RemoteEnabled) continue;
                names.Add(target.Name);
                owners.Add(target.Id + ":" + peer.Instance.InstanceId);
            }
            var ids = _ownership.WithLock(state =>
            {
                foreach (var lease in state.Leases)
                {
                    if (!records.TryGetValue(lease.User.ProcessId, out var record) ||
                        record.StartTimeUtcTicks != lease.User.StartTimeUtcTicks ||
                        !string.Equals(record.ExecutablePath, lease.User.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A shared service user is not an available target. Its service was not changed.");
                    var name = targetNames[record.ProcessId];
                    if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
                }
                return state.Leases.Select(lease => lease.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            }, System.Threading.CancellationToken.None);
            return new SharedRemoteServiceUsage
            {
                TargetNames = names.ToArray(),
                Owners = owners.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                LeaseIds = ids
            };
        }

        public void SaveConfirmed(RemoteServiceConfigSnapshot snapshot, SharedRemoteServiceUsage confirmedUsage)
        {
            if (confirmedUsage == null) throw new ArgumentNullException(nameof(confirmedUsage));
            if (!_drafts.TryGetValue(snapshot, out var shared))
                throw new InvalidOperationException("Load the shared settings before changing them.");
            RemoteServiceConfigPlanner.CaptureActiveProfile(snapshot);
            var requested = RemoteServiceConfigProjection.SplitShared(snapshot);
            var original = RemoteServiceConfigProjection.SplitShared(
                RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot()));
            foreach (var property in typeof(SharedRelayServiceConfigSnapshot).GetProperties())
                property.SetValue(shared, property.GetValue(requested));
            try
            {
                _store.SaveSharedForManagement(shared, confirmedUsage.LeaseIds, () => ValidateOwners(confirmedUsage.Owners));
            }
            catch
            {
                foreach (var property in typeof(SharedRelayServiceConfigSnapshot).GetProperties())
                    property.SetValue(shared, property.GetValue(original));
                throw;
            }
            RemoteServiceConfigProjection.Apply(shared, new TargetRemoteIdentitySnapshot
            {
                CreatedAt = snapshot.CreatedAt,
                UpdatedAt = snapshot.UpdatedAt
            }, snapshot);
        }

        private void ValidateOwners(string[] confirmedOwners)
        {
            // Called with the lifecycle gate and configuration mutex held. Do not reacquire the file gate.
            var peers = TargetInstanceCoordinator.Discover(_channel, _catalog.GetSharedRoot());
            if (peers.Any(peer => !peer.Success))
                throw new InvalidOperationException("A running target could not be verified. Retry before saving.");
            var owners = peers.Where(peer => new JsonRemoteServiceConfigStore(
                _catalog.GetPaths(peer.Instance.TargetId)).LoadTargetIdentity().RemoteEnabled)
                .Select(peer => peer.Instance.TargetId + ":" + peer.Instance.InstanceId)
                .OrderBy(id => id, StringComparer.Ordinal);
            if (!owners.SequenceEqual(confirmedOwners, StringComparer.Ordinal))
                throw new InvalidOperationException("The affected targets changed. Review the target list again before saving.");
        }

        public bool PairingIdentityChanged(RemoteServiceConfigSnapshot snapshot)
        {
            if (!_drafts.TryGetValue(snapshot, out var shared))
                throw new InvalidOperationException("Load the shared settings before changing them.");
            var previous = RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot());
            RemoteServiceConfigPlanner.CaptureActiveProfile(snapshot);
            return !string.Equals(previous.TenantId, snapshot.TenantId, StringComparison.Ordinal) ||
                !string.Equals(_store.UnprotectAccessSecret(previous), _store.UnprotectAccessSecret(snapshot), StringComparison.Ordinal) ||
                !string.Equals(previous.RelaySigningKeyFingerprint, snapshot.RelaySigningKeyFingerprint, StringComparison.Ordinal) ||
                !string.Equals(previous.EmbeddedRelayCertificateSha256, snapshot.EmbeddedRelayCertificateSha256, StringComparison.Ordinal);
        }

        public void Save(RemoteServiceConfigSnapshot snapshot)
        {
            throw new InvalidOperationException("Confirm the shared service users before saving settings.");
        }

        public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
        {
            _store.ProtectAccessSecret(snapshot, accessSecret);
        }

        public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot)
        {
            return _store.UnprotectAccessSecret(snapshot);
        }

        public void Clear()
        {
            throw new InvalidOperationException("Shared service identity and credentials cannot be cleared by the settings editor.");
        }
    }
}
