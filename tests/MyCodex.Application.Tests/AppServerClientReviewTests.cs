using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AppServerClientMessageTests
    {
        [TestCase(ApprovalMode.Custom)]
        [TestCase(ApprovalMode.Auto)]
        [TestCase(ApprovalMode.ReadOnly)]
        [TestCase(ApprovalMode.FullAccessAutoApprove)]
        [TestCase(ApprovalMode.FullAccess)]
        [TestCase(null)]
        public async Task Review_AfterThreadClosed_RestoresHostApprovalModeBeforeReviewRequest(ApprovalMode? mode)
        {
            var client = CreateClient(new AppConfigSnapshot
            {
                CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    EnabledPermissions = new List<CustomApprovalPermissionKind> { CustomApprovalPermissionKind.GitStage }
                }
            });
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                SetPrivateField(client, "_isConnected", true);
                SetPrivateField(client, "_process", Process.GetCurrentProcess());
                SetPrivateField(client, "_inputWriter", writer);
                var modes = GetPrivateField<Dictionary<string, ApprovalMode>>(client, "_threadApprovalModes");
                modes["thread-1"] = ApprovalMode.Custom;
                var toolContexts = GetPrivateField<Dictionary<string, InternalToolExecutionContext>>(client, "_internalToolContexts");
                var toolContext = new InternalToolExecutionContext { WorkspacePath = @"E:\Repo" };
                toolContexts["thread-1"] = toolContext;
                var operation = client.StartReviewAsync("thread-1", @"E:\Repo", mode, "model",
                    AppServerReviewTarget.CreateUncommittedChanges(), false, "high");
                await CompleteReviewPreparationAsync(client, stream, mode, false, null);

                var review = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 5));
                Assert.That(review["method"], Is.EqualTo("review/start"));
                Assert.That(modes.ContainsKey("thread-1"), Is.EqualTo(mode.HasValue));
                Assert.That(toolContexts["thread-1"], Is.SameAs(toolContext));
                if (mode.HasValue) Assert.That(modes["thread-1"], Is.EqualTo(mode.Value));
                if (mode == ApprovalMode.Custom)
                {
                    var events = new List<AppServerEventArgs>();
                    client.EventReceived += (sender, item) => events.Add(item);
                    InvokePrivateInstance(client, "HandleCommandApprovalRequest", "review-approval", "review-approval",
                        new Dictionary<string, object>
                        {
                            { "threadId", "thread-1" }, { "turnId", "review-turn" },
                            { "itemId", "item-1" }, { "command", "git add README.md" }, { "cwd", @"E:\Repo" }
                        });
                    var output = await WaitForWrittenLineCountAsync(stream, 6);
                    Assert.That(output, Does.Contain("\"decision\":\"accept\""));
                    Assert.That(events.Select(item => item.EventType), Is.EqualTo(new[] { "approval_resolved" }));
                    Assert.That(events[0].ApprovalClassification.Category, Is.EqualTo(ApprovalRequestCategory.GitStage));
                }
                CompleteRequest(client, Convert.ToString(review["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object>
                    {
                        { "reviewThreadId", "thread-1" },
                        { "turn", new Dictionary<string, object> { { "id", "review-turn" } } }
                    });
                await operation;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Review_FailedOrCancelledPreparation_DoesNotRestoreHostModeOrStartReview(bool cancel)
        {
            var client = CreateClient();
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            using (var cancellation = new CancellationTokenSource())
            {
                SetPrivateField(client, "_isConnected", true);
                SetPrivateField(client, "_process", Process.GetCurrentProcess());
                SetPrivateField(client, "_inputWriter", writer);
                SetPrivateField(client, "_lifetimeCts", cancellation);
                var modes = GetPrivateField<Dictionary<string, ApprovalMode>>(client, "_threadApprovalModes");
                modes["thread-1"] = ApprovalMode.Custom;
                var operation = client.StartReviewAsync("thread-1", @"E:\Repo", ApprovalMode.Custom, "model",
                    AppServerReviewTarget.CreateUncommittedChanges(), false, "high");
                await CompleteReviewPreparationAsync(client, stream, ApprovalMode.Custom, !cancel,
                    cancel ? (Action)cancellation.Cancel : null);
                if (cancel) Assert.CatchAsync<OperationCanceledException>(async () => await operation);
                else Assert.ThrowsAsync<InvalidOperationException>(async () => await operation);
                Assert.That(modes.ContainsKey("thread-1"), Is.False);
                Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Not.Contain("review/start"));
            }
        }

        [TestCase(ApprovalMode.Custom)]
        [TestCase(ApprovalMode.Auto)]
        [TestCase(ApprovalMode.ReadOnly)]
        [TestCase(ApprovalMode.FullAccessAutoApprove)]
        [TestCase(ApprovalMode.FullAccess)]
        [TestCase(null)]
        public async Task NewReview_RegistersNewThreadBeforeEarlyApproval_AndPreservesSource(ApprovalMode? mode)
        {
            var client = CreateClient(new AppConfigSnapshot
            {
                CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    EnabledPermissions = new List<CustomApprovalPermissionKind> { CustomApprovalPermissionKind.GitStage }
                }
            });
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                SetPrivateField(client, "_isConnected", true);
                SetPrivateField(client, "_process", Process.GetCurrentProcess());
                SetPrivateField(client, "_inputWriter", writer);
                var modes = GetPrivateField<Dictionary<string, ApprovalMode>>(client, "_threadApprovalModes");
                modes["source"] = ApprovalMode.ReadOnly;
                var contexts = GetPrivateField<Dictionary<string, InternalToolExecutionContext>>(client, "_internalToolContexts");
                var sourceContext = new InternalToolExecutionContext { WorkspacePath = @"E:\Repo" };
                contexts["source"] = sourceContext;
                var operation = client.StartReviewAsync("source", @"E:\Repo", mode, "model",
                    AppServerReviewTarget.CreateCustom("Check errors"), true, "high");
                var start = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
                Assert.That(start["method"], Is.EqualTo("thread/start"));
                var startParameters = AppServerJsonValue.GetDictionary(start, "params");
                Assert.That(startParameters.ContainsKey("approvalsReviewer"), Is.EqualTo(mode.HasValue));
                if (mode.HasValue) Assert.That(startParameters["approvalsReviewer"], Is.EqualTo("user"));
                CompleteRequest(client, Convert.ToString(start["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object>
                    {
                        { "thread", new Dictionary<string, object> { { "id", "new-review" }, { "status", new Dictionary<string, object> { { "type", "idle" } } } } },
                        { "cwd", @"E:\Repo" }, { "model", "model" }, { "reasoningEffort", "high" },
                        { "approvalPolicy", "on-request" },
                        { "approvalsReviewer", "user" },
                        { "sandbox", new Dictionary<string, object> { { "type", (mode == ApprovalMode.FullAccess || mode == ApprovalMode.FullAccessAutoApprove) ? "dangerFullAccess" : mode == ApprovalMode.Auto ? "workspaceWrite" : "readOnly" } } }
                    });
                var review = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 2));
                Assert.That(review["method"], Is.EqualTo("review/start"));
                Assert.That(((IDictionary<string, object>)review["params"])["threadId"], Is.EqualTo("new-review"));
                Assert.That(modes["source"], Is.EqualTo(ApprovalMode.ReadOnly));
                Assert.That(contexts["source"], Is.SameAs(sourceContext));
                Assert.That(contexts["new-review"], Is.SameAs(sourceContext));
                Assert.That(modes.ContainsKey("new-review"), Is.EqualTo(mode.HasValue));
                if (mode.HasValue) Assert.That(modes["new-review"], Is.EqualTo(mode));
                if (mode == ApprovalMode.Custom)
                {
                    InvokePrivateInstance(client, "HandleCommandApprovalRequest", "new-approval", "new-approval",
                        new Dictionary<string, object>
                        {
                            { "threadId", "new-review" }, { "turnId", "review-turn" },
                            { "itemId", "item-1" }, { "command", "git add README.md" }, { "cwd", @"E:\Repo" }
                        });
                    Assert.That(await WaitForWrittenLineCountAsync(stream, 3), Does.Contain("\"decision\":\"accept\""));
                }
                CompleteRequest(client, Convert.ToString(review["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object>
                    {
                        { "reviewThreadId", "new-review" },
                        { "turn", new Dictionary<string, object> { { "id", "review-turn" } } }
                    });
                var result = await operation;
                Assert.That(result.CreatedThread.ThreadId, Is.EqualTo("new-review"));
                Assert.That(result.WorkingDirectory, Is.EqualTo(@"E:\Repo"));
                Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Not.Contain("thread/unsubscribe").And.Not.Contain("thread/resume"));
            }
        }

        private static async Task CompleteReviewPreparationAsync(AppServerClient client, MemoryStream stream,
            ApprovalMode? mode, bool mismatch, Action beforeResumeResponse)
        {
            var read = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
            Assert.That(read["method"], Is.EqualTo("thread/read"));
            var idle = new Dictionary<string, object>
            {
                { "id", "thread-1" }, { "status", new Dictionary<string, object> { { "type", "idle" } } }
            };
            CompleteRequest(client, Convert.ToString(read["id"], CultureInfo.InvariantCulture),
                new Dictionary<string, object> { { "thread", idle } });
            var unsubscribe = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 2));
            Assert.That(unsubscribe["method"], Is.EqualTo("thread/unsubscribe"));
            InvokePrivateInstance(client, "HandleNotification", new Dictionary<string, object>
            {
                { "method", "thread/closed" },
                { "params", new Dictionary<string, object> { { "threadId", "thread-1" } } }
            });
            Assert.That(GetPrivateField<Dictionary<string, ApprovalMode>>(client, "_threadApprovalModes").ContainsKey("thread-1"), Is.False);
            CompleteRequest(client, Convert.ToString(unsubscribe["id"], CultureInfo.InvariantCulture), new Dictionary<string, object>());
            await CompleteResumeProviderReadAsync(client, stream, 3);
            var resume = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 4));
            Assert.That(resume["method"], Is.EqualTo("thread/resume"));
            var resumeParameters = AppServerJsonValue.GetDictionary(resume, "params");
            Assert.That(resumeParameters["modelProvider"], Is.EqualTo("current-provider"));
            Assert.That(resumeParameters.ContainsKey("approvalsReviewer"), Is.EqualTo(mode.HasValue));
            if (mode.HasValue) Assert.That(resumeParameters["approvalsReviewer"], Is.EqualTo("user"));
            if (beforeResumeResponse != null) beforeResumeResponse();
            CompleteRequest(client, Convert.ToString(resume["id"], CultureInfo.InvariantCulture),
                new Dictionary<string, object>
                {
                    { "thread", idle }, { "cwd", @"E:\Repo" }, { "model", mismatch ? "wrong-model" : "model" },
                    { "reasoningEffort", "high" }, { "approvalPolicy", "on-request" },
                    { "approvalsReviewer", "user" },
                    { "sandbox", new Dictionary<string, object> { { "type", (mode == ApprovalMode.FullAccess || mode == ApprovalMode.FullAccessAutoApprove) ? "dangerFullAccess" : mode == ApprovalMode.Auto ? "workspaceWrite" : "readOnly" } } }
                });
        }
    }
}
