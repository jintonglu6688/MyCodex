using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class StructuredMessageViewerFormSourceTests
    {
        [Test]
        public void RenderCodeBlock_AppendsCodeTextLineByLineForTextNavigation()
        {
            var source = RepositorySource.ReadSourceFile("StructuredMessageViewerForm.cs");
            var renderBlock = ExtractBlock(
                source,
                "private void RenderCodeBlock(int index, string language, string codeText)",
                "private void AppendText(string text, Font font, Color color, Color? backColor)");

            Assert.That(renderBlock, Does.Contain("AppendCodeTextLines(codeText, _codeFont, UiTheme.TextColor, GetCodeBlockBackColor());"));
            Assert.That(renderBlock, Does.Not.Contain("AppendText(codeText, _codeFont, UiTheme.TextColor, GetCodeBlockBackColor());"));
        }

        private static string ExtractBlock(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Start marker not found: " + startMarker);
            var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), "End marker not found: " + endMarker);
            return source.Substring(start, end - start);
        }
    }
}
