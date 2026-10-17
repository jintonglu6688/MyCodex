using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal interface IEmbeddedRelayCertificateValidator
    {
        string Validate(string certificatePath, string reportedSha256);
    }

    internal sealed class EmbeddedRelayCertificateValidator : IEmbeddedRelayCertificateValidator
    {
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

        public string Validate(string certificatePath, string reportedSha256)
        {
            var expected = NormalizeSha256(reportedSha256);
            if (expected == null)
            {
                throw new InvalidOperationException("Embedded Relay certificate SHA-256 is invalid.");
            }

            using (var certificate = LoadCertificate(certificatePath))
            {
                ValidateLeaf(certificate);
                var actual = ComputeSha256(certificate);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Embedded Relay certificate SHA-256 does not match the reported value.");
                }

                return actual;
            }
        }

        private static X509Certificate2 LoadCertificate(string certificatePath)
        {
            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                throw new InvalidOperationException("Embedded Relay certificate path is missing.");
            }

            var pem = File.ReadAllText(certificatePath);
            var base64 = pem
                .Replace("-----BEGIN CERTIFICATE-----", string.Empty)
                .Replace("-----END CERTIFICATE-----", string.Empty);
            var compact = new StringBuilder(base64.Length);
            foreach (var character in base64)
            {
                if (!char.IsWhiteSpace(character))
                {
                    compact.Append(character);
                }
            }

            try
            {
                return new X509Certificate2(Convert.FromBase64String(compact.ToString()));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Embedded Relay certificate file is invalid.", ex);
            }
        }

        private static void ValidateLeaf(X509Certificate2 certificate)
        {
            if (certificate.HasPrivateKey)
            {
                throw new InvalidOperationException("Embedded Relay certificate must not contain a private key.");
            }

            foreach (var extension in certificate.Extensions)
            {
                var constraints = extension as X509BasicConstraintsExtension;
                if (constraints != null && constraints.CertificateAuthority)
                {
                    throw new InvalidOperationException("Embedded Relay certificate must not be a certificate authority.");
                }
            }

            var now = DateTime.Now;
            if (now < certificate.NotBefore || now > certificate.NotAfter)
            {
                throw new InvalidOperationException("Embedded Relay certificate is not currently valid.");
            }

            if (!HasServerAuthenticationUsage(certificate))
            {
                throw new InvalidOperationException("Embedded Relay certificate is not valid for server authentication.");
            }
        }

        private static bool HasServerAuthenticationUsage(X509Certificate2 certificate)
        {
            foreach (var extension in certificate.Extensions)
            {
                var usages = extension as X509EnhancedKeyUsageExtension;
                if (usages == null)
                {
                    continue;
                }

                foreach (Oid usage in usages.EnhancedKeyUsages)
                {
                    if (string.Equals(usage.Value, ServerAuthenticationOid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string ComputeSha256(X509Certificate2 certificate)
        {
            using (var hash = SHA256.Create())
            {
                var bytes = hash.ComputeHash(certificate.RawData);
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string NormalizeSha256(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
            if (normalized == null || normalized.Length != 64)
            {
                return null;
            }

            foreach (var character in normalized)
            {
                if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
                {
                    return null;
                }
            }

            return normalized;
        }
    }
}
