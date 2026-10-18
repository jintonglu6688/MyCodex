using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WindowsFolderPickerSourceTests
    {
        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void WinFormsFolderSelection_UsesCommonItemDialogInsteadOfLegacyFolderBrowserDialog()
        {
            var winFormsSourceRoot = RepositorySource.GetPath("src", "MyCodex.WinForms");
            var sourceFiles = Directory.GetFiles(winFormsSourceRoot, "*.cs", SearchOption.AllDirectories);
            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = sourceFile.Substring(winFormsSourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var source = File.ReadAllText(sourceFile);

                Assert.That(source, Does.Not.Contain("FolderBrowserDialog"), relativePath);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void WindowsFolderPicker_UsesVistaCommonFileDialogPickFoldersMode()
        {
            var source = RepositorySource.ReadSourceFile("WindowsFolderPicker.cs");

            Assert.That(source, Does.Contain("IFileOpenDialog"));
            Assert.That(source, Does.Contain("FOS_PICKFOLDERS"));
            Assert.That(source, Does.Contain("SetFolder"));
            Assert.That(source, Does.Contain("GetResult"));
        }
    }
}
