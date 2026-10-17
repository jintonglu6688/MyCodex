using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ExternalUrlLauncherTests
    {
        [TestCase("https://example.com/login")]
        [TestCase("http://localhost:39100/callback")]
        [TestCase("http://127.0.0.1:39100/callback")]
        public void TryNormalizeAllowedUrl_AllowsHttpsAndLocalhostHttp(string url)
        {
            string normalizedUrl;

            var allowed = ExternalUrlLauncher.TryNormalizeAllowedUrl(url, out normalizedUrl);

            Assert.That(allowed, Is.True);
            Assert.That(normalizedUrl, Is.Not.Empty);
        }

        [TestCase("http://example.com/login")]
        [TestCase("file:///C:/Windows/System32/calc.exe")]
        [TestCase("cmd.exe")]
        [TestCase("myapp://callback")]
        [TestCase(@"C:\Windows\System32\calc.exe")]
        public void TryNormalizeAllowedUrl_RejectsNonBrowserOrNonLocalHttpSchemes(string url)
        {
            string normalizedUrl;

            var allowed = ExternalUrlLauncher.TryNormalizeAllowedUrl(url, out normalizedUrl);

            Assert.That(allowed, Is.False);
            Assert.That(normalizedUrl, Is.Null);
        }
    }
}
