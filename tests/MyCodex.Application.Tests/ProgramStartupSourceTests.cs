using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ProgramStartupSourceTests
    {
        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void AppServerClient_UsesConfiguredRuntimeStoreDirectly()
        {
            var source = RepositorySource.ReadSourceFile("Program.cs");
            var obsoleteStoreType = "ModeAware" + "AppConfigStore";
            var obsoleteModeContextType = "AppServerRuntime" + "ModeContext";

            Assert.That(source, Does.Not.Contain(obsoleteStoreType));
            Assert.That(source, Does.Not.Contain(obsoleteModeContextType));
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void ProductionComposition_DoesNotRecreateRetiredAuthenticationStores()
        {
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");

            Assert.That(mainFormSource, Does.Not.Contain("new JsonAuthenticationSourceSelectionStore"));
            Assert.That(mainFormSource, Does.Not.Contain("new JsonAuthenticationProfileStore"));
            Assert.That(mainFormSource, Does.Not.Contain("new JsonChatGptAuthBackupStore"));
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void ProductionComposition_DoesNotCreateRetiredProviderOrModelRoutingServices()
        {
            var source = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(source, Does.Not.Contain("Open" + "Router"));
            Assert.That(source, Does.Not.Contain("Gateway" + "Runtime"));
            Assert.That(source, Does.Not.Contain("Model" + "Relay"));
            Assert.That(source, Does.Not.Contain("Mimo2" + "Codex"));
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void ProductionSources_DoNotRetainProviderRoutingUnderLegacyNames()
        {
            var sourceRoot = Path.GetDirectoryName(Path.GetDirectoryName(RepositorySource.FindSourceFile("Program.cs")));
            var retiredTerms = new[]
            {
                "Open" + "Router",
                "Mimo2" + "Codex",
                "Model" + "Relay",
                "Gateway",
                "Authentication" + "Source",
                "model_" + "catalog_json",
                "model" + "Providers",
                "wire_" + "api"
            };
            var officialPolicyPlanner = Path.Combine(sourceRoot, "MyCodex.Infrastructure", "ComputerUse", "ComputerUseConfigurationPlanner.cs");
            var officialThreadOperations = Path.Combine(sourceRoot, "MyCodex.Infrastructure", "AppServer", "AppServerThreadOperations.cs");
            var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    // ConfigRequirements and thread/list both use this official wire key.
                    // Exempt only the literal in these adapters; routing identifiers stay banned.
                    if (string.Equals(path, officialPolicyPlanner, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, officialThreadOperations, StringComparison.OrdinalIgnoreCase))
                        source = source.Replace("\"modelProviders\"", "\"\"");
                    return retiredTerms.Any(term => source.IndexOf(term, StringComparison.Ordinal) >= 0);
                })
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void BuildAndInstallerSources_DoNotPackageRetiredProviderRuntimes()
        {
            var repositoryRoot = Path.GetDirectoryName(Path.GetDirectoryName(RepositorySource.FindSourceFile("Program.cs")));
            var paths = new[]
            {
                Path.Combine(repositoryRoot, "tools", "build", "Build-MyCodex.ps1"),
                Path.Combine(repositoryRoot, "tools", "installer", "build-installer.ps1"),
                Path.Combine(repositoryRoot, "src", "MyCodex.WinForms", "MyCodex.WinForms.csproj")
            };
            var retiredProviderRuntimeTerms = new[]
            {
                "mimo2" + "codex",
                "runtime\\gate" + "ways",
                "model_" + "catalog_json"
            };

            var offenders = paths
                .Where(File.Exists)
                .Where(path => retiredProviderRuntimeTerms.Any(term => File.ReadAllText(path).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void ProductionAndDistributionSources_DoNotOwnABundledNodeRuntime()
        {
            var repositoryRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(RepositorySource.FindSourceFile("Program.cs"))));
            var paths = new[]
            {
                Path.Combine(repositoryRoot, "src", "MyCodex.Infrastructure", "Persistence", "AppConfigService.cs"),
                Path.Combine(repositoryRoot, "src", "MyCodex.WinForms", "MyCodex.WinForms.csproj"),
                Path.Combine(repositoryRoot, "tools", "build", "Build-MyCodex.ps1"),
                Path.Combine(repositoryRoot, "tools", "installer", "build-installer.ps1")
            };
            var retiredRuntimeTerms = new[]
            {
                "GetNodeRuntime",
                "prepare-node-runtime.ps1",
                "node-runtime.version"
            };
            var offenders = paths
                .Where(File.Exists)
                .Where(path => retiredRuntimeTerms.Any(term => File.ReadAllText(path).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();

            Assert.That(offenders, Is.Empty);
            Assert.That(
                File.ReadAllText(Path.Combine(repositoryRoot, "src", "MyCodex.WinForms", "MyCodex.WinForms.csproj")),
                Does.Not.Contain("runtime\\node"));
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void ProductionSources_DoNotRetainSharedWindowsCodexHomeSupport()
        {
            var sourceRoot = Path.GetDirectoryName(Path.GetDirectoryName(RepositorySource.FindSourceFile("Program.cs")));
            var offendingFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).IndexOf("SharedWindowsHome", StringComparison.Ordinal) >= 0)
                .ToArray();

            Assert.That(offendingFiles, Is.Empty);
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void Program_ResolvesTargetBeforeSettingsAndRoutesOnlyFiniteActivation()
        {
            var source = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(source, Does.Contain("mainForm.ApplyStartupArguments(args)"));
            Assert.That(source, Does.Contain("TargetStartupPlanner.Resolve(snapshot, options.TargetId, options.WorkspacePath)"));
            Assert.That(source.IndexOf("TargetInstanceCoordinator.Acquire", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("catalog.GetPaths(selection.Target.Id)", StringComparison.Ordinal)));
            Assert.That(source.IndexOf("catalog.GetPaths(selection.Target.Id)", StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("InitializeUiPreferences(appConfigStore)", StringComparison.Ordinal)));
            Assert.That(source, Does.Contain("Operation = TargetInstanceOperation.Activate, WorkspacePath = selection.WorkspacePath"));
            Assert.That(source, Does.Contain("instance.StartServer(mainForm.HandleTargetInstanceRequestAsync)"));
            Assert.That(source, Does.Not.Contain("NamedPipeServerStream"));
            Assert.That(source, Does.Not.Contain("ReadToEnd()"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void Program_DoesNotReloadConfigBeforeMainFormRecoveryBoundary()
        {
            var source = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(source, Does.Contain("CreateInternalToolRegistry()"));
            Assert.That(source, Does.Not.Contain("CreateInternalToolRegistry(appConfigStore.Load())"));
            Assert.That(source, Does.Not.Contain("appConfigService.EnsureConfigDirectory();"));
        }
    }
}
