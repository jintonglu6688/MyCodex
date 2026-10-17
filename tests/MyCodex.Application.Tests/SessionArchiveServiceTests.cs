using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionArchiveServiceTests
    {
        [TestCase("windows")]
        [TestCase("wsl:Ubuntu")]
        [TestCase("remote-mac:test")]
        [TestCase("remote-unix:test")]
        public async Task Archive_CascadesOnceAndKeepsUnselectedFork(string runtime)
        {
            var sessions = new[] { Session("grandchild", "child"), Session("root"), Session("child", "root"), Session("root") };
            foreach (var session in sessions) session.RuntimeProfileId = runtime;
            var client = new Client();
            var result = await SessionArchiveService.ArchiveAsync(client, sessions, CancellationToken.None);
            Assert.That(client.Calls, Is.EqualTo(new[] { "root" }));
            Assert.That(result.ArchivedThreadIds, Is.EquivalentTo(new[] { "root", "child", "grandchild" }));
            Assert.That(result.ArchivedThreadIds, Does.Not.Contain("fork"));
            Assert.That(result.Failures, Is.Empty);
        }

        [Test]
        public async Task Archive_PartialCascadeRetainsUnconfirmedChildrenAndContinuesOtherRoots()
        {
            var client = new Client { Cascade = false };
            var result = await SessionArchiveService.ArchiveAsync(client,
                new[] { Session("root"), Session("child", "root"), Session("other") }, CancellationToken.None);
            Assert.That(client.Calls, Is.EqualTo(new[] { "root", "other" }));
            Assert.That(result.ArchivedThreadIds, Is.EquivalentTo(new[] { "root", "other" }));
            Assert.That(result.Failures.Keys, Is.EquivalentTo(new[] { "child" }));
        }

        [Test]
        public async Task Archive_FailedRootPreservesErrorAndContinuesOtherRoots()
        {
            var client = new Client { FailedThreadId = "root" };
            var result = await SessionArchiveService.ArchiveAsync(client,
                new[] { Session("root"), Session("child", "root"), Session("other") }, CancellationToken.None);
            Assert.That(client.Calls, Is.EqualTo(new[] { "root", "other" }));
            Assert.That(result.ArchivedThreadIds, Is.EquivalentTo(new[] { "other" }));
            Assert.That(result.Failures.Keys, Is.EquivalentTo(new[] { "root", "child" }));
            Assert.That(result.Failures["root"], Is.EqualTo("archive failed"));
        }

        [Test]
        public async Task Archive_FailedRefreshDoesNotAssumeDescendantsSucceeded()
        {
            var client = new Client { FailFinalRefresh = true };
            var result = await SessionArchiveService.ArchiveAsync(client,
                new[] { Session("root"), Session("child", "root") }, CancellationToken.None);
            Assert.That(result.ArchivedThreadIds, Is.EquivalentTo(new[] { "root" }));
            Assert.That(result.Failures.Keys, Is.EquivalentTo(new[] { "child" }));
        }

        [Test]
        public async Task Archive_AlreadyArchivedSelectionDoesNotSendDuplicateRequest()
        {
            var client = new Client();
            client.Archived.Add("root");
            var result = await SessionArchiveService.ArchiveAsync(client, new[] { Session("root") }, CancellationToken.None);
            Assert.That(client.Calls, Is.Empty);
            Assert.That(result.ArchivedThreadIds, Does.Contain("root"));
        }

        [Test]
        public void Archive_TargetChangeStopsSubsequentRequests()
        {
            var current = true;
            var client = new Client { AfterArchive = () => current = false };
            Assert.ThrowsAsync<OperationCanceledException>(async () => await SessionArchiveService.ArchiveAsync(client,
                new[] { Session("root"), Session("other") }, CancellationToken.None, () => current));
            Assert.That(client.Calls, Is.EqualTo(new[] { "root" }));
        }

        [Test]
        public void Archive_CycleIsRejectedBeforeMutatingRuntime()
        {
            var client = new Client();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionArchiveService.ArchiveAsync(client,
                new[] { Session("root", "child"), Session("child", "root") }, CancellationToken.None));
            Assert.That(client.Calls, Is.Empty);
        }

        private static ChatSession Session(string id, string parent = null)
        {
            return new ChatSession { ThreadId = id, ParentThreadId = parent };
        }

        private sealed class Client : UnsupportedAppServerThreadClient
        {
            public List<string> Calls { get; } = new List<string>();
            public HashSet<string> Archived { get; } = new HashSet<string>();
            public bool Cascade { get; set; } = true;
            public bool FailFinalRefresh { get; set; }
            public string FailedThreadId { get; set; }
            public Action AfterArchive { get; set; }
            private int _reads;

            public override Task ArchiveThreadAsync(string threadId)
            {
                Calls.Add(threadId);
                if (threadId == FailedThreadId) throw new InvalidOperationException("archive failed");
                if (!Archived.Add(threadId)) throw new InvalidOperationException("no rollout found for thread id " + threadId);
                if (threadId == "root" && Cascade) Archived.UnionWith(new[] { "child", "grandchild" });
                AfterArchive?.Invoke();
                return Task.CompletedTask;
            }

            public override Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
            {
                if (++_reads > 1 && FailFinalRefresh) throw new InvalidOperationException("disconnected");
                return Task.FromResult(Archived.Select(id => new AppServerThreadSummary { ThreadId = id }).ToList());
            }
        }
    }
}
