using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RuntimeFileSystemPathRulesTests
    {
        [Test]
        public void ValidateName_RejectsEmptyAndRelativeNames()
        {
            Assert.That(RuntimeFileSystemPathRules.ValidateName(null).Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.ValidateName("  ").Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.ValidateName(".").Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.ValidateName("..").Success, Is.False);
        }

        [Test]
        public void ValidateName_RejectsPathSeparators()
        {
            Assert.That(RuntimeFileSystemPathRules.ValidateName("a/b").Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.ValidateName(@"a\b").Success, Is.False);
        }

        [Test]
        public void ValidateName_AllowsSimpleFileName()
        {
            var result = RuntimeFileSystemPathRules.ValidateName("notes.md");

            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void CombineChildPath_UsesUnixSeparatorForUnixParent()
        {
            var path = RuntimeFileSystemPathRules.CombineChildPath("/home/demo", "notes.md");

            Assert.That(path, Is.EqualTo("/home/demo/notes.md"));
        }

        [Test]
        public void CombineChildPath_UsesWindowsSeparatorForWindowsParent()
        {
            var path = RuntimeFileSystemPathRules.CombineChildPath(@"E:\Repo", "notes.md");

            Assert.That(path, Is.EqualTo(@"E:\Repo\notes.md"));
        }

        [Test]
        public void GetSiblingPath_UsesExistingParent()
        {
            var path = RuntimeFileSystemPathRules.GetSiblingPath("/home/demo/old.txt", "new.txt");

            Assert.That(path, Is.EqualTo("/home/demo/new.txt"));
        }

        [Test]
        public void EnsureSafeDeleteTarget_BlocksRootPaths()
        {
            Assert.That(RuntimeFileSystemPathRules.EnsureSafeDeleteTarget("/").Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.EnsureSafeDeleteTarget(@"E:\").Success, Is.False);
            Assert.That(RuntimeFileSystemPathRules.EnsureSafeDeleteTarget(@"E:").Success, Is.False);
        }

        [Test]
        public void EnsureSafeDeleteTarget_AllowsNestedPaths()
        {
            Assert.That(RuntimeFileSystemPathRules.EnsureSafeDeleteTarget("/home/demo/file.txt").Success, Is.True);
            Assert.That(RuntimeFileSystemPathRules.EnsureSafeDeleteTarget(@"E:\Repo\file.txt").Success, Is.True);
        }
    }
}
