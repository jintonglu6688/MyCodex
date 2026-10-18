using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionListProjectionTests
    {
        [Test]
        public void DeepDescendantsInOtherWorkspaceContributeToCountAndBusyState()
        {
            var sessions = new[]
            {
                new ChatSession { Id = "root", ThreadId = "root", WorkspaceId = "workspace" },
                new ChatSession { Id = "grandchild", ThreadId = "grandchild", ParentThreadId = "child", WorkspaceId = "other" },
                new ChatSession { Id = "child", ThreadId = "child", ParentThreadId = "root", WorkspaceId = "other" }
            };
            var entries = SessionListProjection.BuildWorkspaceEntries(sessions, "workspace", AppInteractionMode.Coding);
            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].SubAgentCount, Is.EqualTo(2));
            Assert.That(SessionListProjection.IsWorking(entries[0], id => id == "grandchild"), Is.True);
        }

        [Test]
        public void BuildMainEntries_HidesSubAgentsAndCountsChildren()
        {
            var mainSession = new ChatSession
            {
                Id = "main-session",
                ThreadId = "parent-thread",
                Title = "BoyRemote"
            };
            var sessions = new List<ChatSession>
            {
                mainSession,
                new ChatSession
                {
                    Id = "child-1",
                    ThreadId = "child-thread-1",
                    Title = "Child 1",
                    IsSubAgent = true,
                    ParentThreadId = "parent-thread"
                },
                new ChatSession
                {
                    Id = "child-2",
                    ThreadId = "child-thread-2",
                    Title = "Child 2",
                    IsSubAgent = true,
                    ParentThreadId = "parent-thread"
                }
            };

            var entries = SessionListProjection.BuildMainEntries(sessions).ToArray();

            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Session, Is.SameAs(mainSession));
            Assert.That(entries[0].SubAgentCount, Is.EqualTo(2));
            Assert.That(entries[0].SubAgents.Select(session => session.Id), Is.EqualTo(new[] { "child-1", "child-2" }));
        }

        [Test]
        public void BuildMainEntries_KeepsSubAgentsWithoutParentReachable()
        {
            var mainSession = new ChatSession
            {
                Id = "main-session",
                ThreadId = "parent-thread",
                Title = "BoyRemote"
            };
            var sessions = new List<ChatSession>
            {
                mainSession,
                new ChatSession
                {
                    Id = "child-1",
                    ThreadId = "child-thread-1",
                    Title = "Child 1",
                    ThreadSource = "subagent"
                }
            };

            var entries = SessionListProjection.BuildMainEntries(sessions).ToArray();

            Assert.That(entries, Has.Length.EqualTo(2));
            Assert.That(entries[0].Session, Is.SameAs(mainSession));
            Assert.That(entries[0].SubAgentCount, Is.EqualTo(0));
            Assert.That(entries[1].Session.Id, Is.EqualTo("child-1"));
        }

        [Test]
        public void BuildMainEntries_ShowsOrphanSubAgentsSoTheyCanBeRemoved()
        {
            var orphanSubAgent = new ChatSession
            {
                Id = "orphan-child",
                ThreadId = "child-thread",
                Title = "History session",
                IsSubAgent = true,
                ParentThreadId = "missing-parent-thread",
                SubAgentNickname = "Ampere"
            };

            var entries = SessionListProjection.BuildMainEntries(new[] { orphanSubAgent }).ToArray();

            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Session, Is.SameAs(orphanSubAgent));
            Assert.That(entries[0].SubAgentCount, Is.EqualTo(0));
        }

        [Test]
        public void BuildWorkspaceEntries_CountsSubAgentsFromOtherWorkspacesWhenParentIsInWorkspace()
        {
            var parent = new ChatSession
            {
                Id = "parent-session",
                ThreadId = "parent-thread",
                WorkspaceId = "workspace-parent",
                Mode = AppInteractionMode.Coding,
                Title = "Parent"
            };
            var siblingWorkspaceSubAgent = new ChatSession
            {
                Id = "child-session",
                ThreadId = "child-thread",
                WorkspaceId = "workspace-child",
                Mode = AppInteractionMode.Coding,
                Title = "Child",
                IsSubAgent = true,
                ParentThreadId = "parent-thread"
            };
            var unrelated = new ChatSession
            {
                Id = "other-session",
                ThreadId = "other-thread",
                WorkspaceId = "workspace-child",
                Mode = AppInteractionMode.Coding,
                Title = "Other"
            };

            var entries = SessionListProjection.BuildWorkspaceEntries(
                new[] { parent, siblingWorkspaceSubAgent, unrelated },
                "workspace-parent",
                AppInteractionMode.Coding).ToArray();

            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Session, Is.SameAs(parent));
            Assert.That(entries[0].SubAgentCount, Is.EqualTo(1));
            Assert.That(entries[0].SubAgents.Select(session => session.Id), Is.EqualTo(new[] { "child-session" }));
        }

        [Test]
        public void WorkingState_RollsBusySubAgentsUpToOneParentEntry()
        {
            var parent = new ChatSession { Id = "parent", ThreadId = "parent-thread" };
            var child1 = new ChatSession { Id = "child-1", ThreadId = "child-thread-1", ParentThreadId = "parent-thread", IsSubAgent = true };
            var child2 = new ChatSession { Id = "child-2", ThreadId = "child-thread-2", ParentThreadId = "parent-thread", IsSubAgent = true };
            var other = new ChatSession { Id = "other", ThreadId = "other-thread" };
            var entries = SessionListProjection.BuildMainEntries(new[] { parent, child1, child2, other });
            var busyThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "child-thread-1",
                "child-thread-2",
                "other-thread"
            };

            Assert.That(SessionListProjection.IsWorking(entries[0], busyThreadIds.Contains), Is.True);
            Assert.That(SessionListProjection.IsWorking(entries[1], busyThreadIds.Contains), Is.True);
            Assert.That(SessionListProjection.CountWorkingEntries(entries, busyThreadIds.Contains), Is.EqualTo(2));
        }

        [Test]
        public void WorkingState_IgnoresUnknownAndMissingThreadIds()
        {
            var entries = SessionListProjection.BuildMainEntries(new[]
            {
                new ChatSession { Id = "missing-thread" },
                new ChatSession { Id = "known", ThreadId = "known-thread" }
            });

            Assert.That(SessionListProjection.CountWorkingEntries(entries, threadId => threadId == "unknown-thread"), Is.Zero);
            Assert.That(SessionListProjection.IsWorking(null, threadId => true), Is.False);
            Assert.That(SessionListProjection.CountWorkingEntries(null, threadId => true), Is.Zero);
        }
    }
}
