using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionSendPreparationTests
    {
        [TestCase(@"C:\project")]
        [TestCase("/home/user/project")]
        [TestCase("/Users/user/project")]
        public async Task PreparedFirstSend_DoesNotWaitForMetadataOrResolveSettingsAgain(string path)
        {
            var client = new Client();
            var service = new SessionThreadService(client, new WorkspaceService(), _ => null);
            service.ModelSettingsResolver = (_, __) => throw new AssertionException("Settings were already validated for this send.");
            var workspace = new WorkspaceInfo { RootPath = path };
            var session = new ChatSession { Title = "title" };
            var settings = new SessionDefaultsResolution { ModelId = "model", ReasoningEffort = "high" };
            await service.RecreateThreadAsync(session, workspace, ApprovalMode.Auto, null, null, null, true, settings);
            await service.SendUserMessageAsync(session, workspace,
                new[] { new AppServerUserInputItem { Type = "text", Text = "test" } }, ApprovalMode.Auto, null, null, null, null, settings);
            Assert.That(client.Sent, Is.True);
            Assert.That(client.Names, Is.Empty);
            var synchronization = service.SynchronizeMetadataAsync(session, workspace, "connection", () => true);
            await client.NameEntered.Task;
            Assert.That(synchronization.IsCompleted, Is.False, "Slow metadata is independent of the completed send.");
            client.NameGate.SetResult(true);
            await synchronization;
            Assert.That(client.MetadataCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task BackgroundMetadata_RechecksOwnershipAfterSlowGitRead()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var release = new ManualResetEventSlim())
            {
                var client = new Client();
                var service = new SessionThreadService(client, new WorkspaceService(), _ =>
                {
                    entered.SetResult(true);
                    Assert.That(release.Wait(3000), Is.True);
                    return null;
                });
                var current = true;
                var task = service.SynchronizeMetadataAsync(new ChatSession { ThreadId = "thread", Title = "old" },
                    new WorkspaceInfo { RootPath = "project" }, "connection", () => current);
                try { await entered.Task; current = false; }
                finally { release.Set(); }
                await task;
                Assert.That(client.Names, Is.Empty);
                Assert.That(client.MetadataCalls, Is.Zero);
            }
        }

        [Test]
        public async Task BackgroundMetadata_CoalescesNewestTitleWhilePreviousUpdateRuns()
        {
            var client = new Client();
            var service = new SessionThreadService(client, new WorkspaceService(), _ => null);
            var workspace = new WorkspaceInfo { RootPath = "project" };
            var session = new ChatSession { ThreadId = "thread", Title = "first" };
            var first = service.SynchronizeMetadataAsync(session, workspace, "connection", () => true);
            await client.NameEntered.Task;
            session.Title = "intermediate";
            _ = service.SynchronizeMetadataAsync(session, workspace, "connection", () => true);
            session.Title = "latest";
            var latest = service.SynchronizeMetadataAsync(session, workspace, "connection", () => true);
            client.NameGate.SetResult(true);
            await latest;
            Assert.That(latest, Is.SameAs(first));
            Assert.That(client.Names, Is.EqualTo(new[] { "first", "latest" }));
        }

        private sealed class Client : UnsupportedAppServerThreadClient
        {
            public bool Sent;
            public int MetadataCalls;
            public readonly List<string> Names = new List<string>();
            public readonly TaskCompletionSource<bool> NameEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> NameGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public override Task<string> CreateThreadAsync(string path, ApprovalMode? approval, string model, string effort, AppServerThreadToolContext context = null)
            { Assert.That(model, Is.EqualTo("model")); return Task.FromResult("thread"); }
            public override Task SendUserMessageAsync(string thread, string path, IReadOnlyCollection<AppServerUserInputItem> items, ApprovalMode? approval, string model, string effort, AppServerCollaborationMode mode = null)
            { Assert.That(model, Is.EqualTo("model")); Sent = true; return Task.CompletedTask; }
            public override Task SetThreadNameAsync(string thread, string name)
            { Names.Add(name); NameEntered.TrySetResult(true); return NameGate.Task; }
            public override Task UpdateThreadGitMetadataAsync(string thread, AppServerThreadGitInfoUpdate info)
            { MetadataCalls++; return Task.CompletedTask; }
        }
    }
}
