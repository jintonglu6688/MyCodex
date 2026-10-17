using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitCheckoutPreflightPlannerTests
    {
        private static IEnumerable<TestCaseData> Cases
        {
            get
            {
                yield return new TestCaseData(null, GitCheckoutPreflightDecision.Proceed)
                    .SetName("Null status proceeds");
                yield return Case("Non-repository proceeds", status => status.IsGitRepository = false, GitCheckoutPreflightDecision.Proceed);
                yield return Case("Clean repository proceeds", null, GitCheckoutPreflightDecision.Proceed);
                yield return Case("Dirty repository prompts", status => status.Files.Add(new GitChangedFileEntry()), GitCheckoutPreflightDecision.PromptDirtyChanges);
                yield return Case("Conflicts block", status => status.HasConflicts = true, GitCheckoutPreflightDecision.BlockConflicts);
                yield return Case("Cherry-pick blocks", status => status.IsCherryPickInProgress = true, GitCheckoutPreflightDecision.BlockActiveGitOperation);
                yield return Case("Revert blocks", status => status.IsRevertInProgress = true, GitCheckoutPreflightDecision.BlockActiveGitOperation);
                yield return Case("Merge blocks", status => status.IsMergeInProgress = true, GitCheckoutPreflightDecision.BlockActiveGitOperation);
                yield return Case("Rebase blocks", status => status.IsRebaseInProgress = true, GitCheckoutPreflightDecision.BlockActiveGitOperation);
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void Evaluate_ReturnsExpectedDecision(
            GitRepositoryStatusSnapshot status,
            GitCheckoutPreflightDecision expected)
        {
            Assert.That(GitCheckoutPreflightPlanner.Evaluate(status), Is.EqualTo(expected));
        }

        private static TestCaseData Case(
            string name,
            System.Action<GitRepositoryStatusSnapshot> arrange,
            GitCheckoutPreflightDecision expected)
        {
            var status = new GitRepositoryStatusSnapshot { IsGitRepository = true };
            if (arrange != null)
            {
                arrange(status);
            }

            return new TestCaseData(status, expected).SetName(name);
        }
    }
}
