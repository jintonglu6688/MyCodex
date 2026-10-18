using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class TextFileOpenPreferenceWiringTests
    {
        [Test]
        public void MainForm_WiresTextFileOpenPreferenceProviderToGitWorkbench()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.DiffWorkbench.cs");

            Assert.That(source, Does.Contain("BuildTextFileOpenPreference"));
            Assert.That(source, Does.Contain("textFileOpenPreferenceProvider"));
        }

        [Test]
        public void MainForm_WiresTextFileOpenPreferenceProviderToAppServerFileWindowManager()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.AppServerFileWindows.cs");

            Assert.That(source, Does.Contain("BuildTextFileOpenPreference"));
            Assert.That(source, Does.Contain("textFileOpenPreferenceProvider"));
        }
    }
}
