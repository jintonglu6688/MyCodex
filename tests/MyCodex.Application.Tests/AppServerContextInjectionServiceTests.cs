using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerContextInjectionServiceTests
    {
        [Test]
        public async Task TryInjectAsync_ForAllowedSourceBuildsSystemMessageInputTextItem()
        {
            var client = new FakeThreadClient();
            var service = new AppServerContextInjectionService(client);

            var result = await service.TryInjectAsync(new AppServerContextInjectionRequest
            {
                ThreadId = "thread-1",
                Source = "office-summary",
                Text = "Summary context",
                Required = false
            }).ConfigureAwait(false);

            Assert.That(result.Injected, Is.True);
            Assert.That(result.ShouldFallbackToUserMessage, Is.False);
            Assert.That(client.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.Items, Has.Count.EqualTo(1));
            Assert.That(client.Items[0]["type"], Is.EqualTo("message"));
            Assert.That(client.Items[0]["role"], Is.EqualTo("system"));
            var content = (object[])client.Items[0]["content"];
            var text = (IDictionary<string, object>)content[0];
            Assert.That(text["type"], Is.EqualTo("input_text"));
            Assert.That(text["text"], Is.EqualTo("Summary context"));
        }

        [Test]
        public async Task TryInjectAsync_ForRequiredFailureRequestsUserMessageFallback()
        {
            var client = new FakeThreadClient
            {
                InjectException = new InvalidOperationException("unsupported")
            };
            var service = new AppServerContextInjectionService(client);

            var result = await service.TryInjectAsync(new AppServerContextInjectionRequest
            {
                ThreadId = "thread-1",
                Source = "migration-summary",
                Text = "Required context",
                Required = true
            }).ConfigureAwait(false);

            Assert.That(result.Injected, Is.False);
            Assert.That(result.ShouldFallbackToUserMessage, Is.True);
            Assert.That(result.ErrorMessage, Is.EqualTo("unsupported"));
        }

        [Test]
        public async Task TryInjectAsync_ForOptionalFailureDoesNotRequestFallback()
        {
            var client = new FakeThreadClient
            {
                InjectException = new InvalidOperationException("unsupported")
            };
            var service = new AppServerContextInjectionService(client);

            var result = await service.TryInjectAsync(new AppServerContextInjectionRequest
            {
                ThreadId = "thread-1",
                Source = "tool-context",
                Text = "Optional context",
                Required = false
            }).ConfigureAwait(false);

            Assert.That(result.Injected, Is.False);
            Assert.That(result.ShouldFallbackToUserMessage, Is.False);
        }

        [Test]
        public async Task TryInjectAsync_ForRequiredNonSuccessResultRequestsUserMessageFallback()
        {
            var client = new FakeThreadClient
            {
                Succeeded = false
            };
            var service = new AppServerContextInjectionService(client);

            var result = await service.TryInjectAsync(new AppServerContextInjectionRequest
            {
                ThreadId = "thread-1",
                Source = "session-import-summary",
                Text = "Required context",
                Required = true
            }).ConfigureAwait(false);

            Assert.That(result.Injected, Is.False);
            Assert.That(result.ShouldFallbackToUserMessage, Is.True);
        }

        [Test]
        public void TryInjectAsync_ForUnknownSourceThrows()
        {
            var service = new AppServerContextInjectionService(new FakeThreadClient());

            Assert.ThrowsAsync<ArgumentException>(() => service.TryInjectAsync(new AppServerContextInjectionRequest
            {
                ThreadId = "thread-1",
                Source = "unknown-source",
                Text = "Context"
            }));
        }

        private sealed class FakeThreadClient : UnsupportedAppServerThreadClient
        {
            public string ThreadId { get; private set; }

            public List<IDictionary<string, object>> Items { get; } = new List<IDictionary<string, object>>();

            public Exception InjectException { get; set; }

            public bool Succeeded { get; set; } = true;

            public override Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken))
            {
                if (InjectException != null)
                {
                    throw InjectException;
                }

                ThreadId = threadId;
                Items.AddRange(items);
                return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = Succeeded });
            }

            public override Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }
        }
    }
}
