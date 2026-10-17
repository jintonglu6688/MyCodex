using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WindowsFolderPickerSourceTests
    {
        [Test]
        public void WinFormsFolderSelection_UsesCommonItemDialogInsteadOfLegacyFolderBrowserDialog()
        {
            var winFormsSourceRoot = GetRepositoryPath("src", "MyCodex.WinForms");
            var sourceFiles = Directory.GetFiles(winFormsSourceRoot, "*.cs", SearchOption.AllDirectories);
            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = sourceFile.Substring(winFormsSourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var source = File.ReadAllText(sourceFile);

                Assert.That(source, Does.Not.Contain("FolderBrowserDialog"), relativePath);
            }
        }

        [Test]
        public void WindowsFolderPicker_UsesVistaCommonFileDialogPickFoldersMode()
        {
            var source = File.ReadAllText(GetRepositoryPath("src", "MyCodex.WinForms", "Dialogs", "WindowsFolderPicker.cs"));

            Assert.That(source, Does.Contain("IFileOpenDialog"));
            Assert.That(source, Does.Contain("FOS_PICKFOLDERS"));
            Assert.That(source, Does.Contain("SetFolder"));
            Assert.That(source, Does.Contain("GetResult"));
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
