using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ThreadAssociationFormTests
    {
        [Test]
        public void BindThreads_DefaultFilterShowsOnlyLinkableSessionsWithoutSelectingThem()
        {
            var targetWorkspace = new WorkspaceInfo { Id = "target", Name = "Repo", RootPath = @"E:\Repo" };
            var client = new FakeAssociationClient();
            using (var form = new ThreadAssociationForm(
                client,
                client,
                new[]
                {
                    new ChatSession { WorkspaceId = "other", ThreadId = "elsewhere" },
                    new ChatSession { WorkspaceId = "target", ThreadId = "current" }
                },
                targetWorkspace))
            {
                form.BindThreadsForTests(new[]
                {
                    Thread("fresh", @"E:\Old", "Fresh"),
                    Thread("elsewhere", @"E:\Old", "Elsewhere"),
                    Thread("current", @"E:\Old", "Current")
                });

                Assert.That(form.SelectedFilterModeForTests, Is.EqualTo(ThreadAssociationFilterMode.Linkable));
                Assert.That(form.VisibleItemCountForTests, Is.EqualTo(2));
                Assert.That(form.GetSelectedThreads(), Is.Empty);
            }
        }

        [Test]
        public void BindThreads_AllFilterShowsCurrentWorkspaceStatus()
        {
            var targetWorkspace = new WorkspaceInfo { Id = "target", Name = "Repo", RootPath = @"E:\Repo" };
            var client = new FakeAssociationClient();
            using (var form = new ThreadAssociationForm(
                client,
                client,
                new[] { new ChatSession { WorkspaceId = "target", ThreadId = "current" } },
                targetWorkspace))
            {
                form.BindThreadsForTests(new[] { Thread("current", @"E:\Old", "Current") });
                form.SelectFilterForTests(ThreadAssociationFilterMode.All);

                Assert.That(form.VisibleItemCountForTests, Is.EqualTo(1));
                Assert.That(
                    form.FirstStatusTextForTests,
                    Is.EqualTo(UiLocalization.Text("ThreadAssociationForm.StatusValue.CurrentWorkspace", "Already in current workspace")));
                Assert.That(form.GetSelectedThreads(), Is.Empty);
            }
        }

        private static AppServerThreadSummary Thread(string threadId, string workingDirectory, string title)
        {
            return new AppServerThreadSummary
            {
                ThreadId = threadId,
                WorkingDirectory = workingDirectory,
                Title = title,
                UpdatedAt = new DateTime(2026, 5, 21, 12, 0, 0)
            };
        }

        private sealed class FakeAssociationClient : UnsupportedAppServerThreadClient, IAppServerConnectionClient
        {
            public event EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public AppServerProtocolCapabilities ProtocolCapabilities { get; } = new AppServerProtocolCapabilities();

            public List<AppServerThreadSummary> Threads { get; set; } = new List<AppServerThreadSummary>();

            public Task ConnectAsync()
            {
                return Task.CompletedTask;
            }

            public Task RestartAsync()
            {
                throw new NotSupportedException();
            }

            public override Task<string> CreateThreadAsync(
                string workspacePath,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerThreadToolContext toolContext = null)
            {
                throw new NotSupportedException();
            }

            public override Task SendUserMessageAsync(
                string threadId,
                string workspacePath,
                IReadOnlyCollection<AppServerUserInputItem> inputItems,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerCollaborationMode collaborationMode = null)
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync()
            {
                return Task.FromResult(Threads);
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Threads);
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null)
            {
                throw new NotSupportedException();
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult<AppServerConversationSummary>(null);
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task ArchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnarchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }



            public override Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null)
            {
                return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double"));
            }

            public override Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = true });
            }

            public override Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }

            public override Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null)
            {
                throw new NotSupportedException();
            }

            public override Task DeleteThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnsubscribeThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task DeleteThreadFileAsync(string sessionPath)
            {
                throw new NotSupportedException();
            }

            public override Task SetThreadNameAsync(string threadId, string name)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerThreadGoal> ClearThreadGoalAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
            {
                throw new NotSupportedException();
            }
        }
    }
}
