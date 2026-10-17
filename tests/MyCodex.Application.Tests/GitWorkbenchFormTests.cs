using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitWorkbenchFormTests
    {
        [Test]
        public void DiffFocusDoesNotLoadAndExplicitLoadDiffRemainsAvailable()
        {
            var source = File.ReadAllText(GetSourcePath());

            Assert.That(source, Does.Not.Contain("_diffTextBox.Enter += async"));
            Assert.That(source, Does.Not.Contain("EnsureSelectedDiffLoadedOnDiffFocusAsync"));
            Assert.That(source, Does.Contain("_loadDiffMenuItem.Click += async (sender, e) => await LoadSelectedDiffAsync();"));
        }

        private static string GetSourcePath()
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms",
                "GitWorkbenchForm.cs"));
        }
    }
}
