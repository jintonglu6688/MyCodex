using System;
using System.IO;
using System.Runtime.CompilerServices;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.RemoteRelay
{
    // The helper owns only the service configuration, never a target's HostId or registration.
    internal sealed class SharedRelayConfigStore : IRemoteServiceConfigStore
    {
        private readonly JsonRemoteServiceConfigStore _store;
        private readonly ConditionalWeakTable<RemoteServiceConfigSnapshot, SharedRelayServiceConfigSnapshot> _baselines =
            new ConditionalWeakTable<RemoteServiceConfigSnapshot, SharedRelayServiceConfigSnapshot>();

        public SharedRelayConfigStore(string sharedRoot)
        {
            _store = new JsonRemoteServiceConfigStore(
                Path.Combine(sharedRoot, "config", "relay-service.json"),
                Path.Combine(sharedRoot, "Relay", "unused-target-identity.json"));
        }

        public RemoteServiceConfigSnapshot Load()
        {
            var shared = _store.LoadShared();
            var snapshot = RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot
            {
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            RemoteServiceConfigPlanner.SelectMode(snapshot, "Embedded");
            _baselines.Add(snapshot, shared);
            return snapshot;
        }

        public void Save(RemoteServiceConfigSnapshot snapshot)
        {
            SharedRelayServiceConfigSnapshot baseline;
            if (!_baselines.TryGetValue(snapshot, out baseline))
            {
                throw new InvalidOperationException("Shared Relay configuration must be loaded before it is changed.");
            }

            RemoteServiceConfigPlanner.CaptureActiveProfile(snapshot);
            var mode = baseline.RemoteServiceMode;
            RemoteServiceConfigPlanner.SelectMode(snapshot, mode);
            var changed = RemoteServiceConfigProjection.SplitShared(snapshot);
            foreach (var property in typeof(SharedRelayServiceConfigSnapshot).GetProperties())
            {
                property.SetValue(baseline, property.GetValue(changed));
            }

            // The helper already holds SharedRelayStateStore's lifecycle lock.
            _store.SaveSharedFromService(baseline);
            RemoteServiceConfigPlanner.SelectMode(snapshot, "Embedded");
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
            throw new InvalidOperationException("A target cannot clear shared Relay credentials.");
        }
    }
}
