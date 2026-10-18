using System.IO;
using MyCodex.Application.Services;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Skills;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedTextAndPathHelperTests
    {
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase(" value ", "value")]
        [TestCase("value", "value")]
        public void TrimToNull_ReturnsTrimmedValueOrNull(string value, string expected)
        {
            Assert.That(TextValueHelper.TrimToNull(value), Is.EqualTo(expected));
        }

        [Test]
        public void FirstNonEmptyTrimmed_ReturnsFirstNonBlankValueTrimmed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TextValueHelper.FirstNonEmptyTrimmed(null, " ", " second ", "third"), Is.EqualTo("second"));
                Assert.That(TextValueHelper.FirstNonEmptyTrimmed(null, " "), Is.EqualTo(string.Empty));
                Assert.That(TextValueHelper.FirstNonEmptyTrimmed(), Is.EqualTo(string.Empty));
                Assert.That(TextValueHelper.FirstNonEmptyTrimmed((string[])null), Is.EqualTo(string.Empty));
            });
        }

        [Test]
        public void NormalizeLocalOrUnixPath_NormalizesUnixAndLocalPaths()
        {
            var localPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "a", "..", "b");

            Assert.Multiple(() =>
            {
                Assert.That(WorkspacePathUtilities.NormalizeLocalOrUnixPath(null), Is.EqualTo(string.Empty));
                Assert.That(WorkspacePathUtilities.NormalizeLocalOrUnixPath("  "), Is.EqualTo(string.Empty));
                Assert.That(WorkspacePathUtilities.NormalizeLocalOrUnixPath(" /home/user/./repo/../work/ "), Is.EqualTo("/home/user/work"));
                Assert.That(WorkspacePathUtilities.NormalizeLocalOrUnixPath(" " + localPath + " "), Is.EqualTo(Path.GetFullPath(localPath)));
                Assert.That(WorkspacePathUtilities.NormalizeLocalOrUnixPath(" bad|path "), Is.EqualTo("bad|path"));
            });
        }

        [TestCase(null, "")]
        [TestCase("  ", "")]
        [TestCase(@" C:\Users\me\.codex\skills\ ", "C:/Users/me/.codex/skills")]
        [TestCase("/home/me/skills//", "/home/me/skills")]
        public void PortableSkillPathNormalize_UsesForwardSlashesWithoutTrailingSlash(string path, string expected)
        {
            Assert.That(PortableSkillPath.Normalize(path), Is.EqualTo(expected));
        }
    }
}
