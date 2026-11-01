using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class McpElicitationFlowTests
    {
        private readonly List<string> _calls = new List<string>();
        private bool _accept;
        private bool _available;
        private Exception _openFailure;

        [SetUp]
        public void SetUp()
        {
            _calls.Clear();
            _accept = true;
            _available = true;
            _openFailure = null;
        }

        [Test]
        public async Task AcceptedHttpsUrl_OpensThenResolves()
        {
            await RunAsync("https://example.com/login");

            Assert.That(_calls, Is.EqualTo(new[] { "confirm:https://example.com/login", "open:https://example.com/login", "resolve" }));
        }

        [TestCase(@"\\host\share\x.exe")]
        [TestCase(@"C:\Windows\System32\calc.exe")]
        [TestCase("file:///C:/Windows/System32/calc.exe")]
        [TestCase("calc.exe")]
        [TestCase("http://example.com/login")]
        [TestCase("")]
        [TestCase(null)]
        public void DisallowedUrl_IsCanceledWithoutPromptOrOpen(string url)
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(url));

            Assert.That(_calls, Is.EqualTo(new[] { "cancel" }));
        }

        [Test]
        public async Task DeclinedUrl_IsCanceledWithoutOpen()
        {
            _accept = false;

            await RunAsync("https://example.com/login");

            Assert.That(_calls, Is.EqualTo(new[] { "confirm:https://example.com/login", "cancel" }));
        }

        [Test]
        public void OpenFailure_CancelsInsteadOfLeavingTheRequestPending()
        {
            _openFailure = new InvalidOperationException("no browser");

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("https://example.com/login"));

            Assert.That(thrown, Is.SameAs(_openFailure));
            Assert.That(_calls, Is.EqualTo(new[] { "confirm:https://example.com/login", "cancel" }));
        }

        [Test]
        public async Task UnavailableAppServer_DoesNotOpenOrRespond()
        {
            _available = false;

            await RunAsync("https://example.com/login");

            Assert.That(_calls, Is.EqualTo(new[] { "confirm:https://example.com/login" }));
        }

        [TestCase(true, true, "resolve")]
        [TestCase(false, true, "cancel")]
        [TestCase(true, false, null)]
        [TestCase(false, false, null)]
        public async Task Confirmation_AnswersOnlyWhileAppServerIsAvailable(bool accept, bool available, string expected)
        {
            await McpElicitationFlow.RunConfirmationAsync(
                () => accept,
                () => Task.FromResult(available),
                () =>
                {
                    _calls.Add("resolve");
                    return Task.CompletedTask;
                },
                () =>
                {
                    _calls.Add("cancel");
                    return Task.CompletedTask;
                });

            Assert.That(_calls, Is.EqualTo(expected == null ? new string[0] : new[] { expected }));
        }

        private Task RunAsync(string url)
        {
            return McpElicitationFlow.RunUrlAsync(
                url,
                allowedUrl =>
                {
                    _calls.Add("confirm:" + allowedUrl);
                    return _accept;
                },
                () => Task.FromResult(_available),
                allowedUrl =>
                {
                    if (_openFailure != null)
                    {
                        throw _openFailure;
                    }

                    _calls.Add("open:" + allowedUrl);
                },
                () =>
                {
                    _calls.Add("resolve");
                    return Task.CompletedTask;
                },
                () =>
                {
                    _calls.Add("cancel");
                    return Task.CompletedTask;
                });
        }
    }
}
