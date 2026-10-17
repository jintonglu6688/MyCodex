using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionThreadOperationWorkflowTests
    {
        [TestCase(true, "turn-2", "turn-2")]
        [TestCase(true, null, null)]
        [TestCase(false, null, null)]
        public async Task CreateBranchSession_UsesVersionGatedTurnBoundedFork(
            bool supportsTurnBoundedFork,
            string lastTurnId,
            string expectedForkLastTurnId)
        {
            var client = new RecordingThreadClient(supportsTurnBoundedFork);
            var workflow = CreateWorkflow(client);
            var sourceSession = CreateSourceSession();
            var workspace = CreateWorkspace();

            var createdSession = await workflow.CreateForkSessionAsync(
                    sourceSession,
                    workspace,
                    "Fork title",
                    1,
                    lastTurnId,
                    ApprovalMode.Auto,
                    "gpt-test",
                    "medium");

            Assert.That(client.ForkedThreadId, Is.EqualTo("source-thread"));
            Assert.That(client.ForkedLastTurnId, Is.EqualTo(expectedForkLastTurnId));
            Assert.That(client.ForkCallCount, Is.EqualTo(1));
            Assert.That(client.RenamedThreadId, Is.EqualTo("fork-thread"));
            Assert.That(client.GitMetadataThreadId, Is.EqualTo("fork-thread"));
            Assert.That(createdSession.ThreadId, Is.EqualTo("fork-thread"));
            Assert.That(createdSession.Title, Is.EqualTo("Fork title"));
            Assert.That(createdSession.IsFork, Is.True);
        }

        [TestCase(null, null)]
        [TestCase("local-model", "high")]
        public async Task Fork_PreservesOverridesAndSurvivesNamingFailure(string model, string effort)
        {
            var client = new RecordingThreadClient(true) { FailName = true };
            var source = CreateSourceSession();
            source.ModelId = model;
            source.ReasoningEffort = effort;
            string warning = null;
            var result = await CreateWorkflow(client).CreateForkSessionAsync(source, CreateWorkspace(), "Fork", 1, null,
                ApprovalMode.Auto, "resolved-model", "medium", message => warning = message);
            Assert.That(result.ThreadId, Is.EqualTo("fork-thread"));
            Assert.That(result.ModelId, Is.EqualTo(model));
            Assert.That(result.ReasoningEffort, Is.EqualTo(effort));
            Assert.That(source.ThreadId, Is.EqualTo("source-thread"));
            Assert.That(warning, Does.Contain("fork-thread"));
            Assert.That(client.RequestModel, Is.EqualTo("resolved-model"));
            Assert.That(client.RequestEffort, Is.EqualTo("medium"));
        }

        [TestCase("inProgress")]
        [TestCase(null)]
        [TestCase("unknown")]
        public void Fork_RejectsUnfinishedOrUnverifiableAnchor(string status)
        {
            var client = new RecordingThreadClient(true) { TurnStatus = status };
            Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateWorkflow(client).CreateForkSessionAsync(
                CreateSourceSession(), CreateWorkspace(), "Fork", 1, "turn-2", ApprovalMode.Auto, "model", "high"));
            Assert.That(client.ForkCallCount, Is.Zero);
        }

        [Test]
        public void Fork_OldRuntimeNeverCreatesFullFallback()
        {
            var client = new RecordingThreadClient(false);
            Assert.ThrowsAsync<NotSupportedException>(async () => await CreateWorkflow(client).CreateForkSessionAsync(
                CreateSourceSession(), CreateWorkspace(), "Fork", 1, "turn-2", ApprovalMode.Auto, "model", "high"));
            Assert.That(client.ForkCallCount, Is.Zero);
        }

        [Test]
        public void CreateForkSessionAsync_MissingForkThreadIdPreservesFailureBehavior()
        {
            var client = new RecordingThreadClient(true)
            {
                ForkSummary = new AppServerThreadSummary()
            };
            var workflow = CreateWorkflow(client);

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await workflow.CreateForkSessionAsync(
                    CreateSourceSession(),
                    CreateWorkspace(),
                    "Fork title",
                    1,
                    null,
                    ApprovalMode.Auto,
                    "gpt-test",
                    "medium"));
            Assert.That(client.ForkCallCount, Is.EqualTo(1));
            Assert.That(client.RenamedThreadId, Is.Null);
            Assert.That(client.GitMetadataThreadId, Is.Null);
        }

        private static SessionThreadOperationWorkflow CreateWorkflow(RecordingThreadClient client)
        {
            return new SessionThreadOperationWorkflow(
                client,
                new SessionThreadService(
                    client,
                    new WorkspaceService(),
                    path => new WorkspaceGitInfoSnapshot
                    {
                        IsGitRepository = true,
                        OriginUrl = "https://example.test/repo.git",
                        Branch = "main",
                        Sha = "abcdef"
                    }),
                new SessionService());
        }

        private static ChatSession CreateSourceSession()
        {
            return new ChatSession
            {
                Id = "source-session",
                WorkspaceId = "workspace-1",
                ThreadId = "source-thread",
                Title = "Source",
                ApprovalMode = ApprovalMode.Auto,
                ModelId = "gpt-test",
                ReasoningEffort = "medium"
            };
        }

        private static WorkspaceInfo CreateWorkspace()
        {
            return new WorkspaceInfo
            {
                Id = "workspace-1",
                Name = "Repo",
                RootPath = @"E:\Repo",
                Kind = WorkspaceKind.Coding
            };
        }

        private sealed class RecordingThreadClient : UnsupportedAppServerThreadClient, IAppServerConnectionClient
        {
            public bool FailName { get; set; }
            public string TurnStatus { get; set; } = "completed";
            public string RequestModel { get; private set; }
            public string RequestEffort { get; private set; }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
            {
                RequestModel = modelId;
                RequestEffort = reasoningEffort;
                return ForkThreadAsync(threadId, lastTurnId);
            }
            public RecordingThreadClient(bool supportsTurnBoundedFork)
            {
                ProtocolCapabilities = new AppServerProtocolCapabilities
                {
                    RuntimeVersion = supportsTurnBoundedFork ? "0.144.1" : "0.142.5",
                    SupportsThreadForkLastTurnId = supportsTurnBoundedFork
                };
                ForkSummary = new AppServerThreadSummary
                {
                    ThreadId = "fork-thread",
                    WorkingDirectory = @"E:\Repo"
                };
            }

            public event EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public AppServerProtocolCapabilities ProtocolCapabilities { get; }

            public AppServerThreadSummary ForkSummary { get; set; }

            public int ForkCallCount { get; private set; }


            public string ForkedThreadId { get; private set; }

            public string ForkedLastTurnId { get; private set; }



            public string RenamedThreadId { get; private set; }

            public string GitMetadataThreadId { get; private set; }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId, System.Threading.CancellationToken cancellationToken)
            {
                var details = new AppServerThreadDetails { ThreadId = threadId };
                details.TurnStatuses["turn-2"] = TurnStatus;
                return Task.FromResult(details);
            }

            public Task ConnectAsync()
            {
                return Task.CompletedTask;
            }

            public Task RestartAsync()
            {
                return Task.CompletedTask;
            }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
            {
                ForkCallCount++;
                ForkedThreadId = threadId;
                ForkedLastTurnId = null;
                return Task.FromResult(ForkSummary);
            }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId)
            {
                ForkCallCount++;
                ForkedThreadId = threadId;
                ForkedLastTurnId = lastTurnId;
                return Task.FromResult(ForkSummary);
            }



            public override Task SetThreadNameAsync(string threadId, string name)
            {
                if (FailName) throw new InvalidOperationException("rename failed");
                RenamedThreadId = threadId;
                return Task.CompletedTask;
            }

            public override Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
            {
                GitMetadataThreadId = threadId;
                return Task.CompletedTask;
            }
        }
    }
}
