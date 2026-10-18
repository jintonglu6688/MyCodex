using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayCertificateValidatorTests
    {
        [Test]
        public void Validate_MatchingPublicLeafReturnsNormalizedFingerprint()
        {
            var fixture = CreateCertificateFixture(false, true);
            using (fixture.Certificate)
            {
                var validator = new EmbeddedRelayCertificateValidator();

                var actual = validator.Validate(fixture.Path, fixture.Sha256.ToUpperInvariant());

                Assert.That(actual, Is.EqualTo(fixture.Sha256));
                Assert.That(fixture.Certificate.HasPrivateKey, Is.False);
            }
        }

        [Test]
        public void Validate_MismatchThrows()
        {
            var fixture = CreateCertificateFixture(false, true);
            using (fixture.Certificate)
            {
                var mismatch = (fixture.Sha256[0] == '0' ? "1" : "0") + fixture.Sha256.Substring(1);

                Assert.That(
                    () => new EmbeddedRelayCertificateValidator().Validate(fixture.Path, mismatch),
                    Throws.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        public void Validate_CaCertificateIsRejected()
        {
            var fixture = CreateCertificateFixture(true, true);
            using (fixture.Certificate)
            {
                Assert.That(
                    () => new EmbeddedRelayCertificateValidator().Validate(fixture.Path, fixture.Sha256),
                    Throws.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        public void Validate_LeafWithoutServerAuthenticationIsRejected()
        {
            var fixture = CreateCertificateFixture(false, false);
            using (fixture.Certificate)
            {
                Assert.That(
                    () => new EmbeddedRelayCertificateValidator().Validate(fixture.Path, fixture.Sha256),
                    Throws.TypeOf<InvalidOperationException>());
            }
        }

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void ValidatorSource_DoesNotOpenOrMutateWindowsCertificateStores()
        {
            var source = RepositorySource.ReadSourceFile("EmbeddedRelayCertificateValidator.cs");

            Assert.That(source, Does.Not.Contain("X509Store"));
            Assert.That(source, Does.Not.Contain("StoreLocation"));
            Assert.That(source, Does.Not.Contain("TrustedPeople"));
        }

        private static CertificateFixture CreateCertificateFixture(bool isCa, bool serverAuthentication)
        {
            var directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "embedded-relay-certificate-validator",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=MyCodex Embedded Relay Test",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCa, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    true));
                var usages = new OidCollection();
                usages.Add(new Oid(serverAuthentication ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2"));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
                var certificateWithKey = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddYears(1));
                var certificate = new X509Certificate2(certificateWithKey.Export(X509ContentType.Cert));
                certificateWithKey.Dispose();
                var path = Path.Combine(directory, "embedded-relay-cert.pem");
                File.WriteAllText(path, ToPem(certificate.RawData));
                return new CertificateFixture(path, certificate, Sha256(certificate));
            }
        }

        private static string ToPem(byte[] der)
        {
            return "-----BEGIN CERTIFICATE-----\n" +
                Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks) +
                "\n-----END CERTIFICATE-----\n";
        }

        private static string Sha256(X509Certificate2 certificate)
        {
            using (var hash = SHA256.Create())
            {
                return string.Concat(hash.ComputeHash(certificate.RawData).Select(value => value.ToString("x2")));
            }
        }

        private sealed class CertificateFixture
        {
            public CertificateFixture(string path, X509Certificate2 certificate, string sha256)
            {
                Path = path;
                Certificate = certificate;
                Sha256 = sha256;
            }

            public string Path { get; private set; }

            public X509Certificate2 Certificate { get; private set; }

            public string Sha256 { get; private set; }
        }
    }
}
