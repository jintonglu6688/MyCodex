using System;
using System.Security.Cryptography;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CngSecureRemoteCryptoTests
    {
        [Test]
        public void HkdfSha256_Rfc5869CaseOneMatches()
        {
            var crypto = new CngSecureRemoteCrypto();
            var ikm = Repeat(0x0b, 22);
            var salt = Hex("000102030405060708090a0b0c");
            var info = Hex("f0f1f2f3f4f5f6f7f8f9");

            var actual = crypto.HkdfSha256(ikm, salt, info, 42);

            Assert.That(actual, Is.EqualTo(Hex(
                "3cb25f25faacd57a90434f64d0362f2a" +
                "2d2d0a90cf1a5a4c5db02d56ecc4c5bf" +
                "34007208d5b887185865")));
        }

        [Test]
        public void AesGcmVector_EncryptsAndDecrypts()
        {
            var vector = SecureRemoteVector.Load();
            var crypto = new CngSecureRemoteCrypto();

            AssertAesGcm(
                crypto,
                vector.Bytes("pairing", "claimKey"),
                vector.HexBytes("inputs", "claimNonceHex"),
                vector.Bytes("pairing", "claimPlaintext"),
                vector.Bytes("transcripts", "visibleClaimHeader"),
                vector.Bytes("pairing", "claimCiphertext"));
            AssertAesGcm(
                crypto,
                vector.Bytes("pairing", "approvalKey"),
                vector.HexBytes("inputs", "approvalNonceHex"),
                vector.Bytes("pairing", "approvalPlaintext"),
                vector.Bytes("transcripts", "approvalHeader"),
                vector.Bytes("pairing", "approvalCiphertext"));
            AssertAesGcm(
                crypto,
                vector.Bytes("session", "androidToWindowsKey"),
                vector.Bytes("envelopes", "androidToWindows", "nonce"),
                vector.Bytes("envelopes", "androidToWindows", "plaintext"),
                vector.Bytes("transcripts", "envelopeAadAndroidToWindows"),
                vector.Bytes("envelopes", "androidToWindows", "ciphertext"));
            AssertAesGcm(
                crypto,
                vector.Bytes("session", "windowsToAndroidKey"),
                vector.Bytes("envelopes", "windowsToAndroid", "nonce"),
                vector.Bytes("envelopes", "windowsToAndroid", "plaintext"),
                vector.Bytes("transcripts", "envelopeAadWindowsToAndroid"),
                vector.Bytes("envelopes", "windowsToAndroid", "ciphertext"));
        }

        [Test]
        public void RawSignatureVector_Verifies()
        {
            var vector = SecureRemoteVector.Load();
            var crypto = new CngSecureRemoteCrypto();

            AssertSignature(
                crypto,
                vector,
                "hostSigning",
                "pairingPackage",
                "hostPairingPackage");
            AssertSignature(
                crypto,
                vector,
                "deviceSigning",
                "pairingClaimForSignature",
                "devicePairingClaim");
            AssertSignature(
                crypto,
                vector,
                "relaySigning",
                "relayChallenge",
                "relayChallenge");
            AssertSignature(
                crypto,
                vector,
                "deviceSigning",
                "relayProof",
                "deviceRelayProof");
            AssertSignature(
                crypto,
                vector,
                "deviceSigning",
                "sessionClientHello",
                "deviceSessionHello");
            AssertSignature(
                crypto,
                vector,
                "hostSigning",
                "sessionServerHello",
                "hostSessionHello");
            AssertSignature(
                crypto,
                vector,
                "hostSigning",
                "pairingApproval",
                "hostPairingApproval");
        }

        [Test]
        public void RawSignatureVerifier_MalformedWireValuesReturnFalse()
        {
            var vector = SecureRemoteVector.Load();
            var crypto = new CngSecureRemoteCrypto();
            var publicKey = vector.Bytes("publicKeys", "hostSigning");
            var transcript = vector.Bytes("transcripts", "pairingPackage");
            var signature = vector.Bytes("signatures", "hostPairingPackage");
            var wrongPrefix = (byte[])publicKey.Clone();
            wrongPrefix[0] = 0x02;
            var offCurve = new byte[65];
            offCurve[0] = 0x04;
            var tamperedSignature = (byte[])signature.Clone();
            tamperedSignature[0] ^= 0x01;

            Assert.That(crypto.VerifyRawEcdsa(new byte[64], transcript, signature), Is.False);
            Assert.That(crypto.VerifyRawEcdsa(wrongPrefix, transcript, signature), Is.False);
            Assert.That(crypto.VerifyRawEcdsa(offCurve, transcript, signature), Is.False);
            Assert.That(crypto.VerifyRawEcdsa(publicKey, transcript, new byte[63]), Is.False);
            Assert.That(crypto.VerifyRawEcdsa(publicKey, transcript, tamperedSignature), Is.False);
        }

        [Test]
        public void EcdhAndHkdfVector_Matches()
        {
            var vector = SecureRemoteVector.Load();
            var crypto = new CngSecureRemoteCrypto();
            var privateScalar = new byte[32];
            privateScalar[31] = 3;

            using (var privateKey = ImportEcdhPrivateKey(
                privateScalar,
                vector.Bytes("publicKeys", "hostAgreement")))
            {
                var sharedSecret = crypto.DeriveAgreement(
                    privateKey,
                    vector.Bytes("publicKeys", "deviceAgreement"));

                try
                {
                    Assert.That(sharedSecret, Is.EqualTo(vector.Bytes("pairing", "ecdhSharedSecret")));
                    Assert.That(
                        Hash(vector.HexBytes("inputs", "inviteSecretHex")),
                        Is.EqualTo(vector.Bytes("pairing", "saltSha256")));
                    Assert.That(
                        crypto.HkdfSha256(
                            sharedSecret,
                            vector.Bytes("pairing", "saltSha256"),
                            vector.Bytes("pairing", "infoSha256"),
                            32),
                        Is.EqualTo(vector.Bytes("pairing", "pairingBaseKey")));
                }
                finally
                {
                    Array.Clear(sharedSecret, 0, sharedSecret.Length);
                }
            }

            privateScalar[31] = 7;
            using (var privateKey = ImportEcdhPrivateKey(
                privateScalar,
                vector.Bytes("publicKeys", "sessionClientEphemeral")))
            {
                var sharedSecret = crypto.DeriveAgreement(
                    privateKey,
                    vector.Bytes("publicKeys", "sessionServerEphemeral"));
                try
                {
                    var salt = Hash(Join(
                        vector.HexBytes("inputs", "sessionClientNonceHex"),
                        vector.HexBytes("inputs", "sessionServerNonceHex")));
                    var sessionMaster = crypto.HkdfSha256(
                        sharedSecret,
                        salt,
                        vector.Bytes("transcripts", "sessionComplete"),
                        32);
                    try
                    {
                        Assert.That(sharedSecret, Is.EqualTo(vector.Bytes("session", "ecdhSharedSecret")));
                        Assert.That(salt, Is.EqualTo(vector.Bytes("session", "saltSha256")));
                        Assert.That(sessionMaster, Is.EqualTo(vector.Bytes("session", "sessionMaster")));
                        Assert.That(
                            crypto.HkdfSha256(
                                sessionMaster,
                                new byte[0],
                                System.Text.Encoding.UTF8.GetBytes("android to windows key"),
                                32),
                            Is.EqualTo(vector.Bytes("session", "androidToWindowsKey")));
                        Assert.That(
                            crypto.HkdfSha256(
                                sessionMaster,
                                new byte[0],
                                System.Text.Encoding.UTF8.GetBytes("windows to android key"),
                                32),
                            Is.EqualTo(vector.Bytes("session", "windowsToAndroidKey")));
                        Assert.That(
                            crypto.HkdfSha256(
                                sessionMaster,
                                new byte[0],
                                System.Text.Encoding.UTF8.GetBytes("android to windows nonce prefix"),
                                4),
                            Is.EqualTo(vector.Bytes("session", "androidToWindowsNoncePrefix")));
                        Assert.That(
                            crypto.HkdfSha256(
                                sessionMaster,
                                new byte[0],
                                System.Text.Encoding.UTF8.GetBytes("windows to android nonce prefix"),
                                4),
                            Is.EqualTo(vector.Bytes("session", "windowsToAndroidNoncePrefix")));
                    }
                    finally
                    {
                        Array.Clear(salt, 0, salt.Length);
                        Array.Clear(sessionMaster, 0, sessionMaster.Length);
                    }
                }
                finally
                {
                    Array.Clear(sharedSecret, 0, sharedSecret.Length);
                }
            }
        }

        [Test]
        public void LeadingZeroEcdhVector_UsesFixedWidthBigEndianSecret()
        {
            var vector = SecureRemoteVector.Load();
            var crypto = new CngSecureRemoteCrypto();
            var scalar = new byte[32];
            scalar[31] = byte.Parse(vector.String("leadingZeroEcdh", "privateScalar"));
            using (var key = ImportEcdhPrivateKey(scalar, vector.Bytes("publicKeys", "sessionServerEphemeral")))
            {
                var secret = crypto.DeriveAgreement(key, vector.Bytes("publicKeys", "sessionServerEphemeral"));
                try
                {
                    Assert.That(secret, Is.EqualTo(vector.Bytes("leadingZeroEcdh", "sharedSecret")));
                    Assert.That(secret.Length, Is.EqualTo(32));
                    Assert.That(secret[0], Is.EqualTo(0));
                }
                finally { Array.Clear(secret, 0, secret.Length); }
            }
        }

        [Test]
        public void HostIdentity_LoadTwiceReturnsSamePublicKeys()
        {
            var hostId = Guid.NewGuid().ToString("D");
            try
            {
                var first = new CngRemoteIdentityStore().LoadOrCreateHostIdentity(hostId);
                var second = new CngRemoteIdentityStore().LoadOrCreateHostIdentity(hostId);

                Assert.That(second.IdentityId, Is.EqualTo(hostId));
                Assert.That(second.KeyVersion, Is.EqualTo(1));
                Assert.That(second.SigningPublicKey, Is.EqualTo(first.SigningPublicKey));
                Assert.That(second.AgreementPublicKey, Is.EqualTo(first.AgreementPublicKey));
            }
            finally
            {
                DeleteIdentity(hostId);
            }
        }

        [Test]
        public void HostIdentity_PrivateKeysCannotBeExported()
        {
            var hostId = Guid.NewGuid().ToString("D");
            try
            {
                new CngRemoteIdentityStore().LoadOrCreateHostIdentity(hostId);

                AssertPrivateExportFails(CngRemoteIdentityStore.GetSigningKeyName(hostId));
                AssertPrivateExportFails(CngRemoteIdentityStore.GetAgreementKeyName(hostId));
            }
            finally
            {
                DeleteIdentity(hostId);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HostIdentity_PartialPersistedIdentityIsRejected(bool signingKeyExists)
        {
            var hostId = Guid.NewGuid().ToString("D");
            var signingKeyName = CngRemoteIdentityStore.GetSigningKeyName(hostId);
            var agreementKeyName = CngRemoteIdentityStore.GetAgreementKeyName(hostId);
            try
            {
                CreateIdentityKey(
                    signingKeyExists ? signingKeyName : agreementKeyName,
                    signingKeyExists ? CngAlgorithm.ECDsaP256 : CngAlgorithm.ECDiffieHellmanP256,
                    signingKeyExists ? CngKeyUsages.Signing : CngKeyUsages.KeyAgreement);

                Assert.Throws<CryptographicException>(
                    () => new CngRemoteIdentityStore().LoadOrCreateHostIdentity(hostId));
                Assert.That(CngKey.Exists(signingKeyName), Is.EqualTo(signingKeyExists));
                Assert.That(CngKey.Exists(agreementKeyName), Is.EqualTo(!signingKeyExists));
            }
            finally
            {
                DeleteIdentity(hostId);
            }
        }

        [Test]
        public void HostIdentity_PersistedKeyUsageMustMatchRole()
        {
            var hostId = Guid.NewGuid().ToString("D");
            try
            {
                CreateIdentityKey(
                    CngRemoteIdentityStore.GetSigningKeyName(hostId),
                    CngAlgorithm.ECDsaP256,
                    CngKeyUsages.AllUsages);
                CreateIdentityKey(
                    CngRemoteIdentityStore.GetAgreementKeyName(hostId),
                    CngAlgorithm.ECDiffieHellmanP256,
                    CngKeyUsages.AllUsages);

                Assert.Throws<CryptographicException>(
                    () => new CngRemoteIdentityStore().LoadOrCreateHostIdentity(hostId));
            }
            finally
            {
                DeleteIdentity(hostId);
            }
        }

        private static void AssertPrivateExportFails(string keyName)
        {
            using (var key = CngKey.Open(keyName))
            {
                Assert.That(key.ExportPolicy, Is.EqualTo(CngExportPolicies.None));
                Assert.Throws<CryptographicException>(() => key.Export(CngKeyBlobFormat.EccPrivateBlob));
            }
        }

        private static void DeleteIdentity(string hostId)
        {
            DeleteKey(CngRemoteIdentityStore.GetSigningKeyName(hostId));
            DeleteKey(CngRemoteIdentityStore.GetAgreementKeyName(hostId));
        }

        private static void DeleteKey(string keyName)
        {
            if (!CngKey.Exists(keyName))
            {
                return;
            }

            using (var key = CngKey.Open(keyName))
            {
                key.Delete();
            }
        }

        private static void CreateIdentityKey(
            string keyName,
            CngAlgorithm algorithm,
            CngKeyUsages keyUsage)
        {
            var parameters = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = keyUsage,
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
            };

            using (CngKey.Create(algorithm, keyName, parameters))
            {
            }
        }

        private static byte[] Repeat(byte value, int count)
        {
            var result = new byte[count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = value;
            }

            return result;
        }

        private static byte[] Hex(string value)
        {
            var result = new byte[value.Length / 2];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }

            return result;
        }

        private static byte[] Join(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static void AssertAesGcm(
            CngSecureRemoteCrypto crypto,
            byte[] key,
            byte[] nonce,
            byte[] plaintext,
            byte[] aad,
            byte[] expected)
        {
            byte[] tag;
            var ciphertext = crypto.EncryptAesGcm(key, nonce, plaintext, aad, out tag);
            var decrypted = crypto.DecryptAesGcm(key, nonce, ciphertext, tag, aad);

            Assert.That(Join(ciphertext, tag), Is.EqualTo(expected));
            Assert.That(decrypted, Is.EqualTo(plaintext));
        }

        private static void AssertSignature(
            CngSecureRemoteCrypto crypto,
            SecureRemoteVector vector,
            string publicKey,
            string transcript,
            string signature)
        {
            Assert.That(
                crypto.VerifyRawEcdsa(
                    vector.Bytes("publicKeys", publicKey),
                    vector.Bytes("transcripts", transcript),
                    vector.Bytes("signatures", signature)),
                Is.True);
        }

        private static byte[] Hash(byte[] value)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(value);
            }
        }

        private static CngKey ImportEcdhPrivateKey(byte[] scalar, byte[] publicKey)
        {
            const int privateMagic = 0x324b4345;
            var blob = new byte[8 + 32 + 32 + 32];
            WriteInt32LittleEndian(blob, 0, privateMagic);
            WriteInt32LittleEndian(blob, 4, 32);
            Buffer.BlockCopy(publicKey, 1, blob, 8, 64);
            Buffer.BlockCopy(scalar, 0, blob, 72, 32);
            try
            {
                return CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob);
            }
            finally
            {
                Array.Clear(blob, 0, blob.Length);
            }
        }

        private static void WriteInt32LittleEndian(byte[] destination, int offset, int value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }
    }
}
