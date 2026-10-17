using System.Reflection;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WebMessageViewerNavigationPolicyTests
    {
        [Test]
        public void ShouldAllowNavigation_AllowsInitialDataDocumentAndBlocksLaterDataNavigation()
        {
            Assert.That(ShouldAllowNavigation("data:text/html;charset=utf-8,%3Cmain%3Econtent%3C/main%3E", true), Is.True);
            Assert.That(ShouldAllowNavigation("data:text/html;charset=utf-8,%3Cmain%3Econtent%3C/main%3E", false), Is.False);
        }

        [Test]
        public void ShouldAllowNavigation_AllowsAboutBlankButDoesNotAllowExternalLinksInsideWebView()
        {
            Assert.That(ShouldAllowNavigation("about:blank", false), Is.True);
            Assert.That(ShouldAllowNavigation("https://example.com/docs", false), Is.False);
        }

        private static bool ShouldAllowNavigation(string uri, bool isLoadingLocalDocument)
        {
            var type = typeof(MyCodex.WinForms.Forms.WebMessageViewerForm);
            var method = type.GetMethod("ShouldAllowNavigation", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ShouldAllowNavigation should exist.");
            return (bool)method.Invoke(null, new object[] { uri, isLoadingLocalDocument });
        }
    }
}
