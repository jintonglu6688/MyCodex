using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MessageDetailPreviewModeTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("unknown")]
        public void Normalize_DefaultsUnknownValuesToWeb(string value)
        {
            Assert.That(MessageDetailPreviewMode.Normalize(value), Is.EqualTo(MessageDetailPreviewMode.Web));
        }

        [TestCase("web", MessageDetailPreviewMode.Web)]
        [TestCase("WEB", MessageDetailPreviewMode.Web)]
        [TestCase(" text ", MessageDetailPreviewMode.Text)]
        public void Normalize_AcceptsSupportedValues(string value, string expected)
        {
            Assert.That(MessageDetailPreviewMode.Normalize(value), Is.EqualTo(expected));
        }
    }
}
