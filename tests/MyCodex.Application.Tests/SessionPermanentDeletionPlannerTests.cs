using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionPermanentDeletionPlannerTests
    {
        [Test]
        public void Build_ParentChildSelectionDeletesAncestorOnceAndIncludesGrandchildren()
        {
            var plan = SessionPermanentDeletionPlanner.Build(new[] { Session("a"), Session("b"), Session("a") },
                new[] { Thread("a"), Thread("b", "a"), Thread("c", "b"), Thread("unrelated") });
            Assert.That(plan.Roots.Select(thread => thread.ThreadId), Is.EqualTo(new[] { "a" }));
            Assert.That(plan.Threads.Select(thread => thread.ThreadId), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Build_OrphanCanBeSelectedWithoutInventingAnAncestor()
        {
            var plan = SessionPermanentDeletionPlanner.Build(new[] { Session("orphan") }, new[] { Thread("orphan", "absent") });
            Assert.That(plan.Roots[0].ThreadId, Is.EqualTo("orphan"));
        }

        [TestCase("active")]
        [TestCase("systemError")]
        [TestCase(null)]
        [TestCase("futureStatus")]
        public void Build_UnsafeDescendantPreventsAnyDeletion(string status)
        {
            var child = Thread("b", "a");
            child.ThreadStatus = status == null ? null : new AppServerThreadStatus { Type = status };
            Assert.Throws<InvalidOperationException>(() => SessionPermanentDeletionPlanner.Build(new[] { Session("a") }, new[] { Thread("a"), child }));
        }

        [Test]
        public void Build_MissingRootAndCyclicRelationsFailClosed()
        {
            Assert.Throws<InvalidOperationException>(() => SessionPermanentDeletionPlanner.Build(new[] { Session("missing") }, new AppServerThreadSummary[0]));
            Assert.Throws<InvalidOperationException>(() => SessionPermanentDeletionPlanner.Build(new[] { Session("a") }, new[] { Thread("a", "b"), Thread("b", "a") }));
        }

        [Test]
        public void SameScope_NewDescendantRequiresNewConfirmation()
        {
            var selected = new[] { Session("a") };
            var before = SessionPermanentDeletionPlanner.Build(selected, new[] { Thread("a") });
            var after = SessionPermanentDeletionPlanner.Build(selected, new[] { Thread("a"), Thread("b", "a") });
            Assert.That(SessionPermanentDeletionPlanner.SameScope(before, after), Is.False);
        }

        [TestCase("windows")]
        [TestCase("wsl:Ubuntu")]
        [TestCase("remote-mac:test")]
        [TestCase("remote-unix:test")]
        public async Task Execute_PartialSuccessSynchronizesOnlyConfirmedScope(string runtime)
        {
            var selected = new[] { Session("a", runtime), Session("b", runtime) };
            var plan = SessionPermanentDeletionPlanner.Build(selected, new[] { Thread("a"), Thread("child", "a"), Thread("b") });
            var calls = new List<string>();
            var synchronized = new List<string>();
            var result = await SessionPermanentDeletionPlanner.ExecuteAsync(plan, id =>
            {
                calls.Add(id);
                if (id == "b") throw new InvalidOperationException("server failed");
                return Task.CompletedTask;
            }, ids => synchronized.AddRange(ids), () => true);
            Assert.That(calls, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(synchronized, Is.EqualTo(new[] { "a", "child" }));
            Assert.That(result.DeletedThreadIds, Is.EqualTo(synchronized));
            Assert.That(result.FailedThreadIds, Is.EqualTo(new[] { "b" }));
            Assert.That(result.FailureReasons, Has.Count.EqualTo(1));
            Assert.That(result.FailureReasons["b"], Is.EqualTo("server failed"));
        }

        [Test]
        public async Task Execute_TargetChangesDuringRequestDoesNotTouchNewTargetOrSendNextRequest()
        {
            var plan = SessionPermanentDeletionPlanner.Build(new[] { Session("a"), Session("b") }, new[] { Thread("a"), Thread("b") });
            var current = true;
            var calls = new List<string>();
            var result = await SessionPermanentDeletionPlanner.ExecuteAsync(plan, id =>
            {
                calls.Add(id);
                current = false;
                return Task.CompletedTask;
            }, ids => Assert.Fail("Late response must not mutate current target."), () => current);
            Assert.That(calls, Is.EqualTo(new[] { "a" }));
            Assert.That(result.DeletedThreadIds, Is.Empty);
            Assert.That(result.FailedThreadIds, Has.Count.EqualTo(2));
            Assert.That(result.FailureReasons, Is.Empty);
        }

        [Test]
        public async Task Execute_EarlyNotificationConfirmsOnlyItsThreadWhenResponseFails()
        {
            var plan = SessionPermanentDeletionPlanner.Build(new[] { Session("a") }, new[] { Thread("a"), Thread("child", "a") });
            var result = await SessionPermanentDeletionPlanner.ExecuteAsync(plan,
                _ => throw new InvalidOperationException("disconnected"), _ => Assert.Fail("No successful response."),
                () => true, id => id == "child");
            Assert.That(result.DeletedThreadIds, Is.EqualTo(new[] { "child" }));
            Assert.That(result.FailedThreadIds, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void ConfirmedLocalRemovalDoesNotRemoveUnconfirmedDescendants()
        {
            var parent = Session("a");
            var child = Session("b");
            child.ParentThreadId = "a";
            var plan = LocalSessionRemovalPlanner.BuildSessionRemoval(parent, null, new[] { parent, child }, null, null, parent, false);
            Assert.That(plan.RetainedSessions, Is.EqualTo(new[] { child }));
            Assert.That(plan.SessionsToClear, Is.EqualTo(new[] { parent }));
        }

        [Test]
        public void CompleteScope_AncestorQueryWithMissingParentCannotSilentlyOmitDescendant()
        {
            Assert.Throws<InvalidOperationException>(() => SessionPermanentDeletionPlanner.EnsureCompleteDescendants(
                Thread("a"), new[] { Thread("orphan") }));
            Assert.DoesNotThrow(() => SessionPermanentDeletionPlanner.EnsureCompleteDescendants(
                Thread("a"), new[] { Thread("b", "a"), Thread("c", "b") }));
        }

        [Test]
        public async Task MissingDeletionRootCanBeRemovedAfterConfirmedAbsence()
        {
            var root = await SessionPermanentDeletionPlanner.ReadDeletionRootAsync(Session("missing"),
                new AppServerThreadSummary[0], id => Task.FromException<AppServerThreadDetails>(
                    new InvalidOperationException("thread not loaded: " + id)), (id, path) => Task.FromResult(true));
            Assert.That(root, Is.Null);
        }

        [TestCase("unconfirmed")]
        [TestCase("descendants")]
        [TestCase("disconnected")]
        public void UnreadableRootIsNotRemovedWithoutCompleteAbsenceEvidence(string scenario)
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionPermanentDeletionPlanner.ReadDeletionRootAsync(
                Session("missing"), scenario == "descendants" ? new[] { Thread("child", "missing") } : new AppServerThreadSummary[0],
                id => Task.FromException<AppServerThreadDetails>(new InvalidOperationException(
                    scenario == "disconnected" ? "disconnected" : "thread not loaded: " + id)),
                (id, path) => Task.FromResult(scenario != "unconfirmed")));
        }

        [Test]
        public async Task ConfirmedMissingRootIsRemovedLocallyWithoutServerDeleteAndScopeChangesAreDetected()
        {
            var plan = SessionPermanentDeletionPlanner.Build(new[] { Session("missing"), Session("normal") },
                new[] { Thread("missing"), Thread("normal") });
            plan.MissingThreadIds.Add("missing");
            var calls = new List<string>();
            var confirmed = new List<string>();
            var result = await SessionPermanentDeletionPlanner.ExecuteAsync(plan, id => { calls.Add(id); return Task.CompletedTask; },
                ids => confirmed.AddRange(ids), () => true);
            Assert.That(calls, Is.EqualTo(new[] { "normal" }));
            Assert.That(confirmed, Is.EquivalentTo(new[] { "missing", "normal" }));
            Assert.That(result.DeletedThreadIds, Is.EquivalentTo(confirmed));
            var restored = SessionPermanentDeletionPlanner.Build(new[] { Session("missing"), Session("normal") }, plan.Threads);
            Assert.That(SessionPermanentDeletionPlanner.SameScope(plan, restored), Is.False);
        }

        private static ChatSession Session(string id, string runtime = "windows")
        {
            return new ChatSession { Id = id, ThreadId = id, RuntimeProfileId = runtime };
        }

        private static AppServerThreadSummary Thread(string id, string parent = null)
        {
            return new AppServerThreadSummary { ThreadId = id, ParentThreadId = parent, ThreadStatus = new AppServerThreadStatus { Type = "idle" } };
        }
    }
}
