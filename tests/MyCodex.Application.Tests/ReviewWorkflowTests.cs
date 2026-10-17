using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ReviewWorkflowTests
    {
        [Test, Combinatorial]
        public async Task Review_AllTargetsPreserveDeliveryAndSettings([Values(0, 1, 2, 3)] int kind, [Values(false, true)] bool detached)
        {
            var target = new AppServerReviewTarget
            {
                Kind = (AppServerReviewTargetKind)kind, Branch = "main", Sha = "abcdef1", Instructions = "Check errors"
            };
            var client = new ReviewClient();
            var workflow = Create(client);
            var session = new ChatSession { ThreadId = "source" };
            var workspace = new WorkspaceInfo { RootPath = "/repo" };
            if (detached)
                await workflow.StartDetachedReviewAsync(session, workspace, ApprovalMode.ReadOnly, "model", "high", "review", target);
            else
                await workflow.StartInlineReviewAsync(session, workspace, ApprovalMode.ReadOnly, "model", "high", "review", target);
            Assert.That(client.Target, Is.SameAs(target));
            Assert.That(client.Detached, Is.EqualTo(detached));
            Assert.That(client.Model, Is.EqualTo("model"));
            Assert.That(client.Effort, Is.EqualTo("high"));
            Assert.That(client.ReviewModel, Is.EqualTo("review"));
        }

        [Test]
        public async Task Review_UsesRequestedSettingsWithoutChangingSessionOverrides()
        {
            var client = new ReviewClient();
            var workflow = Create(client);
            var source = new ChatSession { ThreadId = "source" };
            var workspace = new WorkspaceInfo { Id = "workspace", RootPath = "/repo" };
            await workflow.StartInlineReviewAsync(source, workspace, ApprovalMode.ReadOnly, "configured-model", "high", "review-model");
            Assert.That(client.Model, Is.EqualTo("configured-model"));
            Assert.That(client.Effort, Is.EqualTo("high"));
            Assert.That(client.ReviewModel, Is.EqualTo("review-model"));
            Assert.That(source.ModelId, Is.Null);
            Assert.That(source.ReasoningEffort, Is.Null);
            Assert.That(source.ApprovalMode, Is.Null);
        }

        [Test]
        public async Task DetachedResolution_DoesNotPinResolvedDefaults()
        {
            var source = new ChatSession { ThreadId = "source" };
            var result = await Create(new ReviewClient()).ResolveReviewSessionAsync(source,
                new WorkspaceInfo { Id = "workspace", RootPath = "/repo" },
                new AppServerReviewStartResult { ReviewThreadId = "review" }, id => null,
                ApprovalMode.FullAccess, "inferred-model", "high");
            Assert.That(result.IsNewSession, Is.True);
            Assert.That(result.Session.ModelId, Is.Null);
            Assert.That(result.Session.ReasoningEffort, Is.Null);
            Assert.That(result.Session.ApprovalMode, Is.Null);
            Assert.That(result.Session.ReviewSourceThreadId, Is.EqualTo("source"));
        }

        [Test]
        public async Task DetachedResolution_ImportDuringReadReusesExistingSession()
        {
            var pendingRead = new TaskCompletionSource<AppServerThreadDetails>();
            var client = new ReviewClient { Read = pendingRead.Task };
            ChatSession existing = null;
            var source = new ChatSession { ThreadId = "source", ModelId = "explicit", ReasoningEffort = "low" };
            var operation = Create(client).ResolveReviewSessionAsync(source,
                new WorkspaceInfo { Id = "workspace", RootPath = "/repo" },
                new AppServerReviewStartResult { ReviewThreadId = "review" }, id => existing,
                null, "inferred", "high");
            existing = new ChatSession { ThreadId = "review" };
            pendingRead.SetResult(new AppServerThreadDetails { ThreadId = "review" });
            var result = await operation;
            Assert.That(result.IsNewSession, Is.False);
            Assert.That(result.Session, Is.SameAs(existing));
            Assert.That(result.Session.ModelId, Is.EqualTo("explicit"));
            Assert.That(result.Session.ReasoningEffort, Is.EqualTo("low"));
        }

        [Test]
        public async Task CreatedEmptyReviewSession_IsRecoverableWithoutReadingRollout()
        {
            var client = new ReviewClient { Read = Task.FromException<AppServerThreadDetails>(new InvalidOperationException("no rollout")) };
            var result = await Create(client).ResolveReviewSessionAsync(new ChatSession { ThreadId = "source", ApprovalMode = ApprovalMode.Custom },
                new WorkspaceInfo { Id = "workspace", RootPath = "/repo" },
                new AppServerReviewStartResult
                {
                    ReviewThreadId = "created",
                    CreatedThread = new AppServerThreadSummary { ThreadId = "created", WorkingDirectory = "/repo" }
                }, id => null, null, null, null);
            Assert.That(result.IsNewSession, Is.True);
            Assert.That(result.Session.ThreadId, Is.EqualTo("created"));
            Assert.That(result.Session.ReviewSourceThreadId, Is.EqualTo("source"));
            Assert.That(result.Session.ApprovalMode, Is.EqualTo(ApprovalMode.Custom));
            Assert.That(result.Session.ModelId, Is.Null);
        }

        [Test]
        public void EarlyEvents_AreReplayedOnceInOrderAfterThreadAssociation()
        {
            var buffer = new ReviewStartEventBuffer();
            buffer.Begin();
            var started = new AppServerEventArgs("turn_started", "", "review", "turn");
            var output = new AppServerEventArgs("assistant_delta", "first", "review", "turn");
            var terminal = new AppServerEventArgs("turn_completed", "", "review", "turn");
            Assert.That(buffer.TryBuffer(started, false), Is.True);
            Assert.That(buffer.TryBuffer(output, false), Is.True);
            Assert.That(buffer.TryBuffer(terminal, false), Is.True);
            Assert.That(buffer.Complete(), Is.EqualTo(new[] { started, output, terminal }));
            Assert.That(buffer.Complete(), Is.Empty);
            Assert.That(buffer.TryBuffer(output, false), Is.False);
        }

        [Test]
        public void ResponseBeforeEvents_AndDisconnect_AreNotBuffered()
        {
            var buffer = new ReviewStartEventBuffer();
            buffer.Begin();
            Assert.That(buffer.TryBuffer(new AppServerEventArgs("assistant_delta", "first", "review"), true), Is.False);
            Assert.That(buffer.TryBuffer(new AppServerEventArgs("turn_started", "", "unknown"), false), Is.True);
            Assert.That(buffer.TryBuffer(new AppServerEventArgs("app_server_disconnected", "lost"), false), Is.False);
            Assert.That(buffer.IsPending, Is.False);
            Assert.That(buffer.Complete(), Is.Empty);
        }

        private static SessionThreadOperationWorkflow Create(ReviewClient client)
        {
            return new SessionThreadOperationWorkflow(client,
                new SessionThreadService(client, new WorkspaceService(), path => new WorkspaceGitInfoSnapshot()), new SessionService());
        }

        private sealed class ReviewClient : UnsupportedAppServerThreadClient
        {
            internal string Model;
            internal string Effort;
            internal string ReviewModel;
            internal AppServerReviewTarget Target;
            internal bool Detached;
            internal Task<AppServerThreadDetails> Read = Task.FromResult(new AppServerThreadDetails { ThreadId = "review" });
            public override Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId,
                AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null)
            {
                Model = modelId; Effort = reasoningEffort; ReviewModel = reviewModelId;
                Target = target; Detached = runDetached;
                return Task.FromResult(new AppServerReviewStartResult { ReviewThreadId = threadId, TurnId = "turn" });
            }
            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId) { return Read; }
        }
    }
}
