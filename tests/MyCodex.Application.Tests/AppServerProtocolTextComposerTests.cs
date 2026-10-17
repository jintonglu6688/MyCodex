using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerProtocolTextComposerTests
    {
        [Test]
        public void StripAnsiEscapeSequences_RemovesColorCodes()
        {
            var text = AppServerProtocolTextComposer.StripAnsiEscapeSequences("\u001b[31merror\u001b[0m");

            Assert.That(text, Is.EqualTo("error"));
        }

        [Test]
        public void BuildRecentTextPreview_TruncatesLongText()
        {
            var text = AppServerProtocolTextComposer.BuildRecentTextPreview(new string('a', 160), 3, 40);

            Assert.That(text.Length, Is.LessThanOrEqualTo(43));
            Assert.That(text, Does.StartWith("aaaaaaaa"));
        }

        [Test]
        public void BuildFileChangeSummaryText_UsesChangedFilesWhenPresent()
        {
            var payload = new Dictionary<string, object>
            {
                {
                    "changes",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "path", "src/App.cs" },
                            { "kind", "update" }
                        }
                    }
                }
            };

            List<string> changedFiles;
            var text = AppServerProtocolTextComposer.BuildFileChangeSummaryText(payload, null, out changedFiles);

            Assert.That(text, Does.Contain("src/App.cs"));
            Assert.That(changedFiles, Does.Contain("src/App.cs"));
        }
    }
}
