using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerProtocolCapabilitiesTests
    {
        [TestCase("0.163.0", false)]
        [TestCase("0.164.0", true)]
        public void BrowserIntegrationRequiresVerifiedProtocolVersion(string version, bool supported)
        {
            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled },
                CreateLaunchInfo(CodexRuntimeSources.Bundled, version), CreateInitializeResult("codex/" + version));
            Assert.That(capabilities.SupportsBrowserUseIntegration, Is.EqualTo(supported));
        }

        [Test]
        public void BuildProtocolCapabilities_BundledModernRuntime_EnablesModernProtocol()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.Bundled, "0.128.0");

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled },
                launchInfo,
                CreateInitializeResult("codex/0.128.0"));

            Assert.That(capabilities.RuntimeSource, Is.EqualTo(CodexRuntimeSources.Bundled));
            Assert.That(capabilities.RuntimeVersion, Is.EqualTo("0.128.0"));
            Assert.That(capabilities.ProtocolProfile, Is.EqualTo("modern"));
            Assert.That(capabilities.IsLegacyRuntime, Is.False);
            Assert.That(capabilities.SupportsModernAppServerProtocol, Is.True);
            Assert.That(capabilities.SupportsExperimentalApi, Is.True);
            Assert.That(capabilities.SupportsPluginManagement, Is.True);
            Assert.That(capabilities.SupportsSkills, Is.True);
            Assert.That(capabilities.SupportsMcpManagement, Is.True);
            Assert.That(capabilities.SupportsMcpElicitation, Is.True);
            Assert.That(capabilities.SupportsModelProviderCapabilities, Is.True);
            Assert.That(capabilities.SupportsConfigRequirements, Is.True);
            Assert.That(capabilities.SupportsExternalAgentConfigImport, Is.True);
            Assert.That(capabilities.SupportsFileSystem, Is.True);
            Assert.That(capabilities.SupportsFuzzyFileSearch, Is.True);
            Assert.That(capabilities.SupportsWindowsSandbox, Is.True);
            Assert.That(capabilities.SupportsBrowserUseIntegration, Is.False);
        }

        [Test]
        public void BuildProtocolCapabilities_PackagedVersion_RemainsDiagnosticWhileBundledContractIsEnabled()
        {
            var launchInfo = CreateLaunchInfo(
                CodexRuntimeSources.Bundled,
                CodexRuntimeSources.BundledDefaultVersion);

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled },
                launchInfo,
                CreateInitializeResult("codex/" + CodexRuntimeSources.BundledDefaultVersion));

            Assert.That(capabilities.SupportsPluginManagement, Is.True);
            Assert.That(capabilities.RuntimeVersion, Is.EqualTo(CodexRuntimeSources.BundledDefaultVersion));
            Assert.That(capabilities.SupportsMarketplaceRegistration, Is.True);
            Assert.That(capabilities.SupportsPluginMutation, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluatePluginMutation(capabilities).IsAvailable, Is.True);
        }

        [Test]
        public void BuildProtocolCapabilities_BundledContract_DeclaresMethodCapabilitiesIndependentOfVersion()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.Bundled, "0.1.0");

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled },
                launchInfo,
                CreateInitializeResult("codex/0.1.0"));

            Assert.That(capabilities.RuntimeVersion, Is.EqualTo("0.1.0"));
            Assert.That(capabilities.SupportsPluginManagement, Is.True);
            Assert.That(capabilities.SupportsPluginCatalogObservation, Is.True);
            Assert.That(capabilities.SupportsInstalledPluginObservation, Is.True);
            Assert.That(capabilities.SupportsMarketplaceRegistration, Is.True);
            Assert.That(capabilities.SupportsPluginMutation, Is.True);
        }

        [TestCase(CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom)]
        [TestCase(CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public void BuildProtocolCapabilities_CurrentClientPluginContract_DoesNotDependOnRuntimeSourceOrVersion(
            string runtimeSource)
        {
            var launchInfo = CreateLaunchInfo(runtimeSource, "0.1.0");

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = runtimeSource },
                launchInfo,
                CreateInitializeResult("codex/0.1.0"));

            Assert.That(capabilities.SupportsPluginManagement, Is.True);
            Assert.That(capabilities.SupportsPluginCatalogObservation, Is.True);
            Assert.That(capabilities.SupportsInstalledPluginObservation, Is.True);
            Assert.That(capabilities.SupportsMarketplaceRegistration, Is.True);
            Assert.That(capabilities.SupportsPluginMutation, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluateRegistration(capabilities).IsAvailable, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluatePluginMutation(capabilities).IsAvailable, Is.True);
        }

        [Test]
        public void BuildProtocolCapabilities_KnownOlderRuntime_UsesLegacyProfile()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.System, null);

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.System },
                launchInfo,
                CreateInitializeResult("codex/0.124.0"));

            Assert.That(capabilities.RuntimeSource, Is.EqualTo(CodexRuntimeSources.System));
            Assert.That(capabilities.RuntimeVersion, Is.EqualTo("0.124.0"));
            Assert.That(capabilities.ProtocolProfile, Is.EqualTo("legacy-compatible"));
            Assert.That(capabilities.SupportsModernAppServerProtocol, Is.False);
        }

        [Test]
        public void BuildProtocolCapabilities_UnknownVersion_DefaultsToModernProfile()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.RemoteUnixSsh, null);

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh },
                launchInfo,
                CreateInitializeResult(null));

            Assert.That(capabilities.RuntimeSource, Is.EqualTo(CodexRuntimeSources.RemoteUnixSsh));
            Assert.That(capabilities.RuntimeVersion, Is.Null);
            Assert.That(capabilities.ProtocolProfile, Is.EqualTo("modern"));
            Assert.That(capabilities.IsLegacyRuntime, Is.False);
            Assert.That(capabilities.SupportsModernAppServerProtocol, Is.True);
        }

        [Test]
        public void BuildProtocolCapabilities_RemoteModernTarget_UsesOfficialPluginManagementContract()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.RemoteUnixSsh, null);

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh },
                launchInfo,
                CreateInitializeResult(null));

            Assert.That(capabilities.SupportsPluginCatalogObservation, Is.True);
            Assert.That(capabilities.SupportsInstalledPluginObservation, Is.True);
            Assert.That(capabilities.SupportsMarketplaceRegistration, Is.True);
            Assert.That(capabilities.SupportsPluginMutation, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluateRegistration(capabilities).IsAvailable, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluatePluginMutation(capabilities).IsAvailable, Is.True);
        }

        [Test]
        public void BuildProtocolCapabilities_WslModernTarget_UsesOfficialPluginManagementContract()
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.Wsl, null);

            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl },
                launchInfo,
                CreateInitializeResult(null));

            Assert.That(capabilities.SupportsPluginCatalogObservation, Is.True);
            Assert.That(capabilities.SupportsInstalledPluginObservation, Is.True);
            Assert.That(capabilities.SupportsMarketplaceRegistration, Is.True);
            Assert.That(capabilities.SupportsPluginMutation, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluateRegistration(capabilities).IsAvailable, Is.True);
            Assert.That(MarketplaceMutationPolicy.EvaluatePluginMutation(capabilities).IsAvailable, Is.True);
        }

        [TestCase("0.142.5", false)]
        [TestCase("0.143.0", true)]
        [TestCase("0.144.1", true)]
        [TestCase(null, false)]
        public void BuildProtocolCapabilities_ThreadForkLastTurnIdRequiresKnownRuntimeAtLeast0143(
            string runtimeVersion,
            bool expected)
        {
            var launchInfo = CreateLaunchInfo(CodexRuntimeSources.System, runtimeVersion);
            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.System },
                launchInfo,
                CreateInitializeResult(runtimeVersion == null ? null : "codex/" + runtimeVersion));
            var property = typeof(AppServerProtocolCapabilities).GetProperty("SupportsThreadForkLastTurnId");

            Assert.That(property, Is.Not.Null);
            Assert.That(property.GetValue(capabilities, null), Is.EqualTo(expected));
        }

        [Test]
        public void EnsureSupported_ModernCapability_AllowsFeature()
        {
            var capabilities = new AppServerProtocolCapabilities
            {
                ProtocolProfile = "modern",
                SupportsPluginManagement = true
            };

            Assert.DoesNotThrow(() => AppServerProtocolCapabilityGuards.EnsureSupported(
                capabilities,
                capabilities.SupportsPluginManagement,
                "Plugin management"));
        }

        [TestCase("0.157.0", false)]
        [TestCase("0.158.0", true)]
        [TestCase("0.159.0", true)]
        [TestCase(null, false)]
        public void HistoryPagination_RequiresVerifiedRuntimeContract(string version, bool expected)
        {
            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.System },
                CreateLaunchInfo(CodexRuntimeSources.System, version),
                CreateInitializeResult(version == null ? null : "codex/" + version));
            Assert.That(capabilities.SupportsThreadHistoryPagination, Is.EqualTo(expected));
        }

        [TestCase("0.162.0", AppServerHostPlatform.Windows, false)]
        [TestCase("0.163.0", AppServerHostPlatform.Windows, true)]
        [TestCase("0.164.0", AppServerHostPlatform.Wsl, false)]
        [TestCase("0.164.0", AppServerHostPlatform.RemoteMac, false)]
        [TestCase(null, AppServerHostPlatform.Windows, false)]
        public void RealtimeVoice_RequiresVerifiedRuntimeOnTheLocalWindowsTarget(string version, AppServerHostPlatform platform, bool expected)
        {
            var launch = CreateLaunchInfo(CodexRuntimeSources.System, version);
            var capabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(
                new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.System },
                new AppServerLaunchInfo(launch.FileName, launch.Arguments, launch.EnvironmentVariables, platform),
                CreateInitializeResult(version == null ? null : "codex/" + version));
            Assert.That(capabilities.SupportsRealtimeVoice, Is.EqualTo(expected));
        }

        [Test]
        public void EnsureSupported_LegacyCapability_BlocksFeature()
        {
            var capabilities = new AppServerProtocolCapabilities
            {
                ProtocolProfile = "legacy-compatible",
                SupportsPluginManagement = false
            };

            var exception = Assert.Throws<System.InvalidOperationException>(() =>
                AppServerProtocolCapabilityGuards.EnsureSupported(
                    capabilities,
                    capabilities.SupportsPluginManagement,
                    "Plugin management"));

            Assert.That(exception.Message, Does.Contain("Plugin management"));
            Assert.That(exception.Message, Is.Not.Empty);
        }

        [Test]
        public void IsSupported_NullCapabilities_AllowsFeatureForPreConnectionCompatibility()
        {
            Assert.That(
                AppServerProtocolFeatureSupport.IsSupported(null, AppServerProtocolFeature.PluginManagement),
                Is.True);
        }

        [Test]
        public void IsSupported_LegacyCapabilities_DisablesModernManagementFeatures()
        {
            var capabilities = new AppServerProtocolCapabilities
            {
                SupportsPluginManagement = false,
                SupportsSkills = false,
                SupportsMcpManagement = false,
                SupportsMcpOAuth = false,
                SupportsModelProviderCapabilities = false,
                SupportsExperimentalApi = false,
                SupportsConfigRequirements = false,
                SupportsExternalAgentConfigImport = false,
                SupportsFileSystem = false,
                SupportsFuzzyFileSearch = false,
                SupportsWindowsSandbox = false
            };

            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.PluginManagement), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.Skills), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.McpManagement), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.McpOAuth), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ModelProviderCapabilities), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ExperimentalFeatureList), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ConfigRequirements), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ExternalAgentConfigImport), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.FileSystem), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.FuzzyFileSearch), Is.False);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.WindowsSandbox), Is.False);
        }

        [Test]
        public void IsSupported_ModernCapabilities_EnablesModernManagementFeatures()
        {
            var capabilities = new AppServerProtocolCapabilities
            {
                SupportsPluginManagement = true,
                SupportsSkills = true,
                SupportsMcpManagement = true,
                SupportsMcpOAuth = true,
                SupportsModelProviderCapabilities = true,
                SupportsExperimentalApi = true,
                SupportsConfigRequirements = true,
                SupportsExternalAgentConfigImport = true,
                SupportsFileSystem = true,
                SupportsFuzzyFileSearch = true,
                SupportsWindowsSandbox = true
            };

            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.PluginManagement), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.Skills), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.McpManagement), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.McpOAuth), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ModelProviderCapabilities), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ExperimentalFeatureList), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ConfigRequirements), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.ExternalAgentConfigImport), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.FileSystem), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.FuzzyFileSearch), Is.True);
            Assert.That(AppServerProtocolFeatureSupport.IsSupported(capabilities, AppServerProtocolFeature.WindowsSandbox), Is.True);
        }

        private static AppServerLaunchInfo CreateLaunchInfo(string source, string version)
        {
            var environment = new Dictionary<string, string>
            {
                { "MYCODEX_CODEX_SOURCE", source }
            };
            if (!string.IsNullOrWhiteSpace(version))
            {
                environment["MYCODEX_CODEX_VERSION"] = version;
            }

            return new AppServerLaunchInfo("codex.exe", "app-server --listen stdio://", environment);
        }

        private static IDictionary<string, object> CreateInitializeResult(string userAgent)
        {
            var result = new Dictionary<string, object>
            {
                { "platformOs", "windows" }
            };
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                result["userAgent"] = userAgent;
            }

            return result;
        }
    }
}
