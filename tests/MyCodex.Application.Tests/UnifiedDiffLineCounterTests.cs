using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnifiedDiffLineCounterTests
    {
        [TestCase(null, 0, 0)]
        [TestCase("", 0, 0)]
        [TestCase("   ", 0, 0)]
        [TestCase("diff --git a/file b/file\n--- a/file\n+++ b/file\n@@ -1 +1 @@\n", 0, 0)]
        [TestCase("Binary files a/image.png and b/image.png differ", 0, 0)]
        [TestCase("--- a/file\n+++ b/file\n-old\n+new\n context", 1, 1)]
        [TestCase("--- a/file\r\n+++ b/file\r\n-old\r\n+new\r\n", 1, 1)]
        [TestCase("--- a/file\r+++ b/file\r-old\r+new\r", 1, 1)]
        public void Count_ReturnsExpectedTotals(string diffText, int additions, int deletions)
        {
            var result = UnifiedDiffLineCounter.Count(diffText);

            Assert.That(result.Additions, Is.EqualTo(additions));
            Assert.That(result.Deletions, Is.EqualTo(deletions));
        }

        [Test]
        public void Count_RepeatedInvocationIsStable()
        {
            const string diffText = "--- a/file\n+++ b/file\n@@ -1 +1 @@\n-old\n+new\n";

            var first = UnifiedDiffLineCounter.Count(diffText);
            var second = UnifiedDiffLineCounter.Count(diffText);

            Assert.That(second.Additions, Is.EqualTo(first.Additions));
            Assert.That(second.Deletions, Is.EqualTo(first.Deletions));
        }
    }
}
