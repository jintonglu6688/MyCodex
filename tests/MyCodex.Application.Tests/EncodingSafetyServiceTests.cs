using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class EncodingSafetyServiceTests
    {
        [Test]
        public void ScanDiffSnapshot_WithReplacementCharacterInAddedLine_FlagsIssue()
        {
            var result = ScanAddedLine("src/test.cs", "const string Text = \"Broken \uFFFD\";");

            Assert.That(result.HasIssues, Is.True);
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].FilePath, Is.EqualTo("src/test.cs"));
            Assert.That(result.Issues[0].LineNumber, Is.EqualTo(7));
        }

        [Test]
        public void ScanDiffSnapshot_WithValidUtf8Text_DoesNotFlagIssue()
        {
            var result = ScanAddedLine("docs/readme.md", "const string Text = \"\u4E2D\u6587 Caf\u00E9\";");

            Assert.That(result.HasIssues, Is.False);
            Assert.That(result.Issues, Is.Empty);
        }

        [Test]
        public void ScanDiffSnapshot_WithKnownMojibakeMarker_FlagsIssue()
        {
            var result = ScanAddedLine("Resources/Strings.resx", "<value>\u951F\u65A4\u62F7</value>");

            Assert.That(result.HasIssues, Is.True);
            Assert.That(result.Issues[0].Reason, Is.EqualTo("replacement mojibake"));
        }

        private static EncodingSafetyScanResult ScanAddedLine(string filePath, string addedLine)
        {
            var snapshot = new GitDiffSnapshot();
            snapshot.Files.Add(new GitDiffFileEntry
            {
                FilePath = filePath,
                DiffText = string.Join(
                    "\n",
                    new[]
                    {
                        "diff --git a/" + filePath + " b/" + filePath,
                        "--- a/" + filePath,
                        "+++ b/" + filePath,
                        "@@ -0,0 +7,1 @@",
                        "+" + addedLine
                    })
            });

            return new EncodingSafetyService().ScanDiffSnapshot(snapshot);
        }
    }
}
