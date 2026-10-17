using System;
using System.Reflection;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MessageMarkdownHtmlRendererTests
    {
        [Test]
        public void RenderDocument_ProducesSemanticHtmlAndEscapesMessageContent()
        {
            var html = RenderDocument(
                "# Heading <script>alert(1)</script>\r\n\r\n" +
                "- First item\r\n" +
                "- [Project](https://example.com/docs)\r\n\r\n" +
                "```csharp\r\n" +
                "if (a < b) return;\r\n" +
                "```\r\n");

            Assert.That(html, Does.Contain("<title>&#8203;</title>"));
            Assert.That(html, Does.Not.Contain("<title>Message content</title>"));
            Assert.That(html, Does.Contain("<div id=\"content\" tabindex=\"-1\">"));
            Assert.That(html, Does.Contain("</div>"));
            Assert.That(html, Does.Not.Contain("<main"));
            Assert.That(html, Does.Not.Contain("</main>"));
            Assert.That(html, Does.Contain("<h1>Heading &lt;script&gt;alert(1)&lt;/script&gt;</h1>"));
            Assert.That(html, Does.Contain("<ul>"));
            Assert.That(html, Does.Contain("<li>First item</li>"));
            Assert.That(html, Does.Contain("<a href=\"https://example.com/docs\">Project</a>"));
            Assert.That(html, Does.Contain("<button type=\"button\" class=\"copy-code-button\" data-code=\"if (a &lt; b) return;\">"));
            Assert.That(html, Does.Contain("</button>"));
            Assert.That(html, Does.Not.Contain("Copy code</button>"));
            Assert.That(html, Does.Not.Contain("复制代码</button>"));
            Assert.That(html, Does.Contain("<pre tabindex=\"-1\"><code tabindex=\"-1\" class=\"language-csharp\">if (a &lt; b) return;</code></pre>"));
            Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"));
        }

        [Test]
        public void RenderDocument_ProducesSemanticTableForMarkdownTable()
        {
            var html = RenderDocument(
                "| Element | Expected |\r\n" +
                "| --- | --- |\r\n" +
                "| Heading | Use heading navigation |\r\n" +
                "| Link | Use link navigation |\r\n");

            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Contain("<thead>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Element</th>"));
            Assert.That(html, Does.Contain("<th scope=\"col\">Expected</th>"));
            Assert.That(html, Does.Contain("</thead>"));
            Assert.That(html, Does.Contain("<tbody>"));
            Assert.That(html, Does.Contain("<td>Heading</td>"));
            Assert.That(html, Does.Contain("<td>Use heading navigation</td>"));
            Assert.That(html, Does.Contain("<td>Link</td>"));
            Assert.That(html, Does.Contain("<td>Use link navigation</td>"));
            Assert.That(html, Does.Contain("</tbody>"));
            Assert.That(html, Does.Contain("</table>"));
        }

        [Test]
        public void RenderDocument_PreservesSoftLineBreaksInPlainMessageParagraphs()
        {
            var html = RenderDocument("line one\r\nline two\nline three");

            Assert.That(html, Does.Contain("<p>line one<br>line two<br>line three</p>"));
            Assert.That(html, Does.Not.Contain("<p>line one line two line three</p>"));
        }

        [Test]
        public void RenderDocument_UsesExplicitLineBreaksInsideCodeBlocks()
        {
            var html = RenderDocument(
                "```text\r\n" +
                "first file\r\n" +
                "second file\r\n" +
                "third file\r\n" +
                "```");

            Assert.That(html, Does.Contain("<pre tabindex=\"-1\"><code tabindex=\"-1\" class=\"language-text\">first file<br>second file<br>third file</code></pre>"));
            Assert.That(html, Does.Not.Contain(">first file\nsecond file\nthird file</code>"));
        }

        [Test]
        public void RenderDocument_RendersInlineEmphasisAndStrongText()
        {
            var html = RenderDocument("This is *important* and **critical**. Also _soft_ and __loud__.");

            Assert.That(html, Does.Contain("<em>important</em>"));
            Assert.That(html, Does.Contain("<strong>critical</strong>"));
            Assert.That(html, Does.Contain("<em>soft</em>"));
            Assert.That(html, Does.Contain("<strong>loud</strong>"));
            Assert.That(html, Does.Not.Contain("**critical**"));
            Assert.That(html, Does.Not.Contain("*important*"));
        }

        [Test]
        public void RenderDocument_RendersCommonHeadingAndListVariants()
        {
            var html = RenderDocument(
                "Top Title\r\n" +
                "=========\r\n\r\n" +
                "Section Title\r\n" +
                "-------------\r\n\r\n" +
                "##中文标题\r\n\r\n" +
                "1. First\r\n" +
                "2) Second\r\n\r\n" +
                "---\r\n");

            Assert.That(html, Does.Contain("<h1>Top Title</h1>"));
            Assert.That(html, Does.Contain("<h2>Section Title</h2>"));
            Assert.That(html, Does.Contain("<h2>中文标题</h2>"));
            Assert.That(html, Does.Contain("<ol>"));
            Assert.That(html, Does.Contain("<li>First</li>"));
            Assert.That(html, Does.Contain("<li>Second</li>"));
            Assert.That(html, Does.Contain("</ol>"));
            Assert.That(html, Does.Contain("<hr>"));
        }

        private static string RenderDocument(string markdown)
        {
            var assembly = typeof(MyCodex.WinForms.Forms.StructuredMessageViewerForm).Assembly;
            var type = assembly.GetType("MyCodex.WinForms.Forms.MessageMarkdownHtmlRenderer", false);
            Assert.That(type, Is.Not.Null, "MessageMarkdownHtmlRenderer should exist.");

            var method = type.GetMethod(
                "RenderDocument",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            Assert.That(method, Is.Not.Null, "RenderDocument should exist.");

            return (string)method.Invoke(null, new object[] { markdown });
        }
    }
}
