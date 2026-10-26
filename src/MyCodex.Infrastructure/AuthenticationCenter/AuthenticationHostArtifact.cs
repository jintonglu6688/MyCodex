using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal sealed class AuthenticationHostArtifact
    {
        internal const string ManifestFileName = "mycodex-auth-host.manifest.json";
        private const int ProtocolVersion = 3;
        private const string HostVersion = "0.3.11";
        private const string UpstreamRevision = "85ad4969b3399500baaea9d46b1e08f87aba80e1";
        // Earlier releases may only be replaced: 0.2.x (protocol 2, 3.x upstream)
        // 0.3.0-0.3.2 (protocol 3, v4.0.5 upstream) and 0.3.3-0.3.11 (v4.0.7 upstream).
        private static readonly KeyValuePair<int, string>[] UpdatableIdentities =
        {
            new KeyValuePair<int, string>(2, "e0f70019b2758f5b6b9a04dd60e4689481a0c0ac"),
            new KeyValuePair<int, string>(3, "2db86e94da13365caae55bb08d09295e31500d21"),
            new KeyValuePair<int, string>(3, "790ed8009df809bedaf6c31b5ced05da46bb1f1f")
        };
        private readonly IDictionary<string, object> _manifest;
        private static readonly string[] IdentityFields =
        {
            "hostVersion", "protocolVersion", "upstreamRevision", "sourceRevision", "sourceDirty", "target", "sha256"
        };

        private AuthenticationHostArtifact(IDictionary<string, object> manifest)
        {
            _manifest = manifest;
        }

        internal static AuthenticationHostArtifact Load(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable))
            {
                throw new AuthenticationCenterException("backend_missing");
            }
            var manifestPath = Path.Combine(Path.GetDirectoryName(executable), ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new AuthenticationCenterException("backend_manifest_missing");
            }
            try
            {
                if (new FileInfo(manifestPath).Length > 65536)
                {
                    throw new AuthenticationCenterException("backend_manifest_invalid");
                }
                var manifest = new JavaScriptSerializer { MaxJsonLength = 65536 }
                    .DeserializeObject(File.ReadAllText(manifestPath)) as IDictionary<string, object>;
                if (manifest == null || !Equals(Value(manifest, "schemaVersion"), 1) ||
                    !string.Equals(Value(manifest, "executable") as string, Path.GetFileName(executable), StringComparison.Ordinal) ||
                    !(Value(manifest, "sourceDirty") is bool) ||
                    !IsHex(Value(manifest, "sourceRevision") as string, 40) ||
                    !IsHex(Value(manifest, "sha256") as string, 64))
                {
                    throw new AuthenticationCenterException("backend_manifest_invalid");
                }
                if (!Equals(Value(manifest, "protocolVersion"), ProtocolVersion) ||
                    !Equals(Value(manifest, "hostVersion"), HostVersion) ||
                    !Equals(Value(manifest, "upstreamRevision"), UpstreamRevision) ||
                    !Equals(Value(manifest, "target"), "x86_64-pc-windows-msvc"))
                {
                    throw new AuthenticationCenterException("backend_incompatible");
                }
                using (var stream = File.OpenRead(executable))
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                    if (!string.Equals(hash, (string)Value(manifest, "sha256"), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new AuthenticationCenterException("backend_integrity_failed");
                    }
                }
                return new AuthenticationHostArtifact(manifest);
            }
            catch (AuthenticationCenterException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AuthenticationCenterException("backend_manifest_invalid");
            }
        }

        internal static AuthenticationHostArtifact LoadTarget(string manifestText, string actualHash, string expectedTarget)
        {
            return LoadTargetCore(manifestText, actualHash, expectedTarget, false);
        }

        internal static AuthenticationHostArtifact LoadTargetForUpdate(string manifestText, string actualHash, string expectedTarget)
        {
            return LoadTargetCore(manifestText, actualHash, expectedTarget, true);
        }

        private static AuthenticationHostArtifact LoadTargetCore(string manifestText, string actualHash,
            string expectedTarget, bool allowDifferentRelease)
        {
            try
            {
                if (string.IsNullOrEmpty(manifestText) || manifestText.Length > 65536 ||
                    !IsHex(actualHash, 64))
                {
                    throw new AuthenticationCenterException("backend_manifest_invalid");
                }
                var manifest = new JavaScriptSerializer { MaxJsonLength = 65536 }
                    .DeserializeObject(manifestText) as IDictionary<string, object>;
                if (manifest == null || !Equals(Value(manifest, "schemaVersion"), 1) ||
                    !Equals(Value(manifest, "executable"), "mycodex-auth-host") ||
                    !(Value(manifest, "sourceDirty") is bool) ||
                    !IsHex(Value(manifest, "sourceRevision") as string, 40) ||
                    !IsHex(Value(manifest, "sha256") as string, 64))
                {
                    throw new AuthenticationCenterException("backend_manifest_invalid");
                }
                var current = Equals(Value(manifest, "protocolVersion"), ProtocolVersion) &&
                    Equals(Value(manifest, "upstreamRevision"), UpstreamRevision);
                var legacy = UpdatableIdentities.Any(identity => Equals(Value(manifest, "protocolVersion"), identity.Key) &&
                    Equals(Value(manifest, "upstreamRevision"), identity.Value));
                if (!(current || (allowDifferentRelease && legacy)) ||
                    !Equals(Value(manifest, "target"), expectedTarget) ||
                    !(Value(manifest, "hostVersion") is string hostVersion) || string.IsNullOrWhiteSpace(hostVersion) ||
                    (!allowDifferentRelease && !Equals(hostVersion, HostVersion)))
                {
                    throw new AuthenticationCenterException("backend_incompatible");
                }
                if (!string.Equals(Value(manifest, "sha256") as string, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AuthenticationCenterException("backend_integrity_failed");
                }
                return new AuthenticationHostArtifact(manifest);
            }
            catch (AuthenticationCenterException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AuthenticationCenterException("backend_manifest_invalid");
            }
        }

        internal void ValidateIdentity(IDictionary<string, object> identity)
        {
            foreach (var field in IdentityFields)
            {
                var matches = identity != null && (field == "sha256"
                    ? string.Equals(Value(identity, field) as string, Value(_manifest, field) as string, StringComparison.OrdinalIgnoreCase)
                    : Equals(Value(identity, field), Value(_manifest, field)));
                if (!matches)
                {
                    throw new AuthenticationCenterException("backend_version_mismatch");
                }
            }
        }

        private static object Value(IDictionary<string, object> values, string name)
        {
            return values.TryGetValue(name, out var value) ? value : null;
        }

        private static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value)
            {
                if (!Uri.IsHexDigit(character)) return false;
            }
            return true;
        }
    }
}
