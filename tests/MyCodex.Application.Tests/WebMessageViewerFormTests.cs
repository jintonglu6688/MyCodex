using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class WebMessageViewerFormTests
    {
        [Test]
        public void Constructor_KeepsWebViewOutOfTabOrderUntilContentLoads()
        {
            using (var form = new WebMessageViewerForm(new ChatHistoryItem { MarkdownText = "# Title" }))
            {
                var webView = GetWebView(form);

                Assert.That(webView.TabStop, Is.False);
            }
        }

        [Test]
        public void ShouldCloseForAcceleratorKey_ReturnsTrueOnlyForEscapeKeyDown()
        {
            Assert.That(ShouldCloseForAcceleratorKey(Keys.Escape, true), Is.True);
            Assert.That(ShouldCloseForAcceleratorKey(Keys.Escape, false), Is.False);
            Assert.That(ShouldCloseForAcceleratorKey(Keys.Enter, true), Is.False);
        }

        [Test]
        public void ShouldFocusContentAfterLoad_ReturnsTrueToResetBrowsePosition()
        {
            Assert.That(ShouldFocusContentAfterLoad(), Is.True);
        }

        [Test]
        public void BuildInitialContentFocusScript_ResetsDocumentPositionBeforeFocusingContent()
        {
            var script = BuildInitialContentFocusScript();

            Assert.That(script, Does.Contain("window.scrollTo(0,0);"));
            Assert.That(script, Does.Contain("selection.removeAllRanges();"));
            Assert.That(script, Does.Contain("content.focus"));
        }

        private static Control GetWebView(WebMessageViewerForm form)
        {
            var field = typeof(WebMessageViewerForm).GetField("_webView", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "_webView should exist.");
            return (Control)field.GetValue(form);
        }

        private static bool ShouldCloseForAcceleratorKey(Keys key, bool isKeyDown)
        {
            var method = typeof(WebMessageViewerForm).GetMethod("ShouldCloseForAcceleratorKey", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ShouldCloseForAcceleratorKey should exist.");
            return (bool)method.Invoke(null, new object[] { (int)key, isKeyDown });
        }

        private static bool ShouldFocusContentAfterLoad()
        {
            var method = typeof(WebMessageViewerForm).GetMethod("ShouldFocusContentAfterLoad", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ShouldFocusContentAfterLoad should exist.");
            return (bool)method.Invoke(null, Array.Empty<object>());
        }

        private static string BuildInitialContentFocusScript()
        {
            var method = typeof(WebMessageViewerForm).GetMethod("BuildInitialContentFocusScript", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "BuildInitialContentFocusScript should exist.");
            return (string)method.Invoke(null, Array.Empty<object>());
        }
    }
}
