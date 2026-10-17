using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextFileOpenPreferenceWiringTests
    {
        [Test]
        public void MainForm_WiresTextFileOpenPreferenceProviderToGitWorkbench()
        {
            var source = ReadSource("MainForm.DiffWorkbench.cs");

            Assert.That(source, Does.Contain("BuildTextFileOpenPreference"));
            Assert.That(source, Does.Contain("textFileOpenPreferenceProvider"));
        }

        [Test]
        public void MainForm_WiresTextFileOpenPreferenceProviderToAppServerFileWindowManager()
        {
            var source = ReadSource("MainForm.AppServerFileWindows.cs");

            Assert.That(source, Does.Contain("BuildTextFileOpenPreference"));
            Assert.That(source, Does.Contain("textFileOpenPreferenceProvider"));
        }

        private static string ReadSource(string fileName)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null);
            return File.ReadAllText(Path.Combine(directory.FullName, "src", "MyCodex.WinForms", "Forms", fileName));
        }
    }
}
