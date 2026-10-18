using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitWorkbenchFormTests
    {
        [Test]
        public void DiffFocusDoesNotLoadAndExplicitLoadDiffRemainsAvailable()
        {
            var source = RepositorySource.ReadSourceFile("GitWorkbenchForm.cs");

            Assert.That(source, Does.Not.Contain("_diffTextBox.Enter += async"));
            Assert.That(source, Does.Not.Contain("EnsureSelectedDiffLoadedOnDiffFocusAsync"));
            Assert.That(source, Does.Contain("_loadDiffMenuItem.Click += async (sender, e) => await LoadSelectedDiffAsync();"));
        }
    }
}
