using System;
using System.IO;
using System.Security.Cryptography;

namespace MyCodex.Application.RemoteRelay
{
    public static class SecureRemotePairingRules
    {
        private const string PairingCodeDomain = "MYCODEX-PAIRING-CODE-V1";

        public static string ComputeShortCode(byte[] claimPlaintext)
        {
            if (claimPlaintext == null)
            {
                throw new ArgumentNullException(nameof(claimPlaintext));
            }

            byte[] transcript;
            using (var output = new MemoryStream())
            {
                SecureRemoteTranscript.AppendString(output, PairingCodeDomain);
                SecureRemoteTranscript.AppendBytes(output, claimPlaintext);
                transcript = output.ToArray();
            }

            try
            {
                byte[] digest;
                using (var sha256 = SHA256.Create())
                {
                    digest = sha256.ComputeHash(transcript);
                }

                try
                {
                    var value =
                        ((uint)digest[0] << 24) |
                        ((uint)digest[1] << 16) |
                        ((uint)digest[2] << 8) |
                        digest[3];
                    return (value % 1000000).ToString("D6");
                }
                finally
                {
                    Array.Clear(digest, 0, digest.Length);
                }
            }
            finally
            {
                Array.Clear(transcript, 0, transcript.Length);
            }
        }

        public static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }
    }
}
