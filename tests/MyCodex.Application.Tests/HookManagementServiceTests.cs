using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class HookManagementServiceTests
    {
        [Test]
        public void BuildTrust_UpsertsOnlyTheReviewedHashUnderTheVerbatimKey()
        {
            var hook = Hook(@"C:\Users\me\.codex\hooks.json:pre_tool_use:0:0", "sha256:abc/sha256:content");

            var edit = HookStateEditPlanner.BuildTrust(new[] { hook }).Single();

            Assert.That(edit.KeyPath, Is.EqualTo("hooks.state"));
            Assert.That(edit.MergeStrategy, Is.EqualTo("upsert"));
            var state = (IDictionary<string, object>)((IDictionary<string, object>)edit.Value)[hook.Key];
            Assert.That(state, Is.EquivalentTo(new Dictionary<string, object> { { "trusted_hash", "sha256:abc/sha256:content" } }));
        }

        [Test]
        public void BuildRevoke_DeletesBothHashesThroughAnEscapedQuotedSegment()
        {
            var edits = HookStateEditPlanner.BuildRevoke(Hook("a.b\"c\\d:stop:0:0", "h"));

            Assert.That(edits.Select(edit => edit.KeyPath), Is.EqualTo(new[]
            {
                "hooks.state.\"a.b\\\"c\\\\d:stop:0:0\".trusted_hash",
                "hooks.state.\"a.b\\\"c\\\\d:stop:0:0\".trusted_content_hash"
            }));
            Assert.That(edits.All(edit => edit.Value == null && edit.MergeStrategy == "replace"), Is.True);
        }

        [Test]
        public void Planner_NeverWritesManagedHooksAndTreatsUnknownTrustAsNeedingReview()
        {
            var managed = Hook("m", "h");
            managed.IsManaged = true;
            managed.TrustStatus = "managed";

            Assert.That(HookStateEditPlanner.BuildTrust(new[] { managed }), Is.Empty);
            Assert.That(HookStateEditPlanner.BuildEnabled(managed, false), Is.Empty);
            Assert.That(HookStateEditPlanner.BuildRevoke(managed), Is.Empty);
            Assert.That(HookStateEditPlanner.NeedsReview(new AppServerHookInfo { Key = "k", TrustStatus = "futureStatus" }), Is.True);
        }

        [Test]
        public async Task ListAsync_FallsBackToWorkspaceViewWhenRuntimeAnswersThreadRequestForAnotherCwd()
        {
            var hooks = new FakeHookClient();
            hooks.ThreadResult = new AppServerHooksListResult { Cwd = @"C:\runtime-home" };
            hooks.WorkspaceResult = new AppServerHooksListResult { Cwd = @"E:\Repo" };

            var result = await new HookManagementService(hooks, new FakeConfigClient()).ListAsync(@"E:\Repo", "thread-1");

            Assert.That(result, Is.SameAs(hooks.WorkspaceResult));
            Assert.That(result.IsThreadView, Is.False);
        }

        [Test]
        public async Task ListAsync_UsesThreadViewWhenItsCwdMatchesTheWorkspace()
        {
            var hooks = new FakeHookClient();
            hooks.ThreadResult = new AppServerHooksListResult { Cwd = @"E:\Repo\" };

            var result = await new HookManagementService(hooks, new FakeConfigClient()).ListAsync(@"E:\Repo", "thread-1");

            Assert.That(result.IsThreadView, Is.True);
            Assert.That(hooks.WorkspaceCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task TrustAsync_RefusesToWriteWhenTheDefinitionChangedAfterReview()
        {
            var hooks = new FakeHookClient();
            hooks.WorkspaceResult = List(Hook("k", "new-hash"));

            var result = await new HookManagementService(hooks, new FakeConfigClient())
                .TrustAsync(@"E:\Repo", null, new[] { Hook("k", "reviewed-hash") });

            Assert.That(result.Status, Is.EqualTo(HookStateChangeStatus.Changed));
            Assert.That(hooks.Writes, Is.Empty);
        }

        [Test]
        public async Task TrustAsync_WritesWithUserLayerVersionAndReportsOverride()
        {
            var hooks = new FakeHookClient();
            hooks.WorkspaceResult = List(Hook("k", "h"));
            hooks.WriteResult = new AppServerConfigWriteResult { Status = "okOverridden", Version = "v2" };
            var config = new FakeConfigClient();
            config.Snapshot.Layers.Add(new AppServerConfigLayer { SourceType = "user", Version = "user-v1" });

            var result = await new HookManagementService(hooks, config).TrustAsync(@"E:\Repo", null, new[] { Hook("k", "h") });

            Assert.That(result.Status, Is.EqualTo(HookStateChangeStatus.Saved));
            Assert.That(result.Overridden, Is.True);
            Assert.That(hooks.Writes.Single().Item2, Is.EqualTo("user-v1"));
        }

        [Test]
        public async Task SetEnabledAsync_DoesNotWriteWithoutAnObservedUserLayerVersion()
        {
            var hooks = new FakeHookClient();
            hooks.WorkspaceResult = List(Hook("k", "h"));

            var result = await new HookManagementService(hooks, new FakeConfigClient()).SetEnabledAsync(@"E:\Repo", null, Hook("k", "h"), false);

            Assert.That(result.Status, Is.EqualTo(HookStateChangeStatus.VersionUnavailable));
            Assert.That(hooks.Writes, Is.Empty);
        }

        [Test]
        public void PromptState_PendingFollowsOfficialRuleIncludingDisabledButNotManagedOrTrusted()
        {
            var disabled = Hook("disabled", "h");
            disabled.Enabled = false;
            var trusted = Hook("trusted", "h");
            trusted.TrustStatus = "trusted";
            var managed = Hook("managed", "h");
            managed.TrustStatus = "managed";
            managed.IsManaged = true;

            var pending = HookReviewPromptState.GetPending(List(disabled, trusted, managed, Hook("changed", "h")));

            Assert.That(pending.Select(hook => hook.Key), Is.EqualTo(new[] { "disabled", "changed" }));
        }

        [Test]
        public void PromptState_ShowsOncePerExactHashSetAndAgainWhenItChanges()
        {
            var state = new HookReviewPromptState();
            var first = HookReviewPromptState.GetSignature(new[] { Hook("a", "1"), Hook("b", "1") });
            var reordered = HookReviewPromptState.GetSignature(new[] { Hook("b", "1"), Hook("a", "1") });

            Assert.That(state.Decide(first, true), Is.EqualTo(HookReviewPromptDecision.Show));
            Assert.That(state.Decide(reordered, true), Is.EqualTo(HookReviewPromptDecision.None));
            Assert.That(state.Decide(HookReviewPromptState.GetSignature(new[] { Hook("a", "2") }), true), Is.EqualTo(HookReviewPromptDecision.Show));
            Assert.That(state.Decide(string.Empty, true), Is.EqualTo(HookReviewPromptDecision.None));
            Assert.That(state.Decide(first, true), Is.EqualTo(HookReviewPromptDecision.Show));
        }

        [Test]
        public void PromptState_AnnouncesOnceInBackgroundThenShowsWhenTheWindowCanTakeTheDialog()
        {
            var state = new HookReviewPromptState();
            var signature = HookReviewPromptState.GetSignature(new[] { Hook("a", "1") });

            Assert.That(state.Decide(signature, false), Is.EqualTo(HookReviewPromptDecision.Announce));
            Assert.That(state.Decide(signature, false), Is.EqualTo(HookReviewPromptDecision.None));
            Assert.That(state.HasDeferredPrompt, Is.True);
            Assert.That(state.Decide(signature, true), Is.EqualTo(HookReviewPromptDecision.Show));
            Assert.That(state.HasDeferredPrompt, Is.False);
        }

        [Test]
        public void PromptState_EntersEachNewWorkspaceOrSessionOnce()
        {
            var state = new HookReviewPromptState();

            Assert.That(state.EnterContext(@"E:\Repo", null), Is.True);
            Assert.That(state.EnterContext(@"E:\Repo", null), Is.False);
            Assert.That(state.EnterContext(@"E:\Repo", "thread-1"), Is.True);
            Assert.That(state.EnterContext(@"E:\Other", "thread-1"), Is.True);
        }

        [Test]
        public void PromptState_ChecksEachCurrentThreadViewOnceUntilReconnect()
        {
            var state = new HookReviewPromptState();

            Assert.That(state.NeedsThreadViewCheck("subagent", "thread-1"), Is.False);
            Assert.That(state.NeedsThreadViewCheck("thread-1", "thread-1"), Is.True);
            state.MarkThreadViewChecked("thread-1");
            Assert.That(state.NeedsThreadViewCheck("thread-1", "thread-1"), Is.False);
            state.ResetThreadViews();
            Assert.That(state.NeedsThreadViewCheck("thread-1", "thread-1"), Is.True);
        }

        private static AppServerHookInfo Hook(string key, string hash)
        {
            return new AppServerHookInfo { Key = key, CurrentHash = hash, TrustStatus = "untrusted", Enabled = true };
        }

        private static AppServerHooksListResult List(params AppServerHookInfo[] hooks)
        {
            var list = new AppServerHooksListResult { Cwd = @"E:\Repo" };
            list.Hooks.AddRange(hooks);
            return list;
        }

        internal sealed class FakeHookClient : IAppServerHookClient
        {
            public AppServerHooksListResult ThreadResult { get; set; }

            public AppServerHooksListResult WorkspaceResult { get; set; } = new AppServerHooksListResult();

            public AppServerConfigWriteResult WriteResult { get; set; } = new AppServerConfigWriteResult { Status = "ok" };

            public int WorkspaceCalls { get; private set; }

            public List<Tuple<IReadOnlyCollection<AppServerConfigWriteEdit>, string>> Writes { get; } =
                new List<Tuple<IReadOnlyCollection<AppServerConfigWriteEdit>, string>>();

            public Task<AppServerHooksListResult> ListHooksAsync(string workspacePath, string threadId)
            {
                if (threadId != null)
                {
                    return Task.FromResult(ThreadResult);
                }

                WorkspaceCalls++;
                return Task.FromResult(WorkspaceResult);
            }

            public Task<AppServerConfigWriteResult> WriteHookStateAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string expectedVersion)
            {
                Writes.Add(Tuple.Create(edits, expectedVersion));
                return Task.FromResult(WriteResult);
            }
        }

        internal sealed class FakeConfigClient : IAppServerConfigClient
        {
            public AppServerConfigSnapshot Snapshot { get; } = new AppServerConfigSnapshot();

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
            {
                return Task.FromResult(Snapshot);
            }

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken)
            {
                return Task.FromResult(Snapshot);
            }

            public Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerModelOption>> ListModelsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(
                string workspacePath = null,
                int? limit = null,
                string cursor = null,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotSupportedException();
            }
        }
    }
}
