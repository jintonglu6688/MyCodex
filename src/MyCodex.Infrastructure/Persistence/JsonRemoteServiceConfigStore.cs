using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class JsonRemoteServiceConfigStore : IRemoteServiceConfigStore
    {
        private readonly AppConfigService _appConfigService;
        private readonly SeparatedRemoteServiceConfigStore _separatedStore;
        private readonly DataContractJsonSerializer _serializer = new DataContractJsonSerializer(typeof(RemoteServiceConfigSnapshot));

        public JsonRemoteServiceConfigStore(AppConfigService appConfigService)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            if (appConfigService.HasSeparateDataRoot)
            {
                _separatedStore = new SeparatedRemoteServiceConfigStore(
                    Path.Combine(appConfigService.GetSharedDataDirectory(), "config", "relay-service.json"),
                    appConfigService.GetRemoteServiceConfigFilePath());
            }
        }

        public JsonRemoteServiceConfigStore(string sharedConfigFilePath, string targetIdentityFilePath)
        {
            _separatedStore = new SeparatedRemoteServiceConfigStore(sharedConfigFilePath, targetIdentityFilePath);
        }

        internal T WithTargetIdentityLock<T>(Func<T> action)
        {
            if (_separatedStore == null) throw new InvalidOperationException("Target identity transactions require separated configuration.");
            return _separatedStore.WithTargetIdentityLock(action);
        }

        public RemoteServiceConfigSnapshot Load()
        {
            if (_separatedStore != null)
            {
                return _separatedStore.Load();
            }

            var filePath = _appConfigService.GetRemoteServiceConfigFilePath();
            if (!File.Exists(filePath))
            {
                return new RemoteServiceConfigSnapshot();
            }

            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    var snapshot = _serializer.ReadObject(stream) as RemoteServiceConfigSnapshot ?? new RemoteServiceConfigSnapshot();
                    RemoteServiceConfigPlanner.NormalizeProfiles(snapshot);
                    return snapshot;
                }
            }
            catch (SerializationException)
            {
                return new RemoteServiceConfigSnapshot();
            }
            catch (IOException)
            {
                return new RemoteServiceConfigSnapshot();
            }
            catch (UnauthorizedAccessException)
            {
                return new RemoteServiceConfigSnapshot();
            }
        }

        public void Save(RemoteServiceConfigSnapshot snapshot)
        {
            if (_separatedStore != null)
            {
                _separatedStore.Save(snapshot);
                return;
            }

            _appConfigService.EnsureConfigDirectory();
            var filePath = _appConfigService.GetRemoteServiceConfigFilePath();
            var value = snapshot ?? new RemoteServiceConfigSnapshot();
            RemoteServiceConfigPlanner.CaptureActiveProfile(value);
            if (value.CreatedAt == default(DateTime))
            {
                value.CreatedAt = DateTime.UtcNow;
            }

            value.UpdatedAt = DateTime.UtcNow;
            AtomicFileWriter.Write(filePath, stream => _serializer.WriteObject(stream, value));
        }

        public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(accessSecret))
            {
                snapshot.EncryptedAccessSecret = null;
                snapshot.AccessSecretSuffix = null;
                return;
            }

            var trimmed = accessSecret.Trim();
            snapshot.EncryptedAccessSecret = RemoteSshPasswordProtector.Protect(trimmed);
            snapshot.AccessSecretSuffix = BuildSuffix(trimmed);
        }

        public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret))
            {
                return null;
            }

            return RemoteSshPasswordProtector.Unprotect(snapshot.EncryptedAccessSecret);
        }

        public void Clear()
        {
            if (_separatedStore != null)
            {
                _separatedStore.ClearTargetIdentity();
                return;
            }

            TryDelete(_appConfigService.GetRemoteServiceConfigFilePath());
        }

        public SharedRelayServiceConfigSnapshot LoadShared()
        {
            return _separatedStore != null
                ? _separatedStore.LoadShared()
                : RemoteServiceConfigProjection.SplitShared(Load());
        }

        public void SaveShared(SharedRelayServiceConfigSnapshot shared)
        {
            if (_separatedStore != null)
            {
                _separatedStore.SaveShared(shared);
                return;
            }

            var current = Load();
            Save(RemoteServiceConfigProjection.Compose(
                SeparatedRemoteServiceConfigStore.NormalizeSharedRequest(
                    shared, RemoteServiceConfigProjection.SplitShared(current)),
                RemoteServiceConfigProjection.SplitTargetIdentity(current)));
        }

        internal void SaveSharedFromService(SharedRelayServiceConfigSnapshot shared)
        {
            if (_separatedStore == null)
            {
                throw new InvalidOperationException("The shared Relay helper requires separated configuration storage.");
            }

            _separatedStore.SaveSharedFromService(shared);
        }

        internal void SaveSharedForManagement(SharedRelayServiceConfigSnapshot shared, string[] confirmedLeaseIds, Action validateOwners = null)
        {
            if (_separatedStore == null) throw new InvalidOperationException("Shared settings require separated configuration.");
            _separatedStore.SaveSharedForManagement(shared, confirmedLeaseIds, validateOwners);
        }

        public TargetRemoteIdentitySnapshot LoadTargetIdentity()
        {
            return _separatedStore != null
                ? _separatedStore.LoadTargetIdentity()
                : RemoteServiceConfigProjection.SplitTargetIdentity(Load());
        }

        public void SaveTargetIdentity(TargetRemoteIdentitySnapshot identity)
        {
            if (_separatedStore != null)
            {
                _separatedStore.SaveTargetIdentity(identity);
                return;
            }

            Save(RemoteServiceConfigProjection.Compose(
                RemoteServiceConfigProjection.SplitShared(Load()),
                identity));
        }

        private static string BuildSuffix(string value)
        {
            var normalized = value ?? string.Empty;
            var suffixLength = Math.Min(4, normalized.Length);
            return suffixLength == 0 ? null : normalized.Substring(normalized.Length - suffixLength, suffixLength);
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }
    }
}
