using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class LocalSubAgentThreadSyncPlannerTests
    {
        [Test]
        public void FilterCurrentParentSubAgents_ReturnsExistingAndNewDescendantsWithoutDirectoryRequirement()
        {
            var currentChild = Thread("child-thread", "parent-thread");
            var knownChild = Thread("known-child", "parent-thread");
            var otherParentChild = Thread("other-child", "other-parent");
            var regularThread = new AppServerThreadSummary
            {
                ThreadId = "regular-thread",
                WorkingDirectory = @"E:\MyCodex"
            };

            var result = LocalSubAgentThreadSyncPlanner.FilterCurrentParentSubAgents(
                new[] { currentChild, knownChild, otherParentChild, regularThread },
                "parent-thread",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "child-thread", "known-child" }));
        }

        [Test]
        public void DiscoveryIncludesDeepChildrenAndIgnoresCyclesAndHelperSubtrees()
        {
            var child = Thread("child", "root");
            child.WorkingDirectory = null;
            var result = LocalSubAgentThreadSyncPlanner.FilterCurrentParentSubAgents(
                new[] { Thread("grandchild", "child"), child, Thread("root", "grandchild"), Thread("helper", "root"), Thread("hidden", "helper") },
                "root", new HashSet<string> { "helper" });
            Assert.That(result.Select(thread => thread.ThreadId), Is.EquivalentTo(new[] { "child", "grandchild" }));
        }

        private static AppServerThreadSummary Thread(string threadId, string parentThreadId)
        {
            return new AppServerThreadSummary
            {
                ThreadId = threadId,
                ParentThreadId = parentThreadId,
                ThreadSource = "subagent",
                WorkingDirectory = @"E:\MyCodex"
            };
        }
    }
}
