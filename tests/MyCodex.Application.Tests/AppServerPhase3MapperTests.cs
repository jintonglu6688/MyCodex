using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPhase3MapperTests
    {
        [Test]
        public void ResourceMapper_ParsesTextAndBlobContents()
        {
            var result = AppServerMcpResourceMapper.BuildReadResult(new Dictionary<string, object>
            {
                {
                    "contents",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "uri", "file:///a.md" },
                            { "mimeType", "text/markdown" },
                            { "text", "# A" }
                        },
                        new Dictionary<string, object>
                        {
                            { "uri", "file:///b.bin" },
                            { "mimeType", "application/octet-stream" },
                            { "blob", "AAECAw==" }
                        }
                    }
                }
            });

            Assert.That(result.Contents, Has.Count.EqualTo(2));
            Assert.That(result.Contents[0].Text, Is.EqualTo("# A"));
            Assert.That(result.Contents[1].IsBlob, Is.True);
        }

        [Test]
        public void ToolCallMapper_BuildsDisplayTextFromContentAndStructuredContent()
        {
            var result = AppServerMcpToolCallMapper.BuildResult(new Dictionary<string, object>
            {
                { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", "done" } } } },
                { "structuredContent", new Dictionary<string, object> { { "count", 2 } } },
                { "isError", false }
            });

            Assert.That(result.IsError, Is.False);
            Assert.That(result.DisplayText, Does.Contain("done"));
            Assert.That(result.StructuredContentJson, Does.Contain("count"));
        }

        [Test]
        public void GuardianReviewMapper_SummarizesCommandAction()
        {
            var result = AppServerGuardianApprovalReviewMapper.BuildStartedEvent(new Dictionary<string, object>
            {
                { "threadId", "thread-1" },
                { "turnId", "turn-1" },
                { "startedAtMs", 123L },
                { "reviewId", "review-1" },
                { "targetItemId", "item-1" },
                { "review", new Dictionary<string, object> { { "status", "inProgress" }, { "riskLevel", "medium" }, { "rationale", "needs review" } } },
                { "action", new Dictionary<string, object> { { "type", "command" }, { "command", "npm test" }, { "cwd", "/repo" } } }
            });

            Assert.That(result.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(result.Action.ActionType, Is.EqualTo("command"));
            Assert.That(result.Action.Details, Does.Contain("npm test"));
        }

        [Test]
        public void ModerationMapper_ProducesSanitizedSummary()
        {
            var result = AppServerModerationMetadataMapper.BuildEvent(new Dictionary<string, object>
            {
                { "threadId", "thread-1" },
                { "turnId", "turn-1" },
                { "metadata", new Dictionary<string, object> { { "category", "policy" }, { "decision", "blocked" }, { "secret", "do-not-copy" } } }
            });

            Assert.That(result.Summary, Does.Contain("blocked"));
            Assert.That(result.Details, Does.Not.Contain("do-not-copy"));
        }

        [Test]
        public void GitDiffToRemoteMapper_ParsesShaAndDiff()
        {
            var result = AppServerGitDiffToRemoteMapper.BuildResult(new Dictionary<string, object>
            {
                { "sha", "abc123" },
                { "diff", "diff --git a/a b/a" }
            });

            Assert.That(result.Sha, Is.EqualTo("abc123"));
            Assert.That(result.Diff, Does.StartWith("diff --git"));
        }
    }
}
