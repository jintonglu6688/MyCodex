using System;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WindowsCredentialDiagnosticsTests
    {
        [TestCase("config")]
        [TestCase("migration-backup/config")]
        public void LegacySevenReferenceIsMatchedWithoutReadingProtectedCredentialFields(string directory)
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "credential-diag-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var path = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar), "auth-profiles.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // This non-string protected field must be ignored, not deserialized or decrypted.
            const string original = "{\"Profiles\":[{\"Id\":\"private-id\",\"Kind\":\"openai_compatible\",\"ModelProviderId\":\"mycodex_resp_1234567890ab\",\"Name\":\"private-name\",\"EncryptedApiKey\":{\"secret\":\"synthetic-secret\"}}]}";
            File.WriteAllText(path, original);
            var config = Config(home, "MYCODEX_MYCODEX_RESP_1234567890AB_API_KEY");
            var log = string.Join("\n", WindowsCredentialDiagnostics.Collect(config, root, home,
                (name, target) => target == EnvironmentVariableTarget.User));
            Assert.That(log, Does.Contain("LegacyFilesFound=1").And.Contain("LegacyRecords=1"));
            Assert.That(log, Does.Contain("ConfigMatchesBackendHome=True").And.Contain("BackendReportsUnavailable=True"));
            Assert.That(log, Does.Contain("Name=MYCODEX_MYCODEX_RESP_1234567890AB_API_KEY")
                .And.Contain("MyCodexProcessPresent=False").And.Contain("UserPresent=True").And.Contain("MachinePresent=False"));
            Assert.That(log, Does.Contain("LegacyEnvironmentMatches=1").And.Contain("LegacyProviderMatches=1"));
            Assert.That(log, Does.Contain("BackendPerVariableAvailability=unknown"), "The frontend cannot inspect a resident's environment.");
            Assert.That(log, Does.Not.Contain("synthetic-secret").And.Not.Contain("private-id").And.Not.Contain("private-name"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [TestCase("sk-synthetic-secret\nforged-record")]
        [TestCase("ghp_syntheticsecret")]
        public void UntrustedVariableNamesAreNotLoggedOrLookedUp(string variable)
        {
            var config = Config(null, variable);
            var lookups = 0;
            var log = string.Join("\n", WindowsCredentialDiagnostics.Collect(config, null, null, (name, target) => { lookups++; return true; }));
            Assert.That(log, Does.Contain("Name=invalid_or_redacted"));
            Assert.That(log, Does.Not.Contain("synthetic").And.Not.Contain("forged-record"));
            Assert.That(lookups, Is.Zero);
        }

        [Test]
        public void UnreadableLegacyMetadataAndEnvironmentAccessAreReportedWithoutRawErrors()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "credential-diag-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            File.WriteAllText(Path.Combine(root, "config", "auth-profiles.json"), "invalid synthetic-secret");
            var config = Config(null, "FIXTURE_KEY");
            config.CredentialReferenceScope = "effective_config";
            var log = string.Join("\n", WindowsCredentialDiagnostics.Collect(config, root, null,
                (name, target) => throw new InvalidOperationException("synthetic-secret")));
            Assert.That(log, Does.Contain("LegacyFilesUnreadable=1").And.Contain("ConfigMatchesBackendHome=unknown"));
            Assert.That(log, Does.Contain("MyCodexProcessPresent=unknown").And.Contain("UserPresent=unknown"));
            Assert.That(log, Does.Not.Contain("synthetic-secret"));
        }

        [Test]
        public void OversizedLegacyFilesAreNotDeserialized()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "credential-diag-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            using (var file = File.Create(Path.Combine(root, "config", "auth-profiles.json"))) file.SetLength(4 * 1024 * 1024 + 1);
            var log = string.Join("\n", WindowsCredentialDiagnostics.Collect(Config(null, "FIXTURE_KEY"), root, null, (name, target) => false));
            Assert.That(log, Does.Contain("LegacyFilesFound=1").And.Contain("LegacyFilesUnreadable=1").And.Contain("LegacyRecords=0"));
        }

        private static AppServerConfigSnapshot Config(string home, string variable)
        {
            var snapshot = new AppServerConfigSnapshot
            {
                CredentialReferenceScope = "user_layer", CredentialReferenceProviderId = "mycodex_resp_1234567890ab",
                CredentialReferenceFilePath = home == null ? null : Path.Combine(home, "config.toml")
            };
            snapshot.CredentialEnvironmentReferences.Add(new AppServerCredentialEnvironmentReference { Field = "env_key", VariableName = variable });
            return snapshot;
        }
    }
}
