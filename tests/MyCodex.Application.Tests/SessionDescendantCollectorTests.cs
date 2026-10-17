using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionDescendantCollectorTests
    {
        [Test]
        public void Collect_NullRootReturnsEmptyResult()
        {
            Assert.That(SessionDescendantCollector.Collect(null, new ChatSession[0]), Is.Empty);
        }

        [Test]
        public void Collect_RootWithoutCandidatesReturnsOnlyRoot()
        {
            var root = Session("root", "root-thread", null);

            Assert.That(SessionDescendantCollector.Collect(root, null), Is.EqualTo(new[] { root }));
        }

        [Test]
        public void Collect_OutOfOrderTreeUsesRootFirstDiscoveryOrderAndIgnoresMissingParents()
        {
            var root = Session("root", "ROOT-THREAD", null);
            var grandchild = Session("grandchild", "grandchild-thread", "CHILD-THREAD");
            var child = Session("child", "child-thread", "root-thread");
            var sibling = Session("sibling", "sibling-thread", "ROOT-THREAD");
            var missingParent = Session("missing", "missing-thread", "unknown-thread");

            var result = SessionDescendantCollector.Collect(
                root,
                new[] { grandchild, child, sibling, missingParent });

            Assert.That(result, Is.EqualTo(new[] { root, child, sibling, grandchild }));
        }

        [Test]
        public void Collect_ForwardOrderedTreePreservesLegacyFirstDiscoveryOrder()
        {
            var root = Session("root", "root-thread", null);
            var child = Session("child", "child-thread", "root-thread");
            var grandchild = Session("grandchild", "grandchild-thread", "child-thread");
            var sibling = Session("sibling", "sibling-thread", "root-thread");

            var result = SessionDescendantCollector.Collect(root, new[] { child, grandchild, sibling });

            Assert.That(result, Is.EqualTo(new[] { root, child, grandchild, sibling }));
        }

        [Test]
        public void Collect_DeduplicatesIdsAndRepeatedBlankIdSessions()
        {
            var root = Session("root", "root-thread", null);
            var firstDuplicate = Session("child", "child-thread", "root-thread");
            var secondDuplicate = Session("CHILD", "ignored-thread", "root-thread");
            var blankId = Session(null, "blank-thread", "root-thread");
            var repeatedBlankId = Session(null, "BLANK-THREAD", "root-thread");
            var grandchild = Session("grandchild", "grandchild-thread", "CHILD-THREAD");

            var result = SessionDescendantCollector.Collect(
                root,
                new[] { firstDuplicate, secondDuplicate, blankId, blankId, repeatedBlankId, grandchild });

            Assert.That(result, Is.EqualTo(new[] { root, firstDuplicate, blankId, grandchild }));
        }

        [Test]
        public void Collect_CycleDoesNotAddRootAgain()
        {
            var root = Session("root", "root-thread", "child-thread");
            var child = Session("child", "child-thread", "root-thread");

            var result = SessionDescendantCollector.Collect(root, new[] { child, root });

            Assert.That(result, Is.EqualTo(new[] { root, child }));
        }

        private static ChatSession Session(string id, string threadId, string parentThreadId)
        {
            return new ChatSession
            {
                Id = id,
                ThreadId = threadId,
                ParentThreadId = parentThreadId
            };
        }
    }
}
