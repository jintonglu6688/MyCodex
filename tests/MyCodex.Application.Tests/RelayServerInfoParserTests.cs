using System;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RelayServerInfoParserTests
    {
        [Test]
        public void Parse_UsesCreatedTenantSecret()
        {
            var result = RelayServerInfoParser.Parse(
                "{\"relayHost\":\"relay.example.com\",\"relayPort\":443,\"tlsRequired\":true," +
                "\"tenants\":[{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"enabled\":true}]," +
                "\"secret\":{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"value\":\"secret-value\"}}");

            Assert.That(result.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(result.RelayPort, Is.EqualTo(443));
            Assert.That(result.TlsRequired, Is.True);
            Assert.That(result.TenantId, Is.EqualTo("11111111-1111-4111-8111-111111111111"));
            Assert.That(result.AccessSecret, Is.EqualTo("secret-value"));
        }

        [Test]
        public void Parse_UsesOnlyEnabledTenantWhenSecretIsAbsent()
        {
            var result = RelayServerInfoParser.Parse(
                "{\"relayHost\":\"192.0.2.10\",\"relayPort\":38443,\"tlsRequired\":false," +
                "\"tenants\":[" +
                "{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"enabled\":false}," +
                "{\"tenantId\":\"22222222-2222-4222-8222-222222222222\",\"enabled\":true}]}");

            Assert.That(result.TenantId, Is.EqualTo("22222222-2222-4222-8222-222222222222"));
            Assert.That(result.AccessSecret, Is.Null);
        }

        [TestCase("{\"relayHost\":\"https://relay.example.com\",\"relayPort\":443,\"tlsRequired\":true,\"tenants\":[]}", "invalid_server_endpoint")]
        [TestCase("{\"relayHost\":\"relay.example.com\",\"relayPort\":443,\"tlsRequired\":true,\"tenants\":[]}", "tenant_selection_required")]
        [TestCase("{\"relayHost\":\"relay.example.com\",\"relayPort\":443,\"tlsRequired\":true,\"tenants\":[{\"tenantId\":\"11111111-1111-4111-8111-111111111111\",\"enabled\":true}],\"secret\":{\"tenantId\":\"22222222-2222-4222-8222-222222222222\",\"value\":\"secret\"}}", "invalid_server_secret")]
        public void Parse_RejectsInformationThatCannotBeImported(string json, string errorCode)
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RelayServerInfoParser.Parse(json));

            Assert.That(exception.Message, Is.EqualTo(errorCode));
        }
    }
}
