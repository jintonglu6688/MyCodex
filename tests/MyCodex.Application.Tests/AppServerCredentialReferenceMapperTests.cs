using System.Collections.Generic;
using System.Linq;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCredentialReferenceMapperTests
    {
        [Test]
        public void OnlySelectedProviderEnvironmentNamesAreRetained()
        {
            var config = Config("selected", "SELECTED_KEY");
            var providers = (Dictionary<string, object>)config["model_providers"];
            var selected = (Dictionary<string, object>)providers["selected"];
            selected["experimental_bearer_token"] = "synthetic-secret";
            selected["http_headers"] = new Dictionary<string, object> { { "Authorization", "synthetic-secret" } };
            selected["env_http_headers"] = new Dictionary<string, object> { { "Authorization", "HEADER_KEY" } };
            providers["unused"] = new Dictionary<string, object> { { "env_key", "UNUSED_KEY" } };
            var snapshot = AppServerAccountConfigMapper.BuildConfigSnapshot(new Dictionary<string, object> { { "config", config } });
            Assert.That(snapshot.CredentialReferenceScope, Is.EqualTo("effective_config"));
            Assert.That(snapshot.CredentialReferenceProviderId, Is.EqualTo("selected"));
            Assert.That(snapshot.CredentialEnvironmentReferences.Select(r => r.VariableName), Is.EqualTo(new[] { "SELECTED_KEY", "HEADER_KEY" }));
            Assert.That(snapshot.CredentialEnvironmentReferences.Select(r => r.Field), Is.EqualTo(new[] { "env_key", "env_http_headers" }));
        }

        [Test]
        public void UserLayerTakesPrecedenceOverEffectiveProjectOverridesForNativeCaptureDiagnostics()
        {
            var snapshot = AppServerAccountConfigMapper.BuildConfigSnapshot(new Dictionary<string, object>
            {
                { "config", Config("project", "PROJECT_KEY") },
                { "layers", new object[] { new Dictionary<string, object>
                {
                    { "name", new Dictionary<string, object> { { "type", "user" }, { "file", @"C:\fixture\config.toml" } } },
                    { "config", Config("legacy", "LEGACY_KEY") }
                } } }
            });
            Assert.That(snapshot.ModelProviderId, Is.EqualTo("project"), "Existing effective settings must not change.");
            Assert.That(snapshot.CredentialReferenceScope, Is.EqualTo("user_layer"));
            Assert.That(snapshot.CredentialReferenceFilePath, Is.EqualTo(@"C:\fixture\config.toml"));
            Assert.That(snapshot.CredentialReferenceProviderId, Is.EqualTo("legacy"));
            Assert.That(snapshot.CredentialEnvironmentReferences.Single().VariableName, Is.EqualTo("LEGACY_KEY"));
        }

        [Test]
        public void LargeHeaderMapsAreExplicitlyMarkedAsTruncated()
        {
            var config = Config("selected", "SELECTED_KEY");
            config["profile"] = "private-profile";
            var selected = (Dictionary<string, object>)((Dictionary<string, object>)config["model_providers"])["selected"];
            var headers = new Dictionary<string, object>();
            for (var index = 0; index < 40; index++) headers.Add("header" + index, "ENV_" + index);
            selected["env_http_headers"] = headers;
            var snapshot = AppServerAccountConfigMapper.BuildConfigSnapshot(new Dictionary<string, object> { { "config", config } });
            Assert.That(snapshot.CredentialEnvironmentReferences, Has.Count.EqualTo(32));
            Assert.That(snapshot.CredentialReferencesTruncated, Is.True);
            Assert.That(snapshot.CredentialReferenceHasProfile, Is.True);
        }

        private static Dictionary<string, object> Config(string provider, string variable)
        {
            return new Dictionary<string, object>
            {
                { "model_provider", provider },
                { "model_providers", new Dictionary<string, object>
                {
                    { provider, new Dictionary<string, object> { { "env_key", variable } } }
                } }
            };
        }
    }
}
