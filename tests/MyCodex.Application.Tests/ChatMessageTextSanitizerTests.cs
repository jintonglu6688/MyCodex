using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ChatMessageTextSanitizerTests
    {
        [Test]
        public void SanitizeForDisplay_RemovesEmojiAndMarkdownNoiseButKeepsText()
        {
            var tick = char.ConvertFromUtf32(96);
            var input = string.Join(System.Environment.NewLine, new[]
            {
                "# Title " + char.ConvertFromUtf32(0x2705),
                "**Bold**:",
                "- Update " + tick + "config.toml" + tick,
                "- Restart " + char.ConvertFromUtf32(0x1F680),
                "[Docs](https://example.com)"
            });

            var sanitized = ChatMessageTextSanitizer.SanitizeForDisplay(input);

            Assert.That(sanitized, Is.EqualTo("Title Bold: Update config.toml Restart Docs"));
        }

        [Test]
        public void SanitizeForDisplay_PreservesCommandFlagsPathsAndCodeText()
        {
            var tick = char.ConvertFromUtf32(96);
            var input = "Run " + tick + "dotnet test -c Debug src\\MyCodex.csproj /p:Platform=x64" + tick;

            var sanitized = ChatMessageTextSanitizer.SanitizeForDisplay(input);
            Assert.That(sanitized, Is.EqualTo("Run dotnet test -c Debug src\\MyCodex.csproj /p:Platform=x64"));
        }

        [Test]
        public void SanitizeForReadableLines_RemovesEmptyMarkdownLines()
        {
            var input = string.Join(System.Environment.NewLine, new[]
            {
                "> # Step " + char.ConvertFromUtf32(0x2705),
                string.Empty,
                "- **Open** settings",
                "---",
                "Done " + char.ConvertFromUtf32(0x1F389)
            });

            var lines = ChatMessageTextSanitizer.SanitizeForReadableLines(input);

            Assert.That(lines, Is.EqualTo(new[] { "Step", "Open settings", "Done" }));
        }

        [Test]
        public void SanitizeForDisplay_HandlesInvalidSurrogateText()
        {
            var input = "Reply " + '\uD83D' + " text";

            var sanitized = ChatMessageTextSanitizer.SanitizeForDisplay(input);

            Assert.That(sanitized, Is.EqualTo("Reply text"));
        }
    }
}
