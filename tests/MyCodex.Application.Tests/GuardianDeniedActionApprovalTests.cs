using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GuardianDeniedActionApprovalTests
    {
        [Test]
        public void DeniedCommand_PreservesActionAndBuildsCoreEventWithoutChangingNotification()
        {
            var parameters = Notification();
            var args = AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(parameters);
            var request = args.GuardianApprovalReview.ApprovalRequest;
            Assert.That(request.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(args.RequestId, Is.EqualTo("guardian:thread-1:review-1"));
            Assert.That(request.Event["id"], Is.EqualTo("review-1"));
            Assert.That(request.Event["target_item_id"], Is.EqualTo("item-1"));
            Assert.That(request.Event["turn_id"], Is.EqualTo("turn-1"));
            Assert.That(request.Event["started_at_ms"], Is.EqualTo(100));
            Assert.That(request.Event["completed_at_ms"], Is.EqualTo(200));
            Assert.That(request.Event["status"], Is.EqualTo("denied"));
            Assert.That(request.Event["risk_level"], Is.EqualTo("high"));
            Assert.That(request.Event["user_authorization"], Is.EqualTo("low"));
            var action = (IDictionary<string, object>)request.Event["action"];
            Assert.That(action["source"], Is.EqualTo("unified_exec"));
            Assert.That(action["command"], Is.EqualTo("Remove-Item -LiteralPath 'E:\\fixture\\delete-me' -Recurse -Force"));
            Assert.That(action["cwd"], Is.EqualTo(@"E:\fixture"));
            Assert.That(((IDictionary<string, object>)parameters["action"])["source"], Is.EqualTo("unifiedExec"));
        }

        [TestCase("approved", "agent")]
        [TestCase("inProgress", "agent")]
        [TestCase("timedOut", "agent")]
        [TestCase("aborted", "agent")]
        [TestCase("denied", "policy")]
        [TestCase("denied", null)]
        public void NonAgentDenial_DoesNotOfferManualOverride(string status, string source)
        {
            var parameters = Notification();
            ((IDictionary<string, object>)parameters["review"])["status"] = status;
            parameters["decisionSource"] = source;
            var args = AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(parameters);
            Assert.That(args.GuardianApprovalReview.ApprovalRequest, Is.Null);
            Assert.That(args.RequestId, Is.Null);
        }

        [Test]
        public void IncompleteUnknownOrUnidentifiedReview_DoesNotOfferManualOverride()
        {
            var parameters = Notification();
            Assert.That(AppServerGuardianApprovalReviewMapper.BuildStartedEvent(parameters).ApprovalRequest, Is.Null);
            parameters["reviewId"] = null;
            Assert.That(AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest, Is.Null);
            parameters["reviewId"] = "review-1";
            ((IDictionary<string, object>)parameters["action"])["type"] = "futureAction";
            Assert.That(AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest, Is.Null);
        }

        [TestCase("execve", "execve", "shell", "shell")]
        [TestCase("writeStdin", "write_stdin", "approvalId", "approval_id")]
        [TestCase("applyPatch", "apply_patch", "files", "files")]
        [TestCase("networkAccess", "network_access", "host", "host")]
        [TestCase("mcpToolCall", "mcp_tool_call", "toolName", "tool_name")]
        public void ActionFields_UseCoreNames(string type, string coreType, string field, string coreField)
        {
            var parameters = Notification();
            parameters["action"] = new Dictionary<string, object>
            {
                { "type", type }, { "source", "shell" }, { "cwd", @"E:\fixture" }, { field, "unchanged" }
            };
            var request = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest;
            var action = (IDictionary<string, object>)request.Event["action"];
            Assert.That(action["type"], Is.EqualTo(coreType));
            Assert.That(action[coreField], Is.EqualTo("unchanged"));
        }

        [TestCase(@"E:\fixture\a #b", "file:///E:/fixture/a%20%23b")]
        [TestCase("/home/user/a%20b", "file:///home/user/a%2520b")]
        [TestCase("/Users/user/a #b", "file:///Users/user/a%20%23b")]
        [TestCase(@"/home/user/a\b", "file:///home/user/a%5Cb")]
        [TestCase(@"\\server\share\a #b", "file://server/share/a%20%23b")]
        [TestCase("file:///home/user/fixture", "file:///home/user/fixture")]
        public void TerminalInput_UsesNativePathUriWithoutHostPathResolution(string path, string expected)
        {
            var parameters = Notification();
            var action = (IDictionary<string, object>)parameters["action"];
            action["type"] = "writeStdin";
            action["cwd"] = path;
            var request = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest;
            Assert.That(((IDictionary<string, object>)request.Event["action"])["cwd"], Is.EqualTo(expected));
        }

        [TestCase(@"E:\fixture")]
        [TestCase("/home/user/fixture")]
        [TestCase("/Users/user/fixture")]
        public void PermissionPathsAndEntries_ArePreservedAcrossTargets(string path)
        {
            var entries = new object[] { new Dictionary<string, object>
            {
                { "access", "write" }, { "path", new Dictionary<string, object> { { "type", "path" }, { "path", path } } }
            } };
            var filesystem = new Dictionary<string, object>
            {
                { "read", new[] { path } }, { "write", new[] { path } },
                { "globScanMaxDepth", 3 }, { "entries", entries }
            };
            var parameters = Notification();
            parameters["action"] = new Dictionary<string, object>
            {
                { "type", "requestPermissions" }, { "reason", "write files" },
                { "permissions", new Dictionary<string, object> { { "network", null }, { "fileSystem", filesystem } } }
            };
            var request = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest;
            var action = (IDictionary<string, object>)request.Event["action"];
            var permissions = (IDictionary<string, object>)action["permissions"];
            var mapped = (IDictionary<string, object>)permissions["file_system"];
            Assert.That(mapped["glob_scan_max_depth"], Is.EqualTo(3));
            Assert.That(mapped["entries"], Is.SameAs(entries));
            Assert.That(mapped.ContainsKey("read"), Is.False);
            Assert.That(mapped.ContainsKey("write"), Is.False);
            Assert.That(filesystem.ContainsKey("globScanMaxDepth"), Is.True);

            filesystem.Remove("entries");
            request = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest;
            action = (IDictionary<string, object>)request.Event["action"];
            permissions = (IDictionary<string, object>)action["permissions"];
            mapped = (IDictionary<string, object>)permissions["file_system"];
            var legacyEntries = (List<object>)mapped["entries"];
            Assert.That(legacyEntries.Count, Is.EqualTo(2));
            Assert.That(((IDictionary<string, object>)legacyEntries[0])["access"], Is.EqualTo("read"));
            var mappedPath = (IDictionary<string, object>)((IDictionary<string, object>)legacyEntries[1])["path"];
            Assert.That(mappedPath["path"], Is.EqualTo(path));
        }

        [TestCase("http", "http")]
        [TestCase("https", "https")]
        [TestCase("socks5Tcp", "socks5_tcp")]
        [TestCase("socks5Udp", "socks5_udp")]
        public void NetworkProtocols_UseCoreNames(string protocol, string expected)
        {
            var parameters = Notification();
            var action = (IDictionary<string, object>)parameters["action"];
            action["type"] = "networkAccess";
            action["protocol"] = protocol;
            var request = AppServerGuardianApprovalReviewMapper.BuildCompletedEvent(parameters).ApprovalRequest;
            Assert.That(((IDictionary<string, object>)request.Event["action"])["protocol"], Is.EqualTo(expected));
        }

        [Test]
        public void DialogState_OffersEachLiveDenialOnceAndInvalidatesOldQueuedEvents()
        {
            var state = new GuardianDeniedActionApprovalState();
            var first = AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(Notification());
            var duplicate = AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(Notification());
            Assert.That(state.TryRegister(first), Is.True);
            Assert.That(state.TryRegister(duplicate), Is.False);
            Assert.That(state.TryTake(duplicate), Is.False);
            Assert.That(state.TryTake(first), Is.True);
            Assert.That(state.TryTake(first), Is.False);
            Assert.That(state.TryRegister(duplicate), Is.False);
            state.Clear();
            Assert.That(state.TryRegister(duplicate), Is.True);
            Assert.That(state.TryTake(first), Is.False);
            Assert.That(state.TryTake(duplicate), Is.True);

            state.Clear();
            var historical = new AppServerEventArgs("auto_approval_review_completed", "denied",
                threadId: first.ThreadId, requestId: first.RequestId,
                guardianApprovalReview: first.GuardianApprovalReview, isHistorical: true);
            Assert.That(state.TryRegister(historical), Is.False);
            var wrongThread = new AppServerEventArgs("auto_approval_review_completed", "denied",
                threadId: "other-thread", requestId: first.RequestId, guardianApprovalReview: first.GuardianApprovalReview);
            Assert.That(state.TryRegister(wrongThread), Is.False);
        }

        private static Dictionary<string, object> Notification()
        {
            return new Dictionary<string, object>
            {
                { "threadId", "thread-1" }, { "turnId", "turn-1" }, { "reviewId", "review-1" },
                { "targetItemId", "item-1" }, { "startedAtMs", 100L }, { "completedAtMs", 200L },
                { "decisionSource", "agent" },
                { "review", new Dictionary<string, object>
                    { { "status", "denied" }, { "riskLevel", "high" }, { "userAuthorization", "low" }, { "rationale", "Needs authorization" } } },
                { "action", new Dictionary<string, object>
                    { { "type", "command" }, { "source", "unifiedExec" }, { "cwd", @"E:\fixture" },
                        { "command", "Remove-Item -LiteralPath 'E:\\fixture\\delete-me' -Recurse -Force" } } }
            };
        }
    }
}
