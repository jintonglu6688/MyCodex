using System;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayCliClientTests
    {
        [Test]
        public void BuildInitArguments_RequestsEmbeddedTls()
        {
            var args = EmbeddedRelayCliClient.BuildInitArguments(
                @"C:\Relay\relay-config.json",
                @"C:\Relay\relay-state.db",
                "192.168.1.50",
                38443,
                443,
                39221);

            Assert.That(args, Does.Contain(" --embedded-tls --json"));
            Assert.That(args, Does.Contain(" --listen-host 0.0.0.0 --listen-port 38443"));
            Assert.That(args, Does.Contain(" --public-host \"192.168.1.50\" --public-port 443"));
            Assert.That(args, Does.Contain(" --internal-listen-host 127.0.0.1 --internal-listen-port 39221"));
        }

        [Test]
        public void ParseLocalInfo_ReadsLoopbackInternalEndpoint()
        {
            const string json = "{\"internalListenHost\":\"127.0.0.1\",\"internalListenPort\":39221,\"internalTlsRequired\":false,\"internalRelayUrl\":\"http://127.0.0.1:39221\",\"internalHealthUrl\":\"http://127.0.0.1:39221/health\",\"tenants\":[]}";

            var info = EmbeddedRelayCliClient.ParseLocalInfo(json);

            Assert.That(info.InternalListenHost, Is.EqualTo("127.0.0.1"));
            Assert.That(info.InternalListenPort, Is.EqualTo(39221));
            Assert.That(info.InternalTlsRequired, Is.False);
            Assert.That(info.InternalRelayUrl, Is.EqualTo("http://127.0.0.1:39221"));
            Assert.That(info.InternalHealthUrl, Is.EqualTo("http://127.0.0.1:39221/health"));
        }

        [Test]
        public void ParseLocalInfo_ReadsCertificateIdentity()
        {
            var json = "{\"tlsRequired\":true,\"certificatePath\":\"C:\\\\Relay\\\\embedded-relay-cert.pem\",\"certificateSha256\":\"" + new string('a', 64) + "\",\"tenants\":[]}";

            var info = EmbeddedRelayCliClient.ParseLocalInfo(json);

            Assert.That(info.CertificatePath, Is.EqualTo(@"C:\Relay\embedded-relay-cert.pem"));
            Assert.That(info.CertificateSha256, Is.EqualTo(new string('a', 64)));
        }

        [Test]
        public void ParseLocalInfo_ReadsTenantAndCreatedSecret()
        {
            const string json = @"{
  ""version"": ""0.1.0-dev"",
  ""configPath"": ""C:\\Relay\\relay-config.json"",
  ""statePath"": ""C:\\Relay\\relay-state.db"",
  ""listenHost"": ""0.0.0.0"",
  ""listenPort"": 38443,
  ""publicHost"": ""192.168.1.50"",
  ""publicPort"": 38443,
  ""tlsRequired"": false,
  ""relayHost"": ""192.168.1.50"",
  ""relayPort"": 38443,
  ""relayUrl"": ""http://192.168.1.50:38443"",
  ""healthUrl"": ""http://192.168.1.50:38443/health"",
  ""tenants"": [
    {
      ""tenantId"": ""tenant_local"",
      ""displayName"": ""Local"",
      ""enabled"": true
    }
  ],
  ""secret"": {
    ""tenantId"": ""tenant_local"",
    ""value"": ""tenant-secret-value"",
    ""source"": ""created""
  }
}";

            var info = EmbeddedRelayCliClient.ParseLocalInfo(json);

            Assert.That(info.Version, Is.EqualTo("0.1.0-dev"));
            Assert.That(info.RelayHost, Is.EqualTo("192.168.1.50"));
            Assert.That(info.RelayPort, Is.EqualTo(38443));
            Assert.That(info.HealthUrl, Is.EqualTo("http://192.168.1.50:38443/health"));
            Assert.That(info.Tenants, Has.Length.EqualTo(1));
            Assert.That(info.Tenants[0].TenantId, Is.EqualTo("tenant_local"));
            Assert.That(info.Tenants[0].DisplayName, Is.EqualTo("Local"));
            Assert.That(info.Tenants[0].Enabled, Is.True);
            Assert.That(info.Secret, Is.Not.Null);
            Assert.That(info.Secret.TenantId, Is.EqualTo("tenant_local"));
            Assert.That(info.Secret.Value, Is.EqualTo("tenant-secret-value"));
            Assert.That(info.Secret.Source, Is.EqualTo("created"));
        }

        [Test]
        public void ParseLocalInfo_WhenJsonIsEmpty_ThrowsInvalidOperationException()
        {
            Assert.That(() => EmbeddedRelayCliClient.ParseLocalInfo(" "), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ParseLocalInfo_WhenSecretIsOmitted_LeavesSecretNull()
        {
            const string json = @"{
  ""relayHost"": ""192.168.1.50"",
  ""relayPort"": 38443,
  ""healthUrl"": ""http://192.168.1.50:38443/health"",
  ""tenants"": []
}";

            var info = EmbeddedRelayCliClient.ParseLocalInfo(json);

            Assert.That(info.Secret, Is.Null);
            Assert.That(info.Tenants, Is.Empty);
        }

        [Test]
        public void ParseRotatedSecret_ReadsTenantIdAndSecretWithoutRetainingOtherOutput()
        {
            const string output = "tenantId=tenant_local\r\ntenantSecret=rotated-secret-value\r\n";

            var secret = EmbeddedRelayCliClient.ParseRotatedSecret(output);

            Assert.That(secret.TenantId, Is.EqualTo("tenant_local"));
            Assert.That(secret.Value, Is.EqualTo("rotated-secret-value"));
            Assert.That(secret.Source, Is.EqualTo("rotated"));
        }

        [Test]
        public void BuildRotateTenantSecretArguments_QuotesConfigAndTenantId()
        {
            var arguments = EmbeddedRelayCliClient.BuildRotateTenantSecretArguments(
                @"C:\Relay Data\relay-config.json",
                "tenant_local");

            Assert.That(
                arguments,
                Is.EqualTo("tenant rotate-secret --config \"C:\\Relay Data\\relay-config.json\" --tenant \"tenant_local\""));
        }

        [Test]
        public void BuildInitArguments_EscapesTrailingBackslashAndEmbeddedQuote()
        {
            var args = EmbeddedRelayCliClient.BuildInitArguments(
                @"C:\Relay\",
                @"C:\Relay ""State""\relay-state.db",
                "192.168.1.50",
                38443,
                443,
                39221);

            Assert.That(args, Does.StartWith("local init --config \"C:\\Relay\\\\\" --state \"C:\\Relay \\\"State\\\"\\relay-state.db\" "));
        }

        [Test]
        public void ParseRotatedSecret_WhenSecretIsMissing_DoesNotExposeCommandOutput()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => EmbeddedRelayCliClient.ParseRotatedSecret("tenantId=tenant_local\r\nunexpected=sensitive-output\r\n"));

            Assert.That(exception.Message, Is.EqualTo("Relay CLI did not return a rotated tenant secret."));
            Assert.That(exception.Message, Does.Not.Contain("sensitive-output"));
        }
    }
}
