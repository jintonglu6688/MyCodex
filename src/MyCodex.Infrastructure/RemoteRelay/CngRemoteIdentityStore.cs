using System;
using System.Security.Cryptography;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class CngRemoteIdentityStore : IRemoteIdentityStore
    {
        private const string KeyNamePrefix = "MyCodex.SecureRemote.v1.";
        private readonly CngSecureRemoteCrypto _crypto = new CngSecureRemoteCrypto();
        private string _hostId;

        public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId)
        {
            var canonicalHostId = CanonicalizeHostId(hostId);
            var signingKeyName = GetSigningKeyName(canonicalHostId);
            var agreementKeyName = GetAgreementKeyName(canonicalHostId);
            var signingKeyExists = KeyExists(signingKeyName);
            var agreementKeyExists = KeyExists(agreementKeyName);

            if (signingKeyExists != agreementKeyExists)
            {
                throw new CryptographicException("The persisted host identity is incomplete.");
            }

            if (!signingKeyExists)
            {
                EnsureKey(signingKeyName, CngAlgorithm.ECDsaP256, CngKeyUsages.Signing);
                EnsureKey(agreementKeyName, CngAlgorithm.ECDiffieHellmanP256, CngKeyUsages.KeyAgreement);
            }

            byte[] signingPublicKey;
            byte[] agreementPublicKey;
            using (var signingKey = OpenKey(signingKeyName))
            using (var agreementKey = OpenKey(agreementKeyName))
            {
                ValidateKey(signingKey, CngAlgorithm.ECDsaP256, CngKeyUsages.Signing);
                ValidateKey(
                    agreementKey,
                    CngAlgorithm.ECDiffieHellmanP256,
                    CngKeyUsages.KeyAgreement);
                signingPublicKey = CngSecureRemoteCrypto.ExportSec1PublicKey(signingKey);
                agreementPublicKey = CngSecureRemoteCrypto.ExportSec1PublicKey(agreementKey);
            }

            _hostId = canonicalHostId;
            return new RemoteEndpointIdentity
            {
                IdentityId = canonicalHostId,
                KeyVersion = 1,
                SigningPublicKey = SecureRemoteBase64Url.Encode(signingPublicKey),
                AgreementPublicKey = SecureRemoteBase64Url.Encode(agreementPublicKey)
            };
        }

        public byte[] Sign(byte[] transcript)
        {
            if (transcript == null)
            {
                throw new ArgumentNullException(nameof(transcript));
            }

            using (var key = OpenLoadedKey(GetSigningKeyName))
            using (var signer = new ECDsaCng(key))
            {
                signer.HashAlgorithm = CngAlgorithm.Sha256;
                var signature = signer.SignData(transcript);
                if (signature.Length != 64)
                {
                    Array.Clear(signature, 0, signature.Length);
                    throw new CryptographicException("CNG returned an invalid ECDSA signature length.");
                }

                return signature;
            }
        }

        public byte[] DeriveAgreement(byte[] peerPublicKey)
        {
            using (var key = OpenLoadedKey(GetAgreementKeyName))
            {
                return _crypto.DeriveAgreement(key, peerPublicKey);
            }
        }

        internal static string GetSigningKeyName(string hostId)
        {
            return KeyNamePrefix + CanonicalizeHostId(hostId) + ".signing";
        }

        internal static string GetAgreementKeyName(string hostId)
        {
            return KeyNamePrefix + CanonicalizeHostId(hostId) + ".agreement";
        }

        private static string CanonicalizeHostId(string hostId)
        {
            Guid value;
            if (!Guid.TryParseExact(hostId, "D", out value))
            {
                throw new ArgumentException("The host ID must be a canonical UUID.", nameof(hostId));
            }

            var canonical = value.ToString("D");
            if (!string.Equals(hostId, canonical, StringComparison.Ordinal))
            {
                throw new ArgumentException("The host ID must be a lowercase canonical UUID.", nameof(hostId));
            }

            return canonical;
        }

        private static void EnsureKey(string keyName, CngAlgorithm algorithm, CngKeyUsages keyUsage)
        {
            if (!KeyExists(keyName))
            {
                var parameters = new CngKeyCreationParameters
                {
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = keyUsage,
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
                };

                try
                {
                    using (CngKey.Create(algorithm, keyName, parameters))
                    {
                    }
                }
                catch (CryptographicException)
                {
                    if (!KeyExists(keyName))
                    {
                        throw;
                    }
                }
            }

            using (var key = OpenKey(keyName))
            {
                ValidateKey(key, algorithm, keyUsage);
            }
        }

        private static bool KeyExists(string keyName)
        {
            return CngKey.Exists(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        }

        private static CngKey OpenKey(string keyName)
        {
            return CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        }

        private CngKey OpenLoadedKey(Func<string, string> getKeyName)
        {
            if (_hostId == null)
            {
                throw new InvalidOperationException("LoadOrCreateHostIdentity must be called first.");
            }

            return OpenKey(getKeyName(_hostId));
        }

        private static void ValidateKey(
            CngKey key,
            CngAlgorithm expectedAlgorithm,
            CngKeyUsages expectedKeyUsage)
        {
            if (key.Algorithm != expectedAlgorithm ||
                key.KeyUsage != expectedKeyUsage ||
                key.ExportPolicy != CngExportPolicies.None ||
                key.IsMachineKey)
            {
                throw new CryptographicException("The persisted host identity key is invalid.");
            }
        }

    }
}
