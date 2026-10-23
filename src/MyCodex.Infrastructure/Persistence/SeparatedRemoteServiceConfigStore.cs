using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.RemoteRelay;

namespace MyCodex.Infrastructure.Persistence
{
    internal sealed class SeparatedRemoteServiceConfigStore
    {
        private readonly string _sharedPath;
        private readonly string _identityPath;
        private readonly string _mutexName;
        private readonly SharedRelayStateStore _ownership;
        private readonly ConditionalWeakTable<RemoteServiceConfigSnapshot, Baseline> _baselines =
            new ConditionalWeakTable<RemoteServiceConfigSnapshot, Baseline>();
        private readonly ConditionalWeakTable<SharedRelayServiceConfigSnapshot, SharedRelayServiceConfigSnapshot> _sharedBaselines =
            new ConditionalWeakTable<SharedRelayServiceConfigSnapshot, SharedRelayServiceConfigSnapshot>();
        private readonly ConditionalWeakTable<TargetRemoteIdentitySnapshot, TargetRemoteIdentitySnapshot> _identityBaselines =
            new ConditionalWeakTable<TargetRemoteIdentitySnapshot, TargetRemoteIdentitySnapshot>();

        public SeparatedRemoteServiceConfigStore(
            string sharedPath,
            string identityPath,
            SharedRelayStateStore ownership = null)
        {
            _sharedPath = NormalizePath(sharedPath, nameof(sharedPath));
            _identityPath = NormalizePath(identityPath, nameof(identityPath));
            if (string.Equals(_sharedPath, _identityPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Shared service and target identity must use different files.");
            }

            var sharedDirectory = Path.GetDirectoryName(_sharedPath);
            _ownership = ownership ?? new SharedRelayStateStore(Path.GetDirectoryName(sharedDirectory) ?? sharedDirectory);

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(_sharedPath.ToUpperInvariant()));
                _mutexName = @"Local\MyCodex.RemoteServiceConfig." + BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        public RemoteServiceConfigSnapshot Load()
        {
            return WithLock(() =>
            {
                var shared = ReadShared();
                var identity = Read<TargetRemoteIdentitySnapshot>(_identityPath);
                if (File.Exists(_sharedPath) && identity.CreatedAt == default(DateTime))
                {
                    // Legacy defaults tie TLS initialization to identity creation. A new target
                    // joining an existing service must retain that service's transport settings.
                    identity.CreatedAt = DateTime.UtcNow;
                }

                var snapshot = RemoteServiceConfigProjection.Compose(shared, identity);
                Remember(snapshot, shared, identity);
                return snapshot;
            });
        }

        public void Save(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            WithMutationLock(state =>
            {
                Baseline baseline;
                var hasBaseline = _baselines.TryGetValue(snapshot, out baseline);
                var requestedShared = NormalizeSharedRequest(
                    RemoteServiceConfigProjection.SplitShared(snapshot), hasBaseline ? baseline.Shared : null);
                var requestedIdentity = RemoteServiceConfigProjection.SplitTargetIdentity(snapshot);
                var currentShared = ReadShared();
                var currentIdentity = Read<TargetRemoteIdentitySnapshot>(_identityPath);
                var shared = hasBaseline
                    ? Merge(baseline.Shared, requestedShared, CloneShared(currentShared))
                    : File.Exists(_sharedPath) ? currentShared : requestedShared;
                var identity = hasBaseline
                    ? Merge(baseline.Identity, requestedIdentity, CloneIdentity(currentIdentity), nameof(TargetRemoteIdentitySnapshot.UpdatedAt))
                    : RequireInitialOrUnchanged(_identityPath, requestedIdentity, currentIdentity);
                ValidateIdentity(currentIdentity, identity);
                if (!ValuesEqual(currentShared, shared) || !File.Exists(_sharedPath))
                {
                    EnsureSharedChangeAllowed(state);
                    Write(_sharedPath, shared);
                }

                StampIdentity(identity);
                Write(_identityPath, identity);
                RemoteServiceConfigProjection.Apply(shared, identity, snapshot);
                Remember(snapshot, shared, identity);
                return true;
            });
        }

        public SharedRelayServiceConfigSnapshot LoadShared()
        {
            return WithLock(() =>
            {
                var shared = ReadShared();
                _sharedBaselines.Add(shared, CloneShared(shared));
                return shared;
            });
        }

        public void SaveShared(SharedRelayServiceConfigSnapshot shared)
        {
            WithMutationLock(state => SaveSharedCore(shared, state));
        }

        internal void SaveSharedForManagement(SharedRelayServiceConfigSnapshot shared, string[] confirmedLeaseIds, Action validateOwners = null)
        {
            if (confirmedLeaseIds == null) throw new ArgumentNullException(nameof(confirmedLeaseIds));
            WithMutationLock(state =>
            {
                validateOwners?.Invoke();
                var currentIds = state.Leases.Select(lease => lease.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                if (!currentIds.SequenceEqual(confirmedLeaseIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new InvalidOperationException("The shared service users changed. Review the affected targets again before saving.");
                var previous = ReadShared();
                var changed = SaveSharedCore(shared, null);
                if (EmbeddedConfigurationChanged(previous, shared))
                {
                    state.SettingsRevision++;
                    state.Ready = false;
                    state.Error = null;
                }
                return changed;
            });
        }

        private static bool EmbeddedConfigurationChanged(SharedRelayServiceConfigSnapshot previous, SharedRelayServiceConfigSnapshot current)
        {
            if (!RemoteServiceConfigPlanner.IsEmbeddedMode(current.RemoteServiceMode)) return false;
            return RemoteServiceConfigurationComparer.ConnectionChanged(
                RemoteServiceConfigProjection.Compose(previous, new TargetRemoteIdentitySnapshot()),
                RemoteServiceConfigProjection.Compose(current, new TargetRemoteIdentitySnapshot()));
        }

        internal void SaveSharedFromService(SharedRelayServiceConfigSnapshot shared)
        {
            // The helper already holds the lifecycle file gate while updating its own metadata.
            WithLock(() => SaveSharedCore(shared, null));
        }

        private bool SaveSharedCore(SharedRelayServiceConfigSnapshot shared, SharedRelayState state)
        {
            if (shared == null)
            {
                throw new ArgumentNullException(nameof(shared));
            }

            var current = ReadShared();
            SharedRelayServiceConfigSnapshot baseline;
            var hasBaseline = _sharedBaselines.TryGetValue(shared, out baseline);
            var requested = NormalizeSharedRequest(shared, hasBaseline ? baseline : null);
            var value = hasBaseline
                ? Merge(baseline, requested, CloneShared(current))
                : RequireInitialOrUnchanged(_sharedPath, requested, current);
            if (!ValuesEqual(current, value) || !File.Exists(_sharedPath))
            {
                if (state != null)
                {
                    EnsureSharedChangeAllowed(state);
                }

                Write(_sharedPath, value);
            }

            CopyValues(value, shared);
            _sharedBaselines.Remove(shared);
            _sharedBaselines.Add(shared, CloneShared(value));
            return true;
        }

        public TargetRemoteIdentitySnapshot LoadTargetIdentity()
        {
            return WithLock(() =>
            {
                var identity = Read<TargetRemoteIdentitySnapshot>(_identityPath);
                _identityBaselines.Add(identity, CloneIdentity(identity));
                return identity;
            });
        }

        internal T WithTargetIdentityLock<T>(Func<T> action)
        {
            return WithLock(action);
        }

        public void SaveTargetIdentity(TargetRemoteIdentitySnapshot identity)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            WithLock(() =>
            {
                var current = Read<TargetRemoteIdentitySnapshot>(_identityPath);
                TargetRemoteIdentitySnapshot baseline;
                var value = _identityBaselines.TryGetValue(identity, out baseline)
                    ? Merge(baseline, identity, CloneIdentity(current), nameof(TargetRemoteIdentitySnapshot.UpdatedAt))
                    : RequireInitialOrUnchanged(_identityPath, identity, current);
                ValidateIdentity(current, value);
                StampIdentity(value);
                Write(_identityPath, value);
                CopyValues(value, identity);
                _identityBaselines.Remove(identity);
                _identityBaselines.Add(identity, CloneIdentity(value));
                return true;
            });
        }

        public void ClearTargetIdentity()
        {
            WithLock(() =>
            {
                if (File.Exists(_identityPath))
                {
                    File.Delete(_identityPath);
                }

                return true;
            });
        }

        private SharedRelayServiceConfigSnapshot ReadShared()
        {
            var shared = Read<SharedRelayServiceConfigSnapshot>(_sharedPath);
            var normalized = RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot());
            RemoteServiceConfigPlanner.NormalizeProfiles(normalized);
            return RemoteServiceConfigProjection.SplitShared(normalized);
        }

        internal static SharedRelayServiceConfigSnapshot NormalizeSharedRequest(
            SharedRelayServiceConfigSnapshot requested,
            SharedRelayServiceConfigSnapshot baseline)
        {
            var view = RemoteServiceConfigProjection.Compose(requested, new TargetRemoteIdentitySnapshot());
            RemoteServiceConfigPlanner.NormalizeProfiles(view);
            if (baseline == null)
            {
                RemoteServiceConfigPlanner.CaptureActiveProfile(view);
            }
            else if (!string.Equals(baseline.RemoteServiceMode, requested.RemoteServiceMode, StringComparison.OrdinalIgnoreCase))
            {
                RemoteServiceConfigPlanner.SelectMode(view, requested.RemoteServiceMode);
            }
            else
            {
                var legacyFieldsChanged = false;
                foreach (var profileProperty in typeof(RemoteServiceProfileSnapshot).GetProperties())
                {
                    var legacyProperty = typeof(SharedRelayServiceConfigSnapshot).GetProperty(profileProperty.Name);
                    legacyFieldsChanged |= !ValuesEqual(legacyProperty.GetValue(baseline), legacyProperty.GetValue(requested));
                }

                if (legacyFieldsChanged)
                {
                    RemoteServiceConfigPlanner.CaptureActiveProfile(view);
                }
                else if (!ValuesEqual(
                    RemoteServiceConfigPlanner.IsEmbeddedMode(requested.RemoteServiceMode) ? baseline.EmbeddedProfile : baseline.ExternalProfile,
                    RemoteServiceConfigPlanner.IsEmbeddedMode(requested.RemoteServiceMode) ? requested.EmbeddedProfile : requested.ExternalProfile))
                {
                    RemoteServiceConfigPlanner.SelectMode(view, requested.RemoteServiceMode);
                }
            }

            return RemoteServiceConfigProjection.SplitShared(view);
        }

        private void Remember(
            RemoteServiceConfigSnapshot snapshot,
            SharedRelayServiceConfigSnapshot shared,
            TargetRemoteIdentitySnapshot identity)
        {
            _baselines.Remove(snapshot);
            _baselines.Add(snapshot, new Baseline
            {
                Shared = CloneShared(shared),
                Identity = CloneIdentity(identity)
            });
        }

        private static T RequireInitialOrUnchanged<T>(string path, T requested, T current)
        {
            if (File.Exists(path) && !ValuesEqual(requested, current))
            {
                throw new InvalidOperationException("Load the current configuration before changing existing settings.");
            }

            return requested;
        }

        private static T Read<T>(string path) where T : class, new()
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var value = new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T;
                    if (value == null)
                    {
                        throw new SerializationException("The remote configuration is empty.");
                    }

                    return value;
                }
            }
            catch (FileNotFoundException)
            {
                return new T();
            }
            catch (DirectoryNotFoundException)
            {
                return new T();
            }
        }

