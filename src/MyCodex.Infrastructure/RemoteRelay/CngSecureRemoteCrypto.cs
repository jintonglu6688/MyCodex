using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class CngSecureRemoteCrypto
    {
        private const int AesKeyLength = 32;
        private const int AesGcmNonceLength = 12;
        private const int AesGcmTagLength = 16;
        private const int P256PublicKeyLength = 65;
        private const int P256SignatureLength = 64;
        private const int P256CoordinateLength = 32;
        private const int BCryptEcdhPublicP256Magic = 0x314b4345;
        private const int BCryptEcdsaPublicP256Magic = 0x31534345;
        private const string BCryptAesAlgorithm = "AES";
        private const string BCryptChainingMode = "ChainingMode";
        private const string BCryptChainingModeGcm = "ChainingModeGCM";
        private const string BCryptObjectLength = "ObjectLength";
        private const string NCryptRawSecretKdf = "TRUNCATE";

        public byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            if (ikm == null)
            {
                throw new ArgumentNullException(nameof(ikm));
            }

            if (salt == null)
            {
                throw new ArgumentNullException(nameof(salt));
            }

            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (length <= 0 || length > 255 * 32)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            var effectiveSalt = salt.Length == 0 ? new byte[32] : (byte[])salt.Clone();
            byte[] pseudoRandomKey = null;
            byte[] previous = new byte[0];
            try
            {
                using (var extract = new HMACSHA256(effectiveSalt))
                {
                    pseudoRandomKey = extract.ComputeHash(ikm);
                }

                var result = new byte[length];
                var offset = 0;
                for (var counter = 1; offset < result.Length; counter++)
                {
                    var input = new byte[previous.Length + info.Length + 1];
                    Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
                    Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
                    input[input.Length - 1] = (byte)counter;

                    var next = default(byte[]);
                    try
                    {
                        using (var expand = new HMACSHA256(pseudoRandomKey))
                        {
                            next = expand.ComputeHash(input);
                        }

                        var count = Math.Min(next.Length, result.Length - offset);
                        Buffer.BlockCopy(next, 0, result, offset, count);
                        offset += count;
                    }
                    finally
                    {
                        Array.Clear(input, 0, input.Length);
                        Array.Clear(previous, 0, previous.Length);
                    }

                    previous = next;
                }

                return result;
            }
            finally
            {
                Array.Clear(effectiveSalt, 0, effectiveSalt.Length);
                if (pseudoRandomKey != null)
                {
                    Array.Clear(pseudoRandomKey, 0, pseudoRandomKey.Length);
                }

                Array.Clear(previous, 0, previous.Length);
            }
        }

        public byte[] EncryptAesGcm(
            byte[] key,
            byte[] nonce,
            byte[] plaintext,
            byte[] aad,
            out byte[] tag)
        {
            ValidateAesInputs(key, nonce, plaintext, aad);
            tag = new byte[AesGcmTagLength];
            try
            {
                return TransformAesGcm(true, key, nonce, plaintext, aad, tag);
            }
            catch
            {
                Array.Clear(tag, 0, tag.Length);
                tag = null;
                throw;
            }
        }

        public byte[] DecryptAesGcm(
            byte[] key,
            byte[] nonce,
            byte[] ciphertext,
            byte[] tag,
            byte[] aad)
        {
            ValidateAesInputs(key, nonce, ciphertext, aad);
            if (tag == null)
            {
                throw new ArgumentNullException(nameof(tag));
            }

            if (tag.Length != AesGcmTagLength)
            {
                throw new ArgumentException("AES-GCM tags must be 16 bytes.", nameof(tag));
            }

            return TransformAesGcm(false, key, nonce, ciphertext, aad, (byte[])tag.Clone());
        }

        public bool VerifyRawEcdsa(byte[] publicKey, byte[] transcript, byte[] signature)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            if (transcript == null)
            {
                throw new ArgumentNullException(nameof(transcript));
            }

            if (signature == null)
            {
                throw new ArgumentNullException(nameof(signature));
            }

            if (publicKey.Length != P256PublicKeyLength ||
                publicKey[0] != 0x04 ||
                signature.Length != P256SignatureLength)
            {
                return false;
            }

            try
            {
                using (var key = ImportPublicKey(publicKey, false))
                using (var verifier = new ECDsaCng(key))
                {
                    verifier.HashAlgorithm = CngAlgorithm.Sha256;
                    return verifier.VerifyData(transcript, signature);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public bool IsValidP256PublicKey(byte[] publicKey)
        {
            if (publicKey == null ||
                publicKey.Length != P256PublicKeyLength ||
                publicKey[0] != 0x04)
            {
                return false;
            }

            try
            {
                using (ImportPublicKey(publicKey, false))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        internal CngKey CreateEphemeralAgreementKey()
        {
            var parameters = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.KeyAgreement,
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider
            };
            return CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, parameters);
        }

        internal byte[] DeriveAgreement(CngKey privateKey, byte[] peerPublicKey)
        {
            if (privateKey == null)
            {
                throw new ArgumentNullException(nameof(privateKey));
            }

            if (privateKey.Algorithm != CngAlgorithm.ECDiffieHellmanP256)
            {
                throw new ArgumentException("The private key must be ECDH P-256.", nameof(privateKey));
            }

            ValidatePublicKey(peerPublicKey);
            using (var peerKey = ImportPublicKey(peerPublicKey, true))
            {
                IntPtr secretHandle;
                var privateHandle = privateKey.Handle;
                var peerHandle = peerKey.Handle;
                var privateAddedRef = false;
                var peerAddedRef = false;
                try
                {
                    privateHandle.DangerousAddRef(ref privateAddedRef);
                    peerHandle.DangerousAddRef(ref peerAddedRef);
                    ThrowIfError(NCryptSecretAgreement(
                        privateHandle.DangerousGetHandle(),
                        peerHandle.DangerousGetHandle(),
                        out secretHandle,
                        0));
                }
                finally
                {
                    if (peerAddedRef)
                    {
                        peerHandle.DangerousRelease();
                    }

                    if (privateAddedRef)
                    {
                        privateHandle.DangerousRelease();
                    }
                }

                try
                {
                    int requiredLength;
                    ThrowIfError(NCryptDeriveKey(
                        secretHandle,
                        NCryptRawSecretKdf,
                        IntPtr.Zero,
                        null,
                        0,
                        out requiredLength,
                        0));
                    if (requiredLength != P256CoordinateLength)
                    {
                        throw new CryptographicException("CNG returned an invalid ECDH shared-secret length.");
                    }

                    var sharedSecret = new byte[requiredLength];
                    int actualLength;
                    ThrowIfError(NCryptDeriveKey(
                        secretHandle,
                        NCryptRawSecretKdf,
                        IntPtr.Zero,
                        sharedSecret,
                        sharedSecret.Length,
                        out actualLength,
                        0));
                    if (actualLength != sharedSecret.Length)
                    {
                        Array.Clear(sharedSecret, 0, sharedSecret.Length);
                        throw new CryptographicException("CNG returned an invalid ECDH shared secret.");
                    }

                    Array.Reverse(sharedSecret);
                    return sharedSecret;
                }
                finally
                {
                    NCryptFreeObject(secretHandle);
                }
            }
        }

        internal static byte[] ExportSec1PublicKey(CngKey key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var blob = key.Export(CngKeyBlobFormat.EccPublicBlob);
            try
            {
                if (blob.Length != 8 + (2 * P256CoordinateLength) ||
                    BitConverter.ToInt32(blob, 4) != P256CoordinateLength)
                {
                    throw new CryptographicException("CNG returned an invalid P-256 public key.");
                }

                var result = new byte[P256PublicKeyLength];
                result[0] = 0x04;
                Buffer.BlockCopy(blob, 8, result, 1, 2 * P256CoordinateLength);
                return result;
            }
            finally
            {
                Array.Clear(blob, 0, blob.Length);
            }
        }

        private static byte[] TransformAesGcm(
            bool encrypt,
            byte[] key,
            byte[] nonce,
            byte[] input,
            byte[] aad,
            byte[] tag)
        {
            IntPtr algorithmHandle;
            ThrowIfError(BCryptOpenAlgorithmProvider(
                out algorithmHandle,
                BCryptAesAlgorithm,
                null,
                0));

            var keyHandle = IntPtr.Zero;
            byte[] keyObject = null;
            var keyCopy = (byte[])key.Clone();
            try
            {
                var chainingMode = Encoding.Unicode.GetBytes(BCryptChainingModeGcm + "\0");
                ThrowIfError(BCryptSetProperty(
                    algorithmHandle,
                    BCryptChainingMode,
                    chainingMode,
                    chainingMode.Length,
                    0));

                var objectLengthBytes = new byte[4];
                int propertyLength;
                ThrowIfError(BCryptGetProperty(
                    algorithmHandle,
                    BCryptObjectLength,
                    objectLengthBytes,
                    objectLengthBytes.Length,
                    out propertyLength,
                    0));
                if (propertyLength != objectLengthBytes.Length)
                {
                    throw new CryptographicException("BCrypt returned an invalid AES key-object length.");
                }

                keyObject = new byte[BitConverter.ToInt32(objectLengthBytes, 0)];
                ThrowIfError(BCryptGenerateSymmetricKey(
                    algorithmHandle,
                    out keyHandle,
                    keyObject,
                    keyObject.Length,
                    keyCopy,
                    keyCopy.Length,
                    0));

                return TransformAesGcmWithKey(encrypt, keyHandle, nonce, input, aad, tag);
            }
            finally
            {
                if (keyHandle != IntPtr.Zero)
                {
                    BCryptDestroyKey(keyHandle);
                }

                BCryptCloseAlgorithmProvider(algorithmHandle, 0);
                Array.Clear(keyCopy, 0, keyCopy.Length);
                if (keyObject != null)
                {
                    Array.Clear(keyObject, 0, keyObject.Length);
                }
            }
        }

        private static byte[] TransformAesGcmWithKey(
            bool encrypt,
            IntPtr keyHandle,
            byte[] nonce,
            byte[] input,
            byte[] aad,
            byte[] tag)
        {
            var handles = new List<GCHandle>();
            var output = new byte[input.Length];
            try
            {
                var info = new BCryptAuthenticatedCipherModeInfo
                {
                    Size = Marshal.SizeOf(typeof(BCryptAuthenticatedCipherModeInfo)),
                    Version = 1,
                    Nonce = Pin(nonce, handles),
                    NonceLength = nonce.Length,
                    AuthData = Pin(aad, handles),
                    AuthDataLength = aad.Length,
                    Tag = Pin(tag, handles),
                    TagLength = tag.Length
                };

                int actualLength;
                var status = encrypt
                    ? BCryptEncrypt(
                        keyHandle,
                        input,
                        input.Length,
                        ref info,
                        null,
                        0,
                        output,
                        output.Length,
                        out actualLength,
                        0)
                    : BCryptDecrypt(
                        keyHandle,
                        input,
                        input.Length,
                        ref info,
                        null,
                        0,
                        output,
                        output.Length,
                        out actualLength,
                        0);
                ThrowIfError(status);
                if (actualLength != output.Length)
                {
                    throw new CryptographicException("BCrypt returned an invalid AES-GCM output length.");
                }

                return output;
            }
            catch
            {
                Array.Clear(output, 0, output.Length);
                throw;
            }
            finally
            {
                for (var index = handles.Count - 1; index >= 0; index--)
                {
                    handles[index].Free();
                }

                if (!encrypt)
                {
                    Array.Clear(tag, 0, tag.Length);
                }
            }
        }

        private static IntPtr Pin(byte[] value, ICollection<GCHandle> handles)
        {
            if (value.Length == 0)
            {
                return IntPtr.Zero;
            }

            var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
            handles.Add(handle);
            return handle.AddrOfPinnedObject();
        }

        private static CngKey ImportPublicKey(byte[] publicKey, bool agreement)
        {
            var blob = new byte[8 + (2 * P256CoordinateLength)];
            WriteInt32LittleEndian(
                blob,
                0,
                agreement ? BCryptEcdhPublicP256Magic : BCryptEcdsaPublicP256Magic);
            WriteInt32LittleEndian(blob, 4, P256CoordinateLength);
            Buffer.BlockCopy(publicKey, 1, blob, 8, 2 * P256CoordinateLength);
            try
            {
                return CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob);
            }
            finally
            {
                Array.Clear(blob, 0, blob.Length);
            }
        }

        private static void ValidateAesInputs(
            byte[] key,
            byte[] nonce,
            byte[] input,
            byte[] aad)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (nonce == null)
            {
                throw new ArgumentNullException(nameof(nonce));
            }

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (aad == null)
            {
                throw new ArgumentNullException(nameof(aad));
            }

            if (key.Length != AesKeyLength)
            {
                throw new ArgumentException("AES-GCM keys must be 32 bytes.", nameof(key));
            }

            if (nonce.Length != AesGcmNonceLength)
            {
                throw new ArgumentException("AES-GCM nonces must be 12 bytes.", nameof(nonce));
            }
        }

        private static void ValidatePublicKey(byte[] publicKey)
        {
            if (publicKey == null)
            {
                throw new ArgumentNullException(nameof(publicKey));
            }

            if (publicKey.Length != P256PublicKeyLength || publicKey[0] != 0x04)
            {
                throw new ArgumentException(
                    "P-256 public keys must be 65-byte uncompressed SEC1 values.",
                    nameof(publicKey));
            }
        }

        private static void WriteInt32LittleEndian(byte[] destination, int offset, int value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void ThrowIfError(int status)
        {
            if (status != 0)
            {
                throw new CryptographicException("Windows CNG failed with status 0x" + status.ToString("X8") + ".");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BCryptAuthenticatedCipherModeInfo
        {
            public int Size;
            public int Version;
            public IntPtr Nonce;
            public int NonceLength;
            public IntPtr AuthData;
            public int AuthDataLength;
            public IntPtr Tag;
            public int TagLength;
            public IntPtr MacContext;
            public int MacContextLength;
            public long AadLength;
            public long DataLength;
            public int Flags;
        }

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptOpenAlgorithmProvider(
            out IntPtr algorithmHandle,
            string algorithmId,
            string implementation,
            int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptCloseAlgorithmProvider(IntPtr algorithmHandle, int flags);

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptSetProperty(
            IntPtr handle,
            string property,
            byte[] input,
            int inputLength,
            int flags);

        [DllImport("bcrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int BCryptGetProperty(
            IntPtr handle,
            string property,
            byte[] output,
            int outputLength,
            out int resultLength,
            int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptGenerateSymmetricKey(
            IntPtr algorithmHandle,
            out IntPtr keyHandle,
            byte[] keyObject,
            int keyObjectLength,
            byte[] secret,
            int secretLength,
            int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDestroyKey(IntPtr keyHandle);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptEncrypt(
            IntPtr keyHandle,
            byte[] input,
            int inputLength,
            ref BCryptAuthenticatedCipherModeInfo paddingInfo,
            byte[] iv,
            int ivLength,
            byte[] output,
            int outputLength,
            out int resultLength,
            int flags);

        [DllImport("bcrypt.dll")]
        private static extern int BCryptDecrypt(
            IntPtr keyHandle,
            byte[] input,
            int inputLength,
            ref BCryptAuthenticatedCipherModeInfo paddingInfo,
            byte[] iv,
            int ivLength,
            byte[] output,
            int outputLength,
            out int resultLength,
            int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptSecretAgreement(
            IntPtr privateKeyHandle,
            IntPtr publicKeyHandle,
            out IntPtr secretHandle,
            int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptDeriveKey(
            IntPtr secretHandle,
            string keyDerivationFunction,
            IntPtr parameterList,
            byte[] derivedKey,
            int derivedKeyLength,
            out int resultLength,
            int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptFreeObject(IntPtr handle);
    }
}
