using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PureClientArchitectureGateTests
    {
        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void AppConfigPersistence_DoesNotRetainAuthenticationOrPrivateCodexHomeAuthority()
        {
            var source = RepositorySource.ReadSourceFile("AppConfigService.cs");

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

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void RetiredAuthenticationAndProviderImplementations_AreDeleted()
        {
            foreach (var path in new[]
            {
                RepositorySource.FindSourceFile("JsonAuthenticationProfileStore.cs"),
                RepositorySource.FindSourceFile("JsonChatGptAuthBackupStore.cs"),
                RepositorySource.FindSourceFile("AuthenticationProfileCodexHomePolicy.cs"),
                RepositorySource.FindSourceFile("JsonRuntimeSessionDefaultsStore.cs"),
                RepositorySource.FindSourceFile("IAuthenticationProfileStore.cs"),
                RepositorySource.FindSourceFile("IChatGptAuthBackupStore.cs"),
                RepositorySource.FindSourceFile("AuthenticationProfile.cs"),
                RepositorySource.FindSourceFile("AuthenticationProfileSnapshot.cs"),
                RepositorySource.FindSourceFile("ChatGptAuthBackupSnapshot.cs"),
                RepositorySource.FindSourceFile("ChatGptAccountSelectionResolver.cs"),
                RepositorySource.FindSourceFile("ModelReasoningSupportPolicy.cs")
            })
            {
                Assert.That(File.Exists(path), Is.False, path);
            }
        }

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void ProductionSources_DoNotRetainPrivateOrSharedCodexHomeAuthority()
        {
            var sourceRoot = RepositorySource.GetPath("src");
            var forbidden = new[] { "SharedCodexHome" + "Path", "SharedCodexHome" + "WindowsPath" };
            var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => forbidden.Any(term => File.ReadAllText(path).IndexOf(term, StringComparison.Ordinal) >= 0))
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void ModelCatalog_ComesOnlyFromOfficialAppServerObservations()
        {
            foreach (var path in new[]
            {
                RepositorySource.GetPath("docs", "internal", "known-model-capabilities.md"),
                RepositorySource.GetPath("docs", "internal", "mimo2codex-vision-investigation.md"),
                RepositorySource.GetPath("docs", "mimo2codex-runtime-source.md"),
                RepositorySource.FindSourceFile("DiscoveredModelCapability.cs"),
                RepositorySource.FindSourceFile("ICompatibleModelDiscoveryClient.cs"),
                RepositorySource.FindSourceFile("ModelCapabilitySnapshots.cs"),
                RepositorySource.FindSourceFile("ModelCapabilitySnapshot.cs"),
                RepositorySource.FindSourceFile("CompatibleModelDiscoveryClient.cs"),
                RepositorySource.FindSourceFile("CompatibleModelListParser.cs")
            })
            {
                Assert.That(File.Exists(path), Is.False, path);
            }

            var defaultsSource = RepositorySource.ReadSourceFile("SessionDefaultsService.cs");
            var appConfigSource = RepositorySource.ReadSourceFile("AppConfigSnapshot.cs");
            var storeSource = RepositorySource.ReadSourceFile("JsonAppConfigStore.cs");

            Assert.That(defaultsSource, Does.Not.Contain("BuiltInDefaultModelId"));
            Assert.That(defaultsSource, Does.Not.Contain("LegacyBuiltInDefaultModelId"));
            Assert.That(appConfigSource, Does.Not.Contain("public string DefaultModelId"));
            Assert.That(storeSource, Does.Not.Contain("NormalizeLegacyBuiltInDefault"));
        }

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void ReasoningCapabilities_ComeOnlyFromOfficialObservationsOrExplicitPreferences()
        {
            var mapperSource = RepositorySource.ReadSourceFile("AppServerAccountConfigMapper.cs");

            Assert.That(mapperSource, Does.Contain("defaultReasoningEffort"));
            Assert.That(mapperSource, Does.Contain("supportedReasoningEfforts"));
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void Help_RemainsACompleteUserGuideWithoutRetiredOrInternalImplementationDetails()
        {
            foreach (var path in new[]
            {
                RepositorySource.GetPath("help", "UserManual.en.html"),
                RepositorySource.GetPath("help", "用户手册.html")
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
    }
}
