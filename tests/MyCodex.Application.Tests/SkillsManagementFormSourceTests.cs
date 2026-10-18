using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SkillsManagementFormSourceTests
    {
        [Test]
        public void SkillsManagementForm_DoesNotExposeAStandaloneSkillMarketplace()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");
            var programSource = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(source, Does.Not.Contain("SkillMarket"));
            Assert.That(source, Does.Not.Contain("_marketTabPage"));
            Assert.That(source, Does.Not.Contain("InstallSelectedMarketSkillAsync"));
            Assert.That(programSource, Does.Not.Contain("SkillMarketCatalogClient"));
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Application", "Interfaces", "ISkillMarketCatalogClient.cs")), Is.False);
            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.WinForms", "Forms", "SkillMarketListComposer.cs")), Is.False);
        }

        [Test]
        public void SkillsManagementMenuItem_DoesNotSelectAStandaloneSkillMarketTarget()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Contain("new SkillsManagementForm("));
            Assert.That(source, Does.Not.Contain("IsSharedWindowsCodexHomeEnabled()"));
        }

        [Test]
        public void SkillsManagementForm_UsesAppServerFileSystemForUserSkillWrites()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("OfficialSkillFileOperations"));
            Assert.That(source, Does.Contain("IAppServerFileSystemClient"));
            Assert.That(source, Does.Not.Contain("RemoteMacSkillFileEditor"));
            Assert.That(source, Does.Not.Contain("Process.Start("));
            Assert.That(source, Does.Contain("source.Kind != SkillSourceKind.User && source.Kind != SkillSourceKind.Workspace"));

            var editorSource = RepositorySource.ReadSourceFile("SkillEditForm.cs");
            Assert.That(editorSource, Does.Not.Contain("SkillFileEditor.Save("));
            Assert.That(editorSource, Does.Not.Contain("Directory.CreateDirectory("));
            Assert.That(editorSource, Does.Not.Contain("Process.Start("));
        }

        [Test]
        public void SkillFileEditor_IsPureAndDoesNotReadOrWriteTargetFiles()
        {
            var source = RepositorySource.ReadSourceFile("SkillFileEditor.cs");

            Assert.That(source, Does.Not.Contain("File."));
            Assert.That(source, Does.Not.Contain("Directory."));
        }

        [Test]
        public void SkillsManagementForm_DoesNotShowTabInstructionText()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Not.Contain("SkillsManagementForm.Header"));
            Assert.That(source, Does.Not.Contain("SkillsManagementForm.Tip"));
            Assert.That(source, Does.Not.Contain("SkillsManagementForm.Accessible.Tabs"));
            Assert.That(source, Does.Not.Contain("AccessibleName = UiLocalization.Text(\"SkillsManagementForm.Label.Skills\", \"Skills\")"));
        }

        [Test]
        public void SkillsManagementForm_UninstallUsesObservedUserSkillPathThroughAppServerFileSystem()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("UninstallSelectedSkillAsync"));
            Assert.That(source, Does.Contain("CanUninstallSkill"));
            Assert.That(source, Does.Contain("ResolveSkillSource(skill).Kind == SkillSourceKind.User"));
            Assert.That(source, Does.Contain("_skillFileOperations.DeleteAsync(skill)"));
            Assert.That(source, Does.Not.Contain("SkillUserUninstallService"));
            Assert.That(source, Does.Not.Contain("RemoveInstalledSkillBySourcePath"));
        }

        [Test]
        public void SkillsManagementForm_CancelsLifetimeWorkBeforeClose()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("CancellationTokenSource _lifetimeCancellation"));
            Assert.That(source, Does.Contain("_lifetimeCancellation.Cancel();"));
            Assert.That(source, Does.Not.Contain("LoadIndexAsync(System.Threading.CancellationToken.None)"));
        }
    }
}
