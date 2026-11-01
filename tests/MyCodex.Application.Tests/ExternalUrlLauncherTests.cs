using System;
using System.Collections.Generic;
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
        [TestCase("http://[::1]:39100/callback")]
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

        [TestCase("https://example.com/a", "https://example.com/a")]
        [TestCase("http://example.com/a", "http://example.com/a")]
        [TestCase("mailto:someone@example.com", "mailto:someone@example.com")]
        public void OpenMessageLink_StartsAllowedLinks(string url, string expected)
        {
            var started = new List<string>();
            var errors = new List<string>();

            ExternalUrlLauncher.OpenMessageLink(url, started.Add, errors.Add);

            Assert.That(started, Is.EqualTo(new[] { expected }));
            Assert.That(errors, Is.Empty);
        }

        [TestCase("javascript:alert(1)")]
        [TestCase("file:///C:/Windows/System32/calc.exe")]
        [TestCase(@"C:\Windows\System32\calc.exe")]
        [TestCase("calc.exe")]
        [TestCase(null)]
        public void OpenMessageLink_ReportsDisallowedLinksWithoutStarting(string url)
        {
            var started = new List<string>();
            var errors = new List<string>();

            ExternalUrlLauncher.OpenMessageLink(url, started.Add, errors.Add);

            Assert.That(started, Is.Empty);
            Assert.That(errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void OpenMessageLink_ReportsStartFailure()
        {
            var errors = new List<string>();

            ExternalUrlLauncher.OpenMessageLink("https://example.com/", link => { throw new InvalidOperationException("no browser"); }, errors.Add);

            Assert.That(errors, Is.EqualTo(new[] { "no browser" }));
        }
    }
}
