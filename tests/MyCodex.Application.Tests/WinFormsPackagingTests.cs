using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WinFormsPackagingTests
    {
        [TestCase("DocumentFormat.OpenXml")]
        [TestCase("ClosedXML")]
        [TestCase("OfficeIMO.PowerPoint")]
        [TestCase("OfficeIMO.Word")]
        [TestCase("ShapeCrawler")]
        public void MainAppProjects_DoNotDeclareOfficeDocumentRuntimePackage(string packageId)
        {
            var packageReferences = new[]
                {
                    XDocument.Load(GetRepositoryPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj")),
                    XDocument.Load(GetRepositoryPath("src", "MyCodex.Infrastructure", "MyCodex.Infrastructure.csproj"))
                }
                .SelectMany(project => project.Root.Descendants("PackageReference"))
                .Select(element => (string)element.Attribute("Include"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(packageReferences, Does.Not.Contain(packageId));
        }

        [Test]
        public void WinFormsProject_CopiesPackageRuntimeDependenciesWhenUsingCommonOutputDirectory()
        {
            var project = XDocument.Load(GetRepositoryPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj"));
            var copyTarget = project.Root
                .Descendants("Target")
                .SingleOrDefault(element => string.Equals((string)element.Attribute("Name"), "CopyWinFormsPackageRuntimeDependencies", StringComparison.Ordinal));

            Assert.That(copyTarget, Is.Not.Null);
            Assert.That((string)copyTarget.Attribute("AfterTargets"), Is.EqualTo("ResolveReferences"));
            Assert.That((string)copyTarget.Attribute("Condition"), Does.Contain("UseCommonOutputDirectory"));
            Assert.That(copyTarget.Descendants("Copy").Any(), Is.True);
            Assert.That(copyTarget.ToString(SaveOptions.DisableFormatting), Does.Contain("ReferenceCopyLocalPaths"));
            Assert.That(copyTarget.ToString(SaveOptions.DisableFormatting), Does.Contain("NuGetPackageId"));
        }

        [Test]
        public void WinFormsProject_DoesNotCopyNativePackageDependenciesToOutputRoot()
        {
            var project = XDocument.Load(GetRepositoryPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj"));
            var copyTarget = project.Root
                .Descendants("Target")
                .SingleOrDefault(element => string.Equals((string)element.Attribute("Name"), "CopyWinFormsPackageRuntimeDependencies", StringComparison.Ordinal));

            Assert.That(copyTarget, Is.Not.Null);
            Assert.That(
                copyTarget.ToString(SaveOptions.DisableFormatting),
                Does.Contain("('%(ReferenceCopyLocalPaths.NuGetPackageId)' != 'Scintilla5.NET' Or '%(ReferenceCopyLocalPaths.AssetType)' != 'native')"));
        }

        [Test]
        public void WinFormsProject_RemovesUnsupportedNuGetRuntimeLayoutsAfterCopyingRuntimeDependencies()
        {
            var project = XDocument.Load(GetRepositoryPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj"));
            var removeTarget = project.Root
                .Descendants("Target")
                .SingleOrDefault(element => string.Equals((string)element.Attribute("Name"), "RemoveWinFormsUnsupportedRuntimeAssetLayouts", StringComparison.Ordinal));

            Assert.That(removeTarget, Is.Not.Null);
            Assert.That((string)removeTarget.Attribute("AfterTargets"), Is.EqualTo("CopyFilesToOutputDirectory"));
            Assert.That((string)removeTarget.Attribute("Condition"), Does.Contain("UseCommonOutputDirectory"));
            var targetText = removeTarget.ToString(SaveOptions.DisableFormatting);
            Assert.That(targetText, Does.Contain("$(OutDir)runtimes\\win-x86"));
            Assert.That(targetText, Does.Contain("$(OutDir)runtimes\\win-arm64"));
            Assert.That(targetText, Does.Not.Contain("$(OutDir)runtimes\""));
            Assert.That(targetText, Does.Not.Contain("$(OutDir)runtimes\\win-x64"));
        }

        [Test]
        public void WinFormsAppConfig_DisablesWinFormsUiaAccessibilityImprovements()
        {
            var appConfigPath = GetRepositoryPath("src", "MyCodex.WinForms", "App.config");
            Assert.That(File.Exists(appConfigPath), Is.True, "WinForms App.config should configure process-wide AppContext switches.");

            var appConfig = XDocument.Load(appConfigPath);
            var overrides = appConfig.Root
                .Element("runtime")
                .Element("AppContextSwitchOverrides");
            var values = ((string)overrides.Attribute("value") ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            var expectedSwitches = new[]
            {
                "Switch.UseLegacyAccessibilityFeatures=true",
                "Switch.UseLegacyAccessibilityFeatures.2=true",
                "Switch.UseLegacyAccessibilityFeatures.3=true",
                "Switch.UseLegacyAccessibilityFeatures.4=true",
                "Switch.UseLegacyAccessibilityFeatures.5=true",
                "Switch.System.Windows.Forms.UseLegacyToolTipDisplay=true"
            };

            foreach (var expectedSwitch in expectedSwitches)
            {
                Assert.That(values, Does.Contain(expectedSwitch));
            }
        }

        [Test]
        public void DirectoryBuildProps_UsesRootOutputOnlyWhenTestBuildOutputRootIsEmpty()
        {
            var project = XDocument.Load(GetRepositoryPath("Directory.Build.props"));
            var propertyGroups = project.Root
                .Elements("PropertyGroup")
                .ToArray();

            Assert.That(
                propertyGroups.Any(group =>
                    string.Equals((string)group.Attribute("Condition"), "'$(Configuration)' == 'Debug' And '$(MyCodexTestBuildOutputRoot)' == ''", StringComparison.Ordinal) &&
                    string.Equals((string)group.Element("OutputPath"), "$(MSBuildThisFileDirectory)debug\\", StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                propertyGroups.Any(group =>
                    string.Equals((string)group.Attribute("Condition"), "'$(Configuration)' == 'Release' And '$(MyCodexTestBuildOutputRoot)' == ''", StringComparison.Ordinal) &&
                    string.Equals((string)group.Element("OutputPath"), "$(MSBuildThisFileDirectory)Release\\", StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                propertyGroups.Any(group =>
                    string.Equals((string)group.Attribute("Condition"), "'$(MyCodexTestBuildOutputRoot)' != ''", StringComparison.Ordinal) &&
                    string.Equals((string)group.Element("UseCommonOutputDirectory"), "false", StringComparison.Ordinal) &&
                    string.Equals((string)group.Element("OutputPath"), "$(BaseOutputPath)$(Configuration)\\", StringComparison.Ordinal)),
                Is.True);

            var projectReferenceDefinition = project.Root
                .Elements("ItemDefinitionGroup")
                .Where(group => string.Equals((string)group.Attribute("Condition"), "'$(MyCodexTestBuildOutputRoot)' != ''", StringComparison.Ordinal))
                .Elements("ProjectReference")
                .SingleOrDefault();
            Assert.That(projectReferenceDefinition, Is.Not.Null);
            Assert.That(
                (string)projectReferenceDefinition.Element("AdditionalProperties"),
                Is.EqualTo("MyCodexTestBuildOutputRoot=$(MyCodexTestBuildOutputRoot)"));
        }

        [Test]
        public void TestProject_PassesIsolatedOutputRootToProjectReferences()
        {
            var project = XDocument.Load(GetRepositoryPath("tests", "MyCodex.Application.Tests", "MyCodex.Application.Tests.csproj"));
            Assert.That(
                project.Root
                    .Elements("PropertyGroup")
                    .Any(group => string.Equals((string)group.Element("MyCodexTestBuildOutputRoot"), "$(MSBuildThisFileDirectory)bin\\ProjectReferences\\", StringComparison.Ordinal)),
                Is.True);

            var projectReferences = project.Root
                .Descendants("ProjectReference")
                .ToArray();

            Assert.That(projectReferences, Has.Length.EqualTo(4));
            foreach (var projectReference in projectReferences)
            {
                Assert.That(
                    (string)projectReference.Attribute("AdditionalProperties"),
                    Is.EqualTo("MyCodexTestBuildOutputRoot=$(MSBuildThisFileDirectory)bin\\ProjectReferences\\"));
            }
        }

        private static string GetRepositoryPath(params string[] parts)
        {
            var path = Path.Combine(new[]
            {
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                ".."
            }.Concat(parts ?? Array.Empty<string>()).ToArray());

            return Path.GetFullPath(path);
        }

    }
}
