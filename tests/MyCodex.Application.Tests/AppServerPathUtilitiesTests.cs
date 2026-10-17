using System;
using System.IO;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPathUtilitiesTests
    {
        [Test]
        public void NormalizeDisplayPath_RemovesWindowsLongPathPrefix()
        {
            var result = AppServerPathUtilities.NormalizeDisplayPath(@"\\?\E:\Projects\demo");

            Assert.That(result, Is.EqualTo(@"E:\Projects\demo"));
        }

        [Test]
        public void NormalizeDisplayPath_ReturnsEmptyStringForBlankInput()
        {
            Assert.That(AppServerPathUtilities.NormalizeDisplayPath(null), Is.EqualTo(string.Empty));
            Assert.That(AppServerPathUtilities.NormalizeDisplayPath("   "), Is.EqualTo(string.Empty));
        }

        [Test]
        public void NormalizeLocalPath_TrimsWhitespaceAndQuotesBeforeResolving()
        {
            var expected = Path.GetFullPath(@"E:\Projects\demo");

            var result = AppServerPathUtilities.NormalizeLocalPath("  \"E:\\Projects\\demo\"  ");

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void NormalizeLocalPath_ReturnsEmptyStringForBlankInput()
        {
            Assert.That(AppServerPathUtilities.NormalizeLocalPath(null), Is.EqualTo(string.Empty));
            Assert.That(AppServerPathUtilities.NormalizeLocalPath("   "), Is.EqualTo(string.Empty));
        }

        [Test]
        public void LooksLikeUnixAbsolutePath_DetectsOnlySlashPrefixedPaths()
        {
            Assert.That(AppServerPathUtilities.LooksLikeUnixAbsolutePath("/mnt/e/demo"), Is.True);
            Assert.That(AppServerPathUtilities.LooksLikeUnixAbsolutePath("   /home/user"), Is.True);
            Assert.That(AppServerPathUtilities.LooksLikeUnixAbsolutePath(@"E:\Projects\demo"), Is.False);
            Assert.That(AppServerPathUtilities.LooksLikeUnixAbsolutePath("relative/path"), Is.False);
            Assert.That(AppServerPathUtilities.LooksLikeUnixAbsolutePath(null), Is.False);
        }
    }
}
