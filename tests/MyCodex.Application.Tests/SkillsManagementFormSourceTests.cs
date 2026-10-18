using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SkillsManagementFormSourceTests
    {
        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void SkillsManagementForm_DoesNotExposeAStandaloneSkillMarketplace()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Not.Contain("_marketTabPage"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void SkillsManagementMenuItem_DoesNotSelectAStandaloneSkillMarketTarget()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Contain("new SkillsManagementForm("));
            Assert.That(source, Does.Not.Contain("IsSharedWindowsCodexHomeEnabled()"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void SkillsManagementForm_UsesAppServerFileSystemForUserSkillWrites()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("OfficialSkillFileOperations"));
            Assert.That(source, Does.Contain("IAppServerFileSystemClient"));
            Assert.That(source, Does.Not.Contain("Process.Start("));
            Assert.That(source, Does.Contain("source.Kind != SkillSourceKind.User && source.Kind != SkillSourceKind.Workspace"));

            var editorSource = RepositorySource.ReadSourceFile("SkillEditForm.cs");
            Assert.That(editorSource, Does.Not.Contain("SkillFileEditor.Save("));
            Assert.That(editorSource, Does.Not.Contain("Directory.CreateDirectory("));
            Assert.That(editorSource, Does.Not.Contain("Process.Start("));
        }

        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void SkillFileEditor_IsPureAndDoesNotReadOrWriteTargetFiles()
        {
            var source = RepositorySource.ReadSourceFile("SkillFileEditor.cs");

            Assert.That(source, Does.Not.Contain("File."));
            Assert.That(source, Does.Not.Contain("Directory."));
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void SkillsManagementForm_DoesNotShowTabInstructionText()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Not.Contain("AccessibleName = UiLocalization.Text(\"SkillsManagementForm.Label.Skills\", \"Skills\")"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void SkillsManagementForm_UninstallUsesObservedUserSkillPathThroughAppServerFileSystem()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("UninstallSelectedSkillAsync"));
            Assert.That(source, Does.Contain("CanUninstallSkill"));
            Assert.That(source, Does.Contain("ResolveSkillSource(skill).Kind == SkillSourceKind.User"));
            Assert.That(source, Does.Contain("_skillFileOperations.DeleteAsync(skill)"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
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