        private static void Write<T>(string path, T value)
        {
            AtomicFileWriter.Write(path, stream => new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value), path + ".bak");
        }

        private T WithLock<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, _mutexName))
            {
                var lockTaken = false;
                try
                {
                    try
                    {
                        lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(15));
                    }
                    catch (AbandonedMutexException)
                    {
                        lockTaken = true;
                    }

                    if (!lockTaken)
                    {
                        throw new TimeoutException("The remote configuration is currently in use.");
                    }

                    return action();
                }
                finally
                {
                    if (lockTaken)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private T WithMutationLock<T>(Func<SharedRelayState, T> action)
        {
            // Always acquire lifecycle gate before the configuration mutex, matching the helper.
            return _ownership.WithLock(state => WithLock(() => action(state)), CancellationToken.None);
        }

        private static void EnsureSharedChangeAllowed(SharedRelayState state)
        {
            if (state.Leases.Count == 0)
            {
                return;
            }

            SharedRelayProcessRecord current;
            using (var process = Process.GetCurrentProcess())
            {
                current = SharedRelayProcessRecord.Capture(process);
            }

            if (state.Leases.Count > 1 || state.Leases.Exists(lease =>
                lease.User.ProcessId != current.ProcessId ||
                lease.User.StartTimeUtcTicks != current.StartTimeUtcTicks ||
                !string.Equals(lease.User.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Another target is using the shared Relay. Its configuration was not changed.");
            }
        }

        private static T Merge<T>(T baseline, T requested, T current, string ignoredProperty = null)
        {
            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (string.Equals(property.Name, ignoredProperty, StringComparison.Ordinal))
                {
                    continue;
                }

                var originalValue = property.GetValue(baseline);
                var requestedValue = property.GetValue(requested);
                var currentValue = property.GetValue(current);
                if (ValuesEqual(originalValue, requestedValue))
                {
                    continue;
                }

                if (property.PropertyType == typeof(RemoteServiceProfileSnapshot) &&
                    originalValue != null && requestedValue != null && currentValue != null)
                {
                    property.SetValue(current, Merge(
                        (RemoteServiceProfileSnapshot)originalValue,
                        (RemoteServiceProfileSnapshot)requestedValue,
                        (RemoteServiceProfileSnapshot)currentValue));
                    continue;
                }

                if (!ValuesEqual(originalValue, currentValue) && !ValuesEqual(requestedValue, currentValue))
                {
                    throw new InvalidOperationException("Remote settings changed in another instance. Reload before saving.");
                }

                property.SetValue(current, requestedValue);
            }

            return current;
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left == null || right == null || left is string || left.GetType().IsValueType)
            {
                return Equals(left, right);
            }

            foreach (var property in left.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!ValuesEqual(property.GetValue(left), property.GetValue(right)))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CopyValues<T>(T source, T destination)
        {
            foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                property.SetValue(destination, property.GetValue(source));
            }
        }

        private static void ValidateIdentity(TargetRemoteIdentitySnapshot current, TargetRemoteIdentitySnapshot requested)
        {
            if (!string.IsNullOrEmpty(current.HostId) &&
                !string.Equals(current.HostId, requested.HostId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("An existing target's remote identity cannot be replaced.");
            }

            if (!string.IsNullOrEmpty(requested.HostId))
            {
                Guid hostId;
                if (!Guid.TryParseExact(requested.HostId, "D", out hostId) ||
                    !string.Equals(requested.HostId, hostId.ToString("D"), StringComparison.Ordinal))
                {
                    throw new ArgumentException("The host ID must be a lowercase canonical UUID.");
                }
            }
        }

        private static void StampIdentity(TargetRemoteIdentitySnapshot identity)
        {
            if (identity.CreatedAt == default(DateTime))
            {
                identity.CreatedAt = DateTime.UtcNow;
            }

            identity.UpdatedAt = DateTime.UtcNow;
        }

        private static SharedRelayServiceConfigSnapshot CloneShared(SharedRelayServiceConfigSnapshot shared)
        {
            return RemoteServiceConfigProjection.SplitShared(
                RemoteServiceConfigProjection.Compose(shared, new TargetRemoteIdentitySnapshot()));
        }

        private static TargetRemoteIdentitySnapshot CloneIdentity(TargetRemoteIdentitySnapshot identity)
        {
            return RemoteServiceConfigProjection.SplitTargetIdentity(
                RemoteServiceConfigProjection.Compose(new SharedRelayServiceConfigSnapshot(), identity));
        }

        private static string NormalizePath(string path, string parameterName)
        {
            return string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("A remote configuration file path is required.", parameterName)
                : Path.GetFullPath(path);
        }

        private sealed class Baseline
        {
            public SharedRelayServiceConfigSnapshot Shared { get; set; }

            public TargetRemoteIdentitySnapshot Identity { get; set; }
        }
    }
}
