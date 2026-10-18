using System;
using System.IO;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MarketplaceManagementBoundaryTests
    {
        [Test]
        public void StartupComposition_DoesNotOwnLegacyMarketplaceOrSkillTargetState()
        {
            var programSource = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(programSource, Does.Not.Contain("PluginMarketMirrorService"));
            Assert.That(programSource, Does.Not.Contain("PluginMarketInstallService"));
            Assert.That(programSource, Does.Not.Contain("SkillMarketInstallService"));
            Assert.That(programSource, Does.Not.Contain("PluginMarketInstalledStateService"));
            Assert.That(programSource, Does.Not.Contain("SkillMarketTargetInstaller"));
        }

        [Test]
        public void LegacyTargetMirrorAndInstallerImplementations_AreDeleted()
        {
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Markets", "PluginMarketInstallService.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Markets", "PluginMarketTargetMirrorInstaller.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Markets", "SkillMarketInstallService.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Markets", "SkillMarketTargetInstaller.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Persistence", "LocalSharedSkillMirrorService.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "Persistence", "LocalPluginSkillMirrorService.cs")), Is.False);

            var appConfigSource = RepositorySource.ReadSourceFile("AppConfigService.cs");
            var skillEditorSource = RepositorySource.ReadSourceFile("SkillFileEditor.cs");
            Assert.That(appConfigSource, Does.Not.Contain("marketplaces"));
            Assert.That(appConfigSource, Does.Not.Contain("skill-market"));
            Assert.That(skillEditorSource, Does.Not.Contain("SharedSkillPathResolver"));
        }

        [Test]
        public void PluginManagementComposition_UsesOnlyCatalogAndAppServerForTargetState()
        {
            var formSource = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var compositionSource = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            var constructor = ExtractConstructor(formSource);

            Assert.That(constructor, Does.Not.Contain("IPluginMarketInstallService"));
            Assert.That(constructor, Does.Not.Contain("PluginMarketInstalledStateService"));
            Assert.That(constructor, Does.Not.Contain("PluginRuntimeEnabledStateService"));
            var composition = ExtractMethod(compositionSource, "PluginManagementMenuItem_Click");
            Assert.That(composition, Does.Not.Contain("new PluginMarketInstalledStateService"));
            Assert.That(composition, Does.Not.Contain("new PluginRuntimeEnabledStateService"));

            var attachmentSource = RepositorySource.ReadSourceFile("PluginAttachmentSelectionBuilder.cs");
            var attachmentBuilder = ExtractBlock(attachmentSource, attachmentSource.IndexOf("public static List<AppServerPluginSummary> BuildInsertablePlugins(", StringComparison.Ordinal));
            Assert.That(attachmentBuilder, Does.Not.Contain("PluginMarketInstalledStateService"));

            var remoteAttachmentSource = RepositorySource.ReadSourceFile("AppServerCodingRemoteAttachmentCatalogOperator.cs");
            var remoteList = ExtractBlock(remoteAttachmentSource, remoteAttachmentSource.IndexOf("ListPluginAttachmentsAsync", StringComparison.Ordinal));
            Assert.That(remoteList, Does.Not.Contain("AddRuntimeInstalledPluginAttachments"));
        }

        [Test]
        public void MutationButtons_UseRuntimeCapabilityPolicyInsteadOfVersionChecks()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var install = ExtractPrivateMethod(source, "CanInstallMarketItem");
            var uninstall = ExtractPrivateMethod(source, "CanUninstallPlugin");

            Assert.That(install, Does.Contain("MarketplaceMutationPolicy"));
            Assert.That(uninstall, Does.Contain("MarketplaceMutationPolicy"));
            Assert.That(source, Does.Not.Contain("RuntimeVersion.CompareTo"));
        }

        [Test]
        public void AppServerPluginOperations_UseMethodSpecificProtocolCapabilities()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");

            Assert.That(ExtractBlock(source, source.IndexOf("ListPluginsAsync(", StringComparison.Ordinal)), Does.Contain("SupportsPluginCatalogObservation"));
            Assert.That(ExtractBlock(source, source.IndexOf("GetInstalledPluginsAsync(", StringComparison.Ordinal)), Does.Contain("SupportsInstalledPluginObservation"));
            Assert.That(ExtractBlock(source, source.IndexOf("UpgradeMarketplaceAsync(", StringComparison.Ordinal)), Does.Contain("SupportsMarketplaceRegistration"));
            Assert.That(ExtractBlock(source, source.IndexOf("AddMarketplaceAsync(", StringComparison.Ordinal)), Does.Contain("SupportsMarketplaceRegistration"));
            Assert.That(ExtractBlock(source, source.IndexOf("RemoveMarketplaceAsync(", StringComparison.Ordinal)), Does.Contain("SupportsMarketplaceRegistration"));
            Assert.That(ExtractBlock(source, source.IndexOf("InstallPluginAsync(", StringComparison.Ordinal)), Does.Contain("SupportsPluginMutation"));
            Assert.That(ExtractBlock(source, source.IndexOf("InstallRemotePluginAsync(", StringComparison.Ordinal)), Does.Contain("SupportsPluginMutation"));
            Assert.That(ExtractBlock(source, source.IndexOf("UninstallPluginAsync(", StringComparison.Ordinal)), Does.Contain("SupportsPluginMutation"));
        }

        [Test]
        public void InstalledPluginConsumers_UseOneOfficialObservationService()
        {
            var consumerPaths = new[]
            {
                RepositorySource.FindSourceFile("SkillSourcePluginLoader.cs"),
                RepositorySource.FindSourceFile("AppServerCodingRemoteAttachmentCatalogOperator.cs"),
                RepositorySource.FindSourceFile("MainForm.Attachments.cs"),
                RepositorySource.FindSourceFile("MainForm.Automation.cs"),
                RepositorySource.FindSourceFile("MainForm.SessionWorkbenches.cs"),
                RepositorySource.FindSourceFile("PluginManagementForm.cs")
            };

            foreach (var path in consumerPaths)
            {
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Contain("AppServerInstalledPluginObservationService.LoadAsync"), path);
            }

            var serviceSource = RepositorySource.ReadSourceFile("AppServerInstalledPluginObservationService.cs");
            Assert.That(serviceSource, Does.Contain("GetInstalledPluginsAsync"));
            Assert.That(serviceSource, Does.Contain("ListPluginsAsync"));
            Assert.That(serviceSource, Does.Contain("plugin.Installed"));
            Assert.That(serviceSource, Does.Not.Contain("Directory."));
            Assert.That(serviceSource, Does.Not.Contain("File."));
        }

        [Test]
        public void RemoteCatalogRegistration_IsExplicitAndDoesNotInstallAPlugin()
        {
            var formOperation = RepositorySource.ReadSourceFile("PluginManagementForm.MarketplaceRegistration.cs");
            var serviceOperation = RepositorySource.ReadSourceFile("AppServerMarketplaceRegistrationService.cs");

            Assert.That(formOperation, Does.Contain("AppServerMarketplaceRegistrationService"));
            Assert.That(serviceOperation, Does.Contain("AddMarketplaceAsync"));
            Assert.That(serviceOperation, Does.Contain("ListPluginsAsync"));
            Assert.That(serviceOperation, Does.Contain("GetInstalledPluginsAsync"));
            Assert.That(formOperation + serviceOperation, Does.Not.Contain("InstallPluginAsync"));
            Assert.That(formOperation + serviceOperation, Does.Not.Contain("InstallRemotePluginAsync"));
            Assert.That(formOperation + serviceOperation, Does.Not.Contain("PackageUrl"));
            Assert.That(formOperation + serviceOperation, Does.Not.Contain("ZipFile"));
            Assert.That(formOperation + serviceOperation, Does.Not.Contain("File."));
        }

        [Test]
        public void MutationPolicy_UsesExplicitCapabilityIndependentOfRuntimeSourceOrVersion()
        {
            var validated = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.Bundled,
                RuntimeVersion = CodexRuntimeSources.BundledDefaultVersion,
                SupportsPluginManagement = true,
                SupportsPluginMutation = true
            });
            var updated = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.Bundled,
                RuntimeVersion = "0.999.0",
                SupportsPluginManagement = false,
                SupportsPluginMutation = true
            });
            var unvalidatedBundled = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.Bundled,
                RuntimeVersion = CodexRuntimeSources.BundledDefaultVersion,
                SupportsPluginManagement = true,
                SupportsPluginMutation = false
            });
            var system = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.System,
                RuntimeVersion = "0.999.0",
                SupportsPluginManagement = true
            });
            var observedSystem = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.System,
                RuntimeVersion = "0.999.0",
                SupportsPluginManagement = true,
                SupportsPluginMutation = true
            });

            Assert.That(validated.IsAvailable, Is.True);
            Assert.That(updated.IsAvailable, Is.True);
            Assert.That(unvalidatedBundled.IsAvailable, Is.False);
            Assert.That(system.IsAvailable, Is.False);
            Assert.That(observedSystem.IsAvailable, Is.True);
        }

        [Test]
        public void MutationPolicy_CurrentBundledRuntime_AllowsOfficialMarketplaceChanges()
        {
            var availability = MarketplaceMutationPolicy.EvaluatePluginMutation(new AppServerProtocolCapabilities
            {
                RuntimeSource = CodexRuntimeSources.Bundled,
                RuntimeVersion = "0.147.0",
                SupportsPluginManagement = true,
                SupportsPluginMutation = true
            });

            Assert.That(availability.IsAvailable, Is.True);
        }

        [Test]
        public void PluginState_UsesDistinctOfficialInstalledObservationWithSharedSummaryMapper()
        {
            var pluginClientMethod = typeof(MyCodex.Application.Interfaces.IAppServerPluginClient)
                .GetMethod("GetInstalledPluginsAsync");
            var operationsSource = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");
            var formSource = RepositorySource.ReadSourceFile("PluginManagementForm.cs");

            Assert.That(pluginClientMethod, Is.Not.Null);
            Assert.That(operationsSource, Does.Contain("plugin/installed"));
            Assert.That(operationsSource, Does.Contain("BuildInstalledPluginSnapshot"));
            Assert.That(formSource, Does.Not.Contain("TryLoadRuntimeInstalledSnapshotAsync"));
        }

        private static string ExtractConstructor(string source)
        {
            return ExtractBlock(source, source.IndexOf("public PluginManagementForm(", StringComparison.Ordinal));
        }

        private static string ExtractMethod(string source, string methodName)
        {
            return ExtractBlock(source, source.IndexOf(methodName + "(", StringComparison.Ordinal));
        }

        private static string ExtractPrivateMethod(string source, string methodName)
        {
            return ExtractBlock(source, source.IndexOf("private bool " + methodName + "(", StringComparison.Ordinal));
        }

        private static string ExtractBlock(string source, int start)
        {
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            var bodyStart = source.IndexOf('{', start);
            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }

            Assert.Fail("PluginManagementForm constructor was not closed.");
            return string.Empty;
        }
    }
}
