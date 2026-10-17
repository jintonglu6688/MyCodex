using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class JsonRemoteBindingStore : IRemoteBindingStore
    {
        private const int CurrentVersion = 1;
        private readonly object _sync = new object();
        private readonly string _filePath;
        private readonly DataContractJsonSerializer _serializer =
            new DataContractJsonSerializer(typeof(BindingFile));

        public JsonRemoteBindingStore(string filePath)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("A binding file path is required.", nameof(filePath))
                : Path.GetFullPath(filePath);
        }

        public IReadOnlyList<RemoteDeviceBinding> ListActive(string tenantId, string hostId)
        {
            lock (_sync)
            {
                return Load().Bindings
                    .Where(item =>
                        item.State == RemoteBindingState.Active &&
                        string.Equals(item.TenantId, tenantId, StringComparison.Ordinal) &&
                        string.Equals(item.HostId, hostId, StringComparison.Ordinal))
                    .OrderBy(item => item.DeviceId, StringComparer.Ordinal)
                    .ToList();
            }
        }

        public RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId)
        {
            lock (_sync)
            {
                return Load().Bindings.FirstOrDefault(item =>
                    item.State == RemoteBindingState.Active &&
                    string.Equals(item.TenantId, tenantId, StringComparison.Ordinal) &&
                    string.Equals(item.HostId, hostId, StringComparison.Ordinal) &&
                    string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));
            }
        }

        public RemoteDeviceBinding FindPending(string claimId)
        {
            lock (_sync)
            {
                return Load().Bindings.FirstOrDefault(item =>
                    item.State == RemoteBindingState.Pending &&
                    string.Equals(item.ClaimId, claimId, StringComparison.Ordinal));
            }
        }

        public void SavePending(RemoteDeviceBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            lock (_sync)
            {
                var file = Load();
                var existing = file.Bindings.FirstOrDefault(item =>
                    string.Equals(item.ClaimId, binding.ClaimId, StringComparison.Ordinal));
                if (existing != null)
                {
                    if (existing.State != RemoteBindingState.Pending ||
                        !HasSameImmutableBinding(existing, binding))
                    {
                        throw new InvalidOperationException("The existing pairing binding does not match.");
                    }

                    binding.BindingVersion = existing.BindingVersion;
                    binding.State = existing.State;
                    if (string.IsNullOrWhiteSpace(existing.ApprovalCiphertext) &&
                        !string.IsNullOrWhiteSpace(binding.ApprovalCiphertext))
                    {
                        existing.ApprovalHeader = binding.ApprovalHeader;
                        existing.ApprovalNonce = binding.ApprovalNonce;
                        existing.ApprovalCiphertext = binding.ApprovalCiphertext;
                        Save(file);
                    }
                    else if (!HasSameApproval(existing, binding))
                    {
                        throw new InvalidOperationException("The prepared pairing approval does not match.");
                    }

                    return;
                }

                var history = file.Bindings
                    .Where(item =>
                        string.Equals(item.TenantId, binding.TenantId, StringComparison.Ordinal) &&
                        string.Equals(item.HostId, binding.HostId, StringComparison.Ordinal) &&
                        string.Equals(item.DeviceId, binding.DeviceId, StringComparison.Ordinal))
                    .ToList();
                if (history.Any(item =>
                    !string.Equals(item.DeviceSigningPublicKey, binding.DeviceSigningPublicKey, StringComparison.Ordinal) ||
                    !string.Equals(item.DeviceAgreementPublicKey, binding.DeviceAgreementPublicKey, StringComparison.Ordinal) ||
                    item.DeviceKeyVersion != binding.DeviceKeyVersion))
                {
                    throw new InvalidOperationException("The device identity does not match its binding history.");
                }

                var currentVersion = history.Select(item => item.BindingVersion).DefaultIfEmpty(0).Max();
                var nextVersion = checked(currentVersion + 1);
                binding.State = RemoteBindingState.Pending;
                binding.BindingVersion = nextVersion;
                file.Bindings.Add(binding);
                Save(file);
            }
        }

        public void Activate(string claimId, int bindingVersion)
        {
            lock (_sync)
            {
                var file = Load();
                var binding = file.Bindings.FirstOrDefault(item =>
                    item.State == RemoteBindingState.Pending &&
                    string.Equals(item.ClaimId, claimId, StringComparison.Ordinal));
                if (binding == null ||
                    bindingVersion <= 0 ||
                    binding.BindingVersion != bindingVersion)
                {
                    throw new InvalidOperationException("The pending binding was not found.");
                }

                foreach (var previous in file.Bindings.Where(item =>
                    !ReferenceEquals(item, binding) &&
                    item.State == RemoteBindingState.Active &&
                    string.Equals(item.TenantId, binding.TenantId, StringComparison.Ordinal) &&
                    string.Equals(item.HostId, binding.HostId, StringComparison.Ordinal) &&
                    string.Equals(item.DeviceId, binding.DeviceId, StringComparison.Ordinal)))
                {
                    previous.State = RemoteBindingState.Revoked;
                }

                binding.State = RemoteBindingState.Active;
                Save(file);
            }
        }

        public void Revoke(string tenantId, string hostId, string deviceId)
        {
            lock (_sync)
            {
                var file = Load();
                foreach (var binding in file.Bindings.Where(item =>
                    string.Equals(item.TenantId, tenantId, StringComparison.Ordinal) &&
                    string.Equals(item.HostId, hostId, StringComparison.Ordinal) &&
                    string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal)))
                {
                    binding.State = RemoteBindingState.Revoked;
                }

                Save(file);
            }
        }

        private BindingFile Load()
        {
            if (!File.Exists(_filePath))
            {
                return NewFile();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
                object versionValue;
                if (root == null ||
                    !root.TryGetValue("version", out versionValue) ||
                    (!(versionValue is int) && !(versionValue is long)))
                {
                    throw new InvalidOperationException("The remote binding file is invalid.");
                }

                var storedVersion = versionValue is int ? (int)versionValue : (long)versionValue;
                if (storedVersion != CurrentVersion)
                {
                    return NewFile();
                }

                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var file = _serializer.ReadObject(stream) as BindingFile;
                    if (file == null)
                    {
                        throw new InvalidOperationException("The remote binding file is invalid.");
                    }

                    if (file.Bindings == null)
                    {
                        throw new InvalidOperationException("The remote binding file is invalid.");
                    }

                    return file;
                }
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is SerializationException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException)
            {
                throw new InvalidOperationException("The remote binding file could not be read safely.", ex);
            }
        }

        private static bool HasSameImmutableBinding(
            RemoteDeviceBinding left,
            RemoteDeviceBinding right)
        {
            return
                string.Equals(left.ClaimId, right.ClaimId, StringComparison.Ordinal) &&
                string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal) &&
                string.Equals(left.HostId, right.HostId, StringComparison.Ordinal) &&
                string.Equals(left.DeviceId, right.DeviceId, StringComparison.Ordinal) &&
                string.Equals(left.DeviceName, right.DeviceName, StringComparison.Ordinal) &&
                string.Equals(left.Platform, right.Platform, StringComparison.Ordinal) &&
                string.Equals(left.DeviceSigningPublicKey, right.DeviceSigningPublicKey, StringComparison.Ordinal) &&
                string.Equals(left.DeviceAgreementPublicKey, right.DeviceAgreementPublicKey, StringComparison.Ordinal) &&
                left.DeviceKeyVersion == right.DeviceKeyVersion;
        }

        private static bool HasSameApproval(
            RemoteDeviceBinding left,
            RemoteDeviceBinding right)
        {
            return
                string.Equals(left.ApprovalHeader ?? string.Empty, right.ApprovalHeader ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(left.ApprovalNonce ?? string.Empty, right.ApprovalNonce ?? string.Empty, StringComparison.Ordinal) &&
                string.Equals(left.ApprovalCiphertext ?? string.Empty, right.ApprovalCiphertext ?? string.Empty, StringComparison.Ordinal);
        }

        private void Save(BindingFile file)
        {
            var directory = Path.GetDirectoryName(_filePath);
            Directory.CreateDirectory(directory);
            var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    _serializer.WriteObject(stream, file);
                    stream.Flush();
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(temporary, _filePath, null);
                }
                else
                {
                    File.Move(temporary, _filePath);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static BindingFile NewFile()
        {
            return new BindingFile
            {
                Version = CurrentVersion,
                Bindings = new List<RemoteDeviceBinding>()
            };
        }

        [DataContract]
        private sealed class BindingFile
        {
            [DataMember(Name = "version")]
            public int Version { get; set; }

            [DataMember(Name = "bindings")]
            public List<RemoteDeviceBinding> Bindings { get; set; }
        }
    }
}
