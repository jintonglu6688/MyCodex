using System.Collections.Generic;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodingRemoteEventMapperTests
    {
        [Test]
        public void TryMap_OfficeRetry_StaysRunning()
        {
            var session = new ChatSession { Id = "office", ThreadId = "thread", Mode = AppInteractionMode.Office };
            RemoteRelayEvent result;
            Assert.That(CodingRemoteEventMapper.TryMap(
                new AppServerEventArgs("turn_retrying", "retrying", threadId: "thread", turnId: "turn"), session, out result), Is.True);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.OfficeSessionUpdated));
            Assert.That(result.Payload["status"], Is.EqualTo("running"));
        }

        [TestCase("turn_started", RemoteEventTypes.CodingSessionUpdated, "running")]
        [TestCase("turn_retrying", RemoteEventTypes.CodingSessionUpdated, "running")]
        [TestCase("assistant", RemoteEventTypes.CodingSessionUpdated, "running")]
        [TestCase("turn_completed", RemoteEventTypes.CodingMessageCompleted, "completed")]
        [TestCase("turn_failed", RemoteEventTypes.CodingMessageFailed, "failed")]
        [TestCase("error", RemoteEventTypes.CodingMessageFailed, "failed")]
        public void TryMap_CodingThreadEvent_ReturnsSafeSessionEvent(string appServerEventType, string expectedEventType, string expectedStatus)
        {
            var session = new ChatSession
            {
                Id = "session-a",
                ThreadId = "thread-a",
                Mode = AppInteractionMode.Coding
            };
            var appEvent = new AppServerEventArgs(
                appServerEventType,
                "contains text that must not be used as a command",
                threadId: "thread-a",
                turnId: "turn-a",
                commandText: "secret command text");

            RemoteRelayEvent remoteEvent;
            var mapped = CodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(expectedEventType));
            Assert.That(remoteEvent.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(remoteEvent.Payload["status"], Is.EqualTo(expectedStatus));
            Assert.That(remoteEvent.Payload["threadId"], Is.EqualTo("thread-a"));
            Assert.That(remoteEvent.Payload["isSubAgent"], Is.False);
            Assert.That(remoteEvent.Payload.ContainsKey("commandText"), Is.False);
        }

        [Test]
        public void TryMap_SubAgentTurnCompleted_IncludesSubAgentMarker()
        {
            var session = new ChatSession
            {
                Id = "subagent-session",
                ThreadId = "subagent-thread",
                Mode = AppInteractionMode.Coding,
                IsSubAgent = true
            };
            var appEvent = new AppServerEventArgs(
                "turn_completed",
                "completed",
                threadId: "subagent-thread",
                turnId: "subagent-turn");

            RemoteRelayEvent remoteEvent;
            var mapped = CodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageCompleted));
            Assert.That(remoteEvent.Payload["isSubAgent"], Is.True);
        }

        [Test]
        public void TryMap_ApprovalRequested_DefaultMapperDoesNotExposeRawCommandText()
        {
            var session = new ChatSession
            {
                Id = "session-a",
                ThreadId = "thread-a",
                Mode = AppInteractionMode.Coding
            };
            var appEvent = new AppServerEventArgs(
                "approval_requested",
                "approval content",
                threadId: "thread-a",
                turnId: "turn-a",
                requestId: "approval-1",
                commandText: "Remove-Item -LiteralPath secret.txt",
                approvalKind: AppServerApprovalRequestKind.Command,
                canApproveForSession: true);

            RemoteRelayEvent remoteEvent;
            var mapped = CodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ApprovalRequested));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("approval-1"));
            Assert.That(remoteEvent.Payload["status"], Is.EqualTo("waiting_approval"));
            Assert.That(remoteEvent.Payload.ContainsKey("commandText"), Is.False);
        }

        [Test]
        public void TryMap_ApprovalRequested_WinFormsMapperIncludesDialogPayload()
        {
            var session = new ChatSession
            {
                Id = "session-a",
                ThreadId = "thread-a",
                Mode = AppInteractionMode.Coding
            };
            var appEvent = new AppServerEventArgs(
                "approval_requested",
                "approval content",
                threadId: "thread-a",
                turnId: "turn-a",
                requestId: "approval-1",
                commandText: "Set-Content -LiteralPath approval-flow-test.txt -Value \"approval flow test\"",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command,
                canApproveForSession: true);

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ApprovalRequested));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("approval-1"));
            Assert.That(remoteEvent.Payload["title"], Is.Not.Null.And.Not.Empty);
            Assert.That(remoteEvent.Payload["questionText"], Does.Contain("approval-flow-test.txt"));
            Assert.That(remoteEvent.Payload["detailsText"], Does.Contain("Set-Content -LiteralPath"));
            Assert.That(remoteEvent.Payload["actionKey"], Is.EqualTo("Write"));
            Assert.That(remoteEvent.Payload["canApproveForSession"], Is.True);
            Assert.That(remoteEvent.Payload.ContainsKey("commandText"), Is.False);

            var decisions = remoteEvent.Payload["decisions"] as IReadOnlyList<Dictionary<string, object>>;
            Assert.That(decisions, Is.Not.Null);
            Assert.That(decisions, Has.Count.EqualTo(3));
            Assert.That(decisions[0]["id"], Is.EqualTo("allow_once"));
            Assert.That(decisions[1]["id"], Is.EqualTo("allow_for_session"));
            Assert.That(decisions[2]["id"], Is.EqualTo("decline"));
            Assert.That(decisions[0]["label"], Is.EqualTo(UiLocalization.Text("CodingRemote.Approval.Decision.AllowOnce", "Allow once")));
            Assert.That(decisions[1]["label"], Is.EqualTo(UiLocalization.Text("CodingRemote.Approval.Decision.AllowForSession", "Allow for session")));
            Assert.That(decisions[2]["label"], Is.EqualTo(UiLocalization.Text("CodingRemote.Approval.Decision.Decline", "Decline")));
            foreach (var decision in decisions)
            {
                Assert.That((string)decision["label"], Does.Not.Contain("&"));
            }
        }

        [Test]
        public void TryMap_ToolUserInputRequested_BuildsRemoteToolInputEvent()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var question = new AppServerToolUserInputQuestion
            {
                Id = "choice",
                Header = "选择",
                Question = "请选择模式",
                Description = "用于测试",
                IsOther = true,
                IsSecret = false
            };
            question.Options.Add(new AppServerToolUserInputOption { Label = "继续", Value = "continue", Description = "继续当前计划" });
            var appEvent = new AppServerEventArgs(
                "tool_user_input_requested",
                "Need input",
                threadId: "thread-1",
                turnId: "turn-1",
                itemId: "item-1",
                requestId: "input-1",
                toolQuestions: new[] { question });

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ToolInputRequested));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("input-1"));
            var questions = (IReadOnlyList<Dictionary<string, object>>)remoteEvent.Payload["questions"];
            Assert.That(questions[0]["id"], Is.EqualTo("choice"));
            Assert.That(questions[0]["isOther"], Is.EqualTo(true));
        }

        [Test]
        public void TryMap_ToolUserInputResolved_BuildsRemoteToolInputCompletedEvent()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var appEvent = new AppServerEventArgs(
                "tool_user_input_resolved",
                "Tool input submitted.",
                threadId: "thread-1",
                turnId: "turn-1",
                requestId: "input-1");

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ToolInputCompleted));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("input-1"));
            Assert.That(remoteEvent.Payload["status"], Is.EqualTo("input_completed"));
        }

        [Test]
        public void TryMap_AsyncInputQueued_BuildsAsyncToolInputEventWithFreeFormAnswer()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var question = new AppServerToolUserInputQuestion { Id = "async-1", Question = "使用哪个数据库？" };
            question.Options.Add(new AppServerToolUserInputOption { Label = "SQLite", Value = "SQLite" });
            var appEvent = new AppServerEventArgs(
                RemoteAsyncInputRequests.QueuedEventType,
                "assistant reply",
                threadId: "thread-1",
                turnId: "turn-1",
                itemId: "item-1",
                requestId: "async:thread-1:item-1",
                details: "工作区 / 会话",
                toolQuestions: new[] { question });

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ToolInputRequested));
            Assert.That(remoteEvent.RequestId, Is.EqualTo("async:thread-1:item-1"));
            Assert.That(remoteEvent.Payload["kind"], Is.EqualTo("async"));
            Assert.That(remoteEvent.Payload["title"], Is.EqualTo(UiLocalization.Text("AsyncInputForm.Title", "Pending questions")));
            Assert.That((string)remoteEvent.Payload["headerText"], Does.Contain("工作区 / 会话"));
            Assert.That(remoteEvent.Payload.ContainsKey("detailsText"), Is.False);
            var questions = (IReadOnlyList<Dictionary<string, object>>)remoteEvent.Payload["questions"];
            Assert.That(questions[0]["id"], Is.EqualTo("async-1"));
            Assert.That(questions[0]["isOther"], Is.EqualTo(true));
        }

        [Test]
        public void TryMap_AsyncInputResolved_BuildsToolInputCompletedEvent()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var appEvent = new AppServerEventArgs(
                RemoteAsyncInputRequests.ResolvedEventType,
                string.Empty,
                threadId: "thread-1",
                turnId: "turn-1",
                requestId: "async:thread-1:item-1");

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ToolInputCompleted));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("async:thread-1:item-1"));
        }

        [Test]
        public void TryMap_RawAsyncInputRequested_IsNotPublishedBeforeWindowsQueuesIt()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var question = new AppServerToolUserInputQuestion { Id = "async-1", Question = "Q" };
            var appEvent = new AppServerEventArgs("async_input_requested", "reply", threadId: "thread-1", itemId: "item-1", toolQuestions: new[] { question });

            RemoteRelayEvent remoteEvent;
            WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(remoteEvent == null || remoteEvent.EventType != RemoteEventTypes.ToolInputRequested, Is.True);
        }

        [Test]
        public void TryMap_PlanExecutionRequested_BuildsRemotePlanPromptEvent()
        {
            var session = new ChatSession { Id = "session-1", ThreadId = "thread-1", Mode = AppInteractionMode.Coding };
            var appEvent = new AppServerEventArgs(
                "plan_execution_requested",
                "# Plan\n\n- Step 1",
                threadId: "thread-1",
                turnId: "turn-1",
                requestId: "plan-1");

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.PlanExecutionRequested));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("plan-1"));
            Assert.That(remoteEvent.Payload["planText"], Does.Contain("Step 1"));
            var actions = (IReadOnlyList<Dictionary<string, object>>)remoteEvent.Payload["actions"];
            Assert.That(actions, Has.Count.EqualTo(2));
            Assert.That(actions[0]["id"], Is.EqualTo("execute"));
            Assert.That(actions[1]["id"], Is.EqualTo("continue"));
        }

        [Test]
        public void TryMap_MismatchedThread_ReturnsFalse()
        {
            var session = new ChatSession
            {
                Id = "session-a",
                ThreadId = "thread-a",
                Mode = AppInteractionMode.Coding
            };

            RemoteRelayEvent remoteEvent;
            var mapped = CodingRemoteEventMapper.TryMap(
                new AppServerEventArgs("turn_started", "started", threadId: "thread-other"),
                session,
                out remoteEvent);

            Assert.That(mapped, Is.False);
            Assert.That(remoteEvent, Is.Null);
        }

        [Test]
        public void TryMap_OfficeThreadCompleted_ReturnsOfficeMessageCompletedEvent()
        {
            var session = new ChatSession
            {
                Id = "office-session",
                ThreadId = "thread-office",
                Mode = AppInteractionMode.Office
            };

            RemoteRelayEvent remoteEvent;
            var mapped = CodingRemoteEventMapper.TryMap(
                new AppServerEventArgs("turn_completed", "completed", threadId: "thread-office", turnId: "turn-office"),
                session,
                out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.OfficeMessageCompleted));
            Assert.That(remoteEvent.Payload["sessionId"], Is.EqualTo("office-session"));
            Assert.That(remoteEvent.Payload["status"], Is.EqualTo("completed"));
        }

        [Test]
        public void TryMap_OfficeApprovalRequested_WinFormsMapperIncludesDialogPayload()
        {
            var session = new ChatSession
            {
                Id = "office-session",
                ThreadId = "thread-office",
                Mode = AppInteractionMode.Office
            };
            var appEvent = new AppServerEventArgs(
                "approval_requested",
                "approval content",
                threadId: "thread-office",
                turnId: "turn-office",
                requestId: "approval-office",
                commandText: "Set-Content -LiteralPath office-flow-test.txt -Value \"approval flow test\"",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command,
                canApproveForSession: true);

            RemoteRelayEvent remoteEvent;
            var mapped = WinFormsCodingRemoteEventMapper.TryMap(appEvent, session, out remoteEvent);

            Assert.That(mapped, Is.True);
            Assert.That(remoteEvent.EventType, Is.EqualTo(RemoteEventTypes.ApprovalRequested));
            Assert.That(remoteEvent.Payload["sessionId"], Is.EqualTo("office-session"));
            Assert.That(remoteEvent.Payload["requestId"], Is.EqualTo("approval-office"));
            Assert.That(remoteEvent.Payload["questionText"], Does.Contain("office-flow-test.txt"));
        }
    }
}
