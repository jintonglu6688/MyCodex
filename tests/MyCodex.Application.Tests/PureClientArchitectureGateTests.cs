using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PureClientArchitectureGateTests
    {
        [Test]
        public void AppConfigPersistence_DoesNotRetainAuthenticationOrPrivateCodexHomeAuthority()
        {
            var source = File.ReadAllText(SourcePath("src", "MyCodex.Infrastructure", "Persistence", "AppConfigService.cs"));

            foreach (var retiredAuthority in new[]
            {
                "GetAuthenticationProfilesFilePath",
                "GetChatGptAuthBackup",
                "GetSharedCodexHomeDirectory",
                "GetAuthenticationProfileCodexHomeDirectory",
                "GetCodexAuthFilePath",
                "EnsureRuntimeProfileConfigDirectory"
            })
            {
                Assert.That(source, Does.Not.Contain(retiredAuthority));
            }
        }

        [Test]
        public void RetiredAuthenticationAndProviderImplementations_AreDeleted()
        {
            foreach (var path in new[]
            {
                SourcePath("src", "MyCodex.Infrastructure", "Persistence", "JsonAuthenticationProfileStore.cs"),
                SourcePath("src", "MyCodex.Infrastructure", "Persistence", "JsonChatGptAuthBackupStore.cs"),
                SourcePath("src", "MyCodex.Infrastructure", "Persistence", "AuthenticationProfileCodexHomePolicy.cs"),
                SourcePath("src", "MyCodex.Infrastructure", "Persistence", "JsonRuntimeSessionDefaultsStore.cs"),
                SourcePath("src", "MyCodex.Application", "Interfaces", "IAuthenticationProfileStore.cs"),
                SourcePath("src", "MyCodex.Application", "Interfaces", "IChatGptAuthBackupStore.cs"),
                SourcePath("src", "MyCodex.Domain", "Entities", "AuthenticationProfile.cs"),
                SourcePath("src", "MyCodex.Domain", "Entities", "AuthenticationProfileSnapshot.cs"),
                SourcePath("src", "MyCodex.Domain", "Entities", "ChatGptAuthBackupSnapshot.cs"),
                SourcePath("src", "MyCodex.WinForms", "Forms", "ChatGptAccountSelectionResolver.cs"),
                SourcePath("src", "MyCodex.WinForms", "Forms", "ModelReasoningSupportPolicy.cs")
            })
            {
                Assert.That(File.Exists(path), Is.False, path);
            }
        }

        [Test]
        public void RuntimeEndpointInitialization_DoesNotCreateAuthenticationBackupStorage()
        {
            var source = File.ReadAllText(SourcePath("src", "MyCodex.WinForms", "Forms", "MainForm.RuntimeConnection.cs"));

            Assert.That(source, Does.Not.Contain("chatgpt-auth-backups"));
        }

        [Test]
        public void ProductionSources_DoNotRetainPrivateOrSharedCodexHomeAuthority()
        {
            var sourceRoot = SourcePath("src");
            var forbidden = new[] { "SharedCodexHome" + "Path", "SharedCodexHome" + "WindowsPath" };
            var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => forbidden.Any(term => File.ReadAllText(path).IndexOf(term, StringComparison.Ordinal) >= 0))
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void ModelCatalog_ComesOnlyFromOfficialAppServerObservations()
        {
            foreach (var path in new[]
            {
                SourcePath("docs", "internal", "known-model-capabilities.md"),
                SourcePath("docs", "internal", "mimo2codex-vision-investigation.md"),
                SourcePath("docs", "mimo2codex-runtime-source.md"),
                SourcePath("src", "MyCodex.Application", "DTOs", "DiscoveredModelCapability.cs"),
                SourcePath("src", "MyCodex.Application", "Interfaces", "ICompatibleModelDiscoveryClient.cs"),
                SourcePath("src", "MyCodex.Application", "Services", "ModelCapabilitySnapshots.cs"),
                SourcePath("src", "MyCodex.Domain", "Entities", "ModelCapabilitySnapshot.cs"),
                SourcePath("src", "MyCodex.Infrastructure", "Models", "CompatibleModelDiscoveryClient.cs"),
                SourcePath("src", "MyCodex.Infrastructure", "Models", "CompatibleModelListParser.cs")
            })
            {
                Assert.That(File.Exists(path), Is.False, path);
            }

            var defaultsSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.Application", "Services", "SessionDefaultsService.cs"));
            var appConfigSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.Domain", "Entities", "AppConfigSnapshot.cs"));
            var storeSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.Infrastructure", "Persistence", "JsonAppConfigStore.cs"));

            Assert.That(defaultsSource, Does.Not.Contain("BuiltInDefaultModelId"));
            Assert.That(defaultsSource, Does.Not.Contain("LegacyBuiltInDefaultModelId"));
            Assert.That(appConfigSource, Does.Not.Contain("public string DefaultModelId"));
            Assert.That(storeSource, Does.Not.Contain("NormalizeLegacyBuiltInDefault"));
        }

        [Test]
        public void ReasoningCapabilities_ComeOnlyFromOfficialObservationsOrExplicitPreferences()
        {
            var defaultsSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.Application", "Services", "SessionDefaultsService.cs"));
            var automationSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.WinForms", "Forms", "AutomationEditForm.cs"));
            var modelSettingsSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.WinForms", "Forms", "ModelSettingsForm.cs"));
            var mapperSource = File.ReadAllText(SourcePath(
                "src", "MyCodex.Infrastructure", "AppServer", "AppServerAccountConfigMapper.cs"));

            Assert.That(defaultsSource, Does.Not.Contain("BuiltInDefaultReasoningEffort"));
            Assert.That(automationSource, Does.Not.Contain("BuiltInDefaultReasoningEffort"));
            Assert.That(automationSource, Does.Not.Contain("BuildFallbackReasoningEfforts"));
            Assert.That(modelSettingsSource, Does.Not.Contain("BuildFallbackEffortChoices"));
            Assert.That(mapperSource, Does.Contain("defaultReasoningEffort"));
            Assert.That(mapperSource, Does.Contain("supportedReasoningEfforts"));
        }

        [Test]
        public void Help_RemainsACompleteUserGuideWithoutRetiredOrInternalImplementationDetails()
        {
            foreach (var path in new[]
            {
                SourcePath("help", "UserManual.en.html"),
                SourcePath("help", "用户手册.html")
            })
            {
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Contain(path.EndsWith("UserManual.en.html", StringComparison.Ordinal)
                    ? "Authentication Center" : "认证中心"), path);
                Assert.That(source, Does.Not.Contain("does not keep account backups or provide a separate account or model-service switcher"));
                Assert.That(source, Does.Not.Contain("不保存账号备份，也不提供单独的账号或模型服务切换功能"));
                Assert.That(source, Does.Not.Contain("OpenRouter"));
                Assert.That(source, Does.Not.Contain("Model Relay"));
                Assert.That(source, Does.Not.Contain("模型中转"));
                Assert.That(source, Does.Not.Contain("source layers"));
                Assert.That(source, Does.Not.Contain("来源层"));
                Assert.That(source, Does.Not.Contain("requirements"));
                Assert.That(source, Does.Not.Contain("stdout/stderr"));
                Assert.That(source, Does.Not.Contain("底层输出缓冲"));
                Assert.That(source, Does.Not.Contain("prepared automatically"));
                Assert.That(source, Does.Not.Contain("自动准备"));

                Assert.That(
                    source.Split(new[] { "<h2>" }, StringSplitOptions.None).Length - 1,
                    Is.GreaterThanOrEqualTo(10),
                    path);
                Assert.That(
                    source.Split(new[] { "<p" }, StringSplitOptions.None).Length - 1,
                    Is.GreaterThanOrEqualTo(80),
                    path);
                Assert.That(
                    source.Split(new[] { "<li>" }, StringSplitOptions.None).Length - 1,
                    Is.GreaterThanOrEqualTo(150),
                    path);
            }
        }

        private static string SourcePath(params string[] parts)
        {
            var root = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}
