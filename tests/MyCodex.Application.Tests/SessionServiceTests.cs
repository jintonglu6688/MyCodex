using System;
using NUnit.Framework;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class SessionServiceTests
    {
        [Test]
        public void CreateSession_UsesTrimmedExplicitTitle()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-1", "Demo");

            var session = service.CreateSession(workspace, 3, "  Custom Title  ");

            Assert.That(session.WorkspaceId, Is.EqualTo("workspace-1"));
            Assert.That(session.Title, Is.EqualTo("Custom Title"));
            Assert.That(session.ThreadId, Is.EqualTo(string.Empty));
            Assert.That(session.IsImported, Is.False);
        }

        [Test]
        public void CreateSession_UsesDefaultTitleWhenExplicitTitleIsMissing()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-2", "Demo");

            var session = service.CreateSession(workspace, 5, " ");

            Assert.That(session.Title, Is.EqualTo("Demo"));
            Assert.That(session.Mode, Is.EqualTo(AppInteractionMode.Coding));
        }

        [Test]
        public void CreateSession_MarksOfficeSessionsWhenWorkspaceIsOffice()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("office-workspace", "Office");
            workspace.Kind = WorkspaceKind.Office;

            var session = service.CreateSession(workspace, 1);

            Assert.That(session.WorkspaceId, Is.EqualTo("office-workspace"));
            Assert.That(session.Title, Is.EqualTo("Office"));
            Assert.That(session.Mode, Is.EqualTo(AppInteractionMode.Office));
        }

        [Test]
        public void CreateImportedSession_FallsBackToTrimmedPreviewWhenTitleIsMissing()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-3", "Demo");
            var threadSummary = new AppServerThreadSummary
            {
                ThreadId = "thread-1",
                Preview = "  This is a preview that is definitely longer than thirty characters.  ",
                Source = "codex",
                SessionPath = @"C:\threads\thread-1.jsonl"
            };

            var session = service.CreateImportedSession(workspace, threadSummary);

            Assert.That(session.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(session.IsImported, Is.True);
            Assert.That(session.ThreadSource, Is.EqualTo("codex"));
            Assert.That(session.SessionPath, Is.EqualTo(@"C:\threads\thread-1.jsonl"));
            Assert.That(session.Title, Is.EqualTo("This is a preview that is defi..."));
        }

        [Test]
        public void CreateImportedSession_PreservesSubAgentMetadata()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-subagent", "Demo");
            var threadSummary = new AppServerThreadSummary
            {
                ThreadId = "child-thread",
                Title = "Child Agent",
                ThreadSource = "subagent",
                ParentThreadId = "parent-thread",
                SubagentDepth = 1,
                AgentNickname = "Ampere",
                AgentRole = "default"
            };

            var session = service.CreateImportedSession(workspace, threadSummary);

            Assert.That(session.IsSubAgent, Is.True);
            Assert.That(session.ParentThreadId, Is.EqualTo("parent-thread"));
            Assert.That(session.SubAgentDepth, Is.EqualTo(1));
            Assert.That(session.SubAgentNickname, Is.EqualTo("Ampere"));
            Assert.That(session.SubAgentRole, Is.EqualTo("default"));
        }

        [Test]
        public void CreateImportedSession_PreservesThreadSourceWhenDifferentFromDisplaySource()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-subagent-source", "Demo");
            var threadSummary = new AppServerThreadSummary
            {
                ThreadId = "child-thread",
                Title = "Child Agent",
                Source = "Subagent",
                ThreadSource = "subagent"
            };

            var session = service.CreateImportedSession(workspace, threadSummary);

            Assert.That(session.ThreadSource, Is.EqualTo("subagent"));
            Assert.That(session.IsSubAgent, Is.True);
        }

        [Test]
        public void CreateForkSession_PreservesForkMetadata()
        {
            var service = new SessionService();
            var workspace = CreateWorkspace("workspace-4", "Demo");
            var sourceSession = new ChatSession
            {
                ThreadId = "source-thread",
                Title = "Source Session"
            };
            var threadSummary = new AppServerThreadSummary
            {
                ThreadId = "fork-thread",
                Preview = "Fork preview"
            };

            var session = service.CreateForkSession(
                workspace,
                sourceSession,
                threadSummary,
                "  Fork Session  ",
                2,
                ApprovalMode.Auto,
                "gpt-5.4-mini",
                "high");

            Assert.That(session.Title, Is.EqualTo("Fork Session"));
            Assert.That(session.IsFork, Is.True);
            Assert.That(session.ForkSourceThreadId, Is.EqualTo("source-thread"));
            Assert.That(session.ForkSourceTitle, Is.EqualTo("Source Session"));
            Assert.That(session.ForkIndex, Is.EqualTo(2));
            Assert.That(session.ApprovalMode, Is.EqualTo(ApprovalMode.Auto));
            Assert.That(session.ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(session.ReasoningEffort, Is.EqualTo("high"));
        }

        private static WorkspaceInfo CreateWorkspace(string id, string name)
        {
            return new WorkspaceInfo
            {
                Id = id,
                Name = name,
                RootPath = @"C:\workspace"
            };
        }
    }
}
