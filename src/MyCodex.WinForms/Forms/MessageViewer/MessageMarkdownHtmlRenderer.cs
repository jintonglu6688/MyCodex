using System;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal static class MessageMarkdownHtmlRenderer
    {
        private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UsePipeTables()
            .UseGridTables()
            .UseAutoLinks()
            .UseTaskLists()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();

        private static readonly Regex LooseAtxHeadingRegex = new Regex(
            @"^(?<indent>\s{0,3})(?<level>#{1,6})(?<text>[^\s#].*)$",
            RegexOptions.Compiled);
        private static readonly Regex FenceBoundaryRegex = new Regex(@"^\s{0,3}(```+|~~~+)", RegexOptions.Compiled);
        private static readonly Regex RenderedCodeBlockRegex = new Regex(
            @"<pre><code(?<attributes>[^>]*)>(?<code>[\s\S]*?)</code></pre>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LanguageClassRegex = new Regex(
            @"\bclass\s*=\s*""[^""]*\blanguage-(?<language>[^""\s]+)[^""]*""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TableHeaderCellRegex = new Regex(
            @"<th(?=\s|>)(?<attributes>[^>]*)>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string RenderDocument(string markdown)
        {
            return RenderDocument(markdown, "Message content");
        }

        public static string RenderDocument(string markdown, string accessibleName)
        {
            var safeAccessibleName = HtmlEncode(string.IsNullOrWhiteSpace(accessibleName) ? "Message content" : accessibleName);
            var builder = new StringBuilder();
            builder.AppendLine("<!doctype html>");
            builder.AppendLine("<html lang=\"\">");
            builder.AppendLine("<head>");
            builder.AppendLine("<meta charset=\"utf-8\">");
            builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            builder.AppendLine("<title>&#8203;</title>");
            builder.AppendLine("<style>");
            builder.Append(BuildStyle());
            builder.AppendLine("</style>");
            builder.AppendLine("</head>");
            builder.AppendLine("<body>");
            builder.AppendLine("<div id=\"content\" tabindex=\"-1\">");
            builder.Append(RenderBody(markdown));
            builder.AppendLine("</div>");
            builder.AppendLine("<script>");
            builder.Append(BuildScript());
            builder.AppendLine("</script>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");
            return builder.ToString();
        }

        private static string RenderBody(string markdown)
        {
            var normalized = Normalize(markdown);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "<p>No displayable content in the current message.</p>" + Environment.NewLine;
            }

            var renderedHtml = Markdown.ToHtml(NormalizeLooseMarkdown(normalized), MarkdownPipeline);
            return NormalizeRenderedHtml(renderedHtml);
        }

        private static void AppendCodeBlockLines(StringBuilder builder, string codeText)
        {
            var lines = Normalize(codeText).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append("<br>");
                }

                builder.Append(HtmlEncode(lines[i] ?? string.Empty));
            }
        }

        private static string NormalizeLooseMarkdown(string markdown)
        {
            var lines = Normalize(markdown).Split('\n');
            var builder = new StringBuilder();
            var inFence = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                var line = lines[i] ?? string.Empty;
                if (FenceBoundaryRegex.IsMatch(line))
                {
                    inFence = !inFence;
                    builder.Append(line);
                    continue;
                }

                var heading = inFence ? Match.Empty : LooseAtxHeadingRegex.Match(line);
                if (heading.Success && ShouldNormalizeLooseHeading(heading.Groups["level"].Value, heading.Groups["text"].Value))
                {
                    builder.Append(heading.Groups["indent"].Value);
                    builder.Append(heading.Groups["level"].Value);
                    builder.Append(' ');
                    builder.Append(heading.Groups["text"].Value.TrimStart());
                    continue;
                }

                builder.Append(line);
            }

            return builder.ToString();
        }

        private static bool ShouldNormalizeLooseHeading(string level, string text)
        {
            if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(text))
            {
                return false;
            }

            var first = text[0];
            if (level.Length == 1 && char.IsDigit(first))
            {
                return false;
            }

            return char.IsLetter(first) || first > 127 || first == '(' || first == '[' || first == '\u300a';
        }

        private static string NormalizeRenderedHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var normalized = html
                .Replace("<br />\r\n", "<br>")
                .Replace("<br />\n", "<br>")
                .Replace("<br />", "<br>")
                .Replace("<hr />", "<hr>");
            normalized = AddTableHeaderScopes(normalized);
            normalized = AddCodeCopyButtons(normalized);
            return normalized;
        }

        private static string AddTableHeaderScopes(string html)
        {
            return TableHeaderCellRegex.Replace(html, match =>
            {
                var attributes = match.Groups["attributes"].Value ?? string.Empty;
                return attributes.IndexOf("scope=", StringComparison.OrdinalIgnoreCase) >= 0
                    ? match.Value
                    : "<th scope=\"col\"" + attributes + ">";
            });
        }

        private static string AddCodeCopyButtons(string html)
        {
            return RenderedCodeBlockRegex.Replace(html, match =>
            {
                var attributes = match.Groups["attributes"].Value ?? string.Empty;
                var codeHtml = match.Groups["code"].Value ?? string.Empty;
                var codeText = WebUtility.HtmlDecode(codeHtml).TrimEnd('\r', '\n');
                var language = ExtractLanguage(attributes);
                var builder = new StringBuilder();
                builder.Append("<button type=\"button\" class=\"copy-code-button\" data-code=\"");
                builder.Append(HtmlAttributeEncode(codeText));
                builder.Append("\">");
                builder.Append(HtmlEncode(UiLocalization.Text("WebMessageViewerForm.Button.CopyCode", "Copy")));
                builder.AppendLine("</button>");

                builder.Append("<pre tabindex=\"-1\"><code tabindex=\"-1\"");
                if (!string.IsNullOrWhiteSpace(language))
                {
                    builder.Append(" class=\"language-");
                    builder.Append(HtmlAttributeEncode(language));
                    builder.Append("\"");
                }

                builder.Append(">");
                AppendCodeBlockLines(builder, codeText);
                builder.AppendLine("</code></pre>");
                return builder.ToString();
            });
        }

        private static string ExtractLanguage(string attributes)
        {
            var match = LanguageClassRegex.Match(attributes ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            return SanitizeCssClassSuffix(WebUtility.HtmlDecode(match.Groups["language"].Value));
        }

        private static string BuildStyle()
        {
            var bodyBack = ToCssColor(UiTheme.CardBackColor);
            var text = ToCssColor(UiTheme.TextColor);
            var muted = ToCssColor(UiTheme.MutedTextColor);
            var codeBack = ToCssColor(UiTheme.CurrentMode == UiThemeMode.Dark ? UiTheme.SubtleBackColor : Color.FromArgb(243, 245, 248));
            var link = UiTheme.CurrentMode == UiThemeMode.Dark ? "#93c5fd" : "#0b63ce";

            return string.Format(
                CultureInfo.InvariantCulture,
                "html,body{{margin:0;padding:0;background:{0};color:{1};font:14px/1.65 \"Microsoft YaHei UI\",\"Segoe UI\",sans-serif;}}" +
                "body{{padding:20px;box-sizing:border-box;}}" +
                "#content{{outline:none;max-width:none;}}" +
                "h1,h2,h3,h4,h5,h6{{margin:0 0 0.65em;font-weight:700;line-height:1.3;}}" +
                "h1{{font-size:1.55rem;}}h2{{font-size:1.35rem;}}h3{{font-size:1.18rem;}}h4,h5,h6{{font-size:1rem;}}" +
                "p{{margin:0 0 0.8em;}}" +
                "ul,ol{{margin:0 0 0.9em 1.4em;padding:0;}}" +
                "li{{margin:0.2em 0;}}" +
                "blockquote{{margin:0 0 0.9em;padding:0.2em 0 0.2em 1em;border-left:4px solid {2};color:{2};}}" +
                "hr{{border:0;border-top:1px solid {2};margin:1em 0;}}" +
                "table{{border-collapse:collapse;margin:0 0 1em;width:100%;}}" +
                "th,td{{border:1px solid {2};padding:6px 8px;text-align:left;vertical-align:top;}}" +
                "th{{font-weight:700;background:{3};}}" +
                ".copy-code-button{{display:inline-block;margin:0 0 0.45em;padding:4px 10px;border:1px solid {2};border-radius:4px;background:{0};color:{1};font:13px \"Microsoft YaHei UI\",\"Segoe UI\",sans-serif;cursor:pointer;}}" +
                ".copy-code-button:focus{{outline:2px solid {4};outline-offset:2px;}}" +
                "pre{{margin:0 0 1em;padding:12px;overflow:auto;background:{3};border-radius:6px;}}" +
                "code{{font-family:Consolas,\"Cascadia Mono\",monospace;background:{3};padding:0.08em 0.25em;border-radius:4px;}}" +
                "pre code{{display:block;padding:0;background:transparent;white-space:pre;}}" +
                "a{{color:{4};}}",
                bodyBack,
                text,
                muted,
                codeBack,
                link);
        }

        private static string BuildScript()
        {
            return "document.addEventListener('click',function(event){" +
                   "var target=event.target;" +
                   "if(!target||!target.classList||!target.classList.contains('copy-code-button')){return;}" +
                   "if(!window.chrome||!window.chrome.webview){return;}" +
                   "window.chrome.webview.postMessage(JSON.stringify({kind:'copyCode',text:target.getAttribute('data-code')||''}));" +
                   "});" +
                   "document.addEventListener('keydown',function(event){" +
                   "if(event.key!=='Escape'){return;}" +
                   "if(!window.chrome||!window.chrome.webview){return;}" +
                   "event.preventDefault();" +
                   "window.chrome.webview.postMessage(JSON.stringify({kind:'close'}));" +
                   "});";
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string SanitizeCssClassSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '+')
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string ToCssColor(Color color)
        {
            return ColorTranslator.ToHtml(color);
        }

        private static string HtmlEncode(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }

        private static string HtmlAttributeEncode(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }
    }
}
