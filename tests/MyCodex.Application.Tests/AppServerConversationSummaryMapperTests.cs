using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerConversationSummaryMapperTests
    {
        [Test]
        public void BuildSummary_MapsOfficialConversationSummaryFields()
        {
            var summary = AppServerConversationSummaryMapper.BuildSummary(
                SummaryDictionary(),
                path => path);

            Assert.That(summary.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(summary.Path, Is.EqualTo("/sessions/thread-1.jsonl"));
            Assert.That(summary.Preview, Is.EqualTo("Preview text"));
            Assert.That(summary.Timestamp, Is.EqualTo(DateTimeOffset.Parse("2026-06-18T01:02:03Z").LocalDateTime));
            Assert.That(summary.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-06-18T02:03:04Z").LocalDateTime));
            Assert.That(summary.ModelProviderId, Is.EqualTo("openai"));
            Assert.That(summary.WorkingDirectory, Is.EqualTo("/workspace/demo"));
            Assert.That(summary.CliVersion, Is.EqualTo("0.140.0"));
            Assert.That(summary.Source, Is.EqualTo("rollout"));
            Assert.That(summary.GitInfo, Is.Not.Null);
            Assert.That(summary.GitInfo.Sha, Is.EqualTo("abc123"));
            Assert.That(summary.GitInfo.Branch, Is.EqualTo("main"));
            Assert.That(summary.GitInfo.OriginUrl, Is.EqualTo("https://example.com/repo.git"));
        }

        [Test]
        public void BuildSummary_MapsServerPathsThroughCallback()
        {
            var summary = AppServerConversationSummaryMapper.BuildSummary(
                SummaryDictionary(),
                path => string.Equals(path, "/workspace/demo", StringComparison.Ordinal)
                    ? @"E:\workspace\demo"
                    : (string.Equals(path, "/sessions/thread-1.jsonl", StringComparison.Ordinal)
                        ? @"E:\sessions\thread-1.jsonl"
                        : path));

            Assert.That(summary.Path, Is.EqualTo(@"E:\sessions\thread-1.jsonl"));
            Assert.That(summary.WorkingDirectory, Is.EqualTo(@"E:\workspace\demo"));
        }

        [Test]
        public void ToThreadSummary_MergesOfficialPreviewWithoutReplacingLocalAssociation()
        {
            var existing = new MyCodex.Application.DTOs.AppServerThreadSummary
            {
                ThreadId = "thread-1",
                Title = "Local title",
                Preview = string.Empty,
                SessionPath = @"E:\sessions\local.jsonl",
                WorkingDirectory = @"E:\workspace\local",
                CreatedAt = new DateTime(2026, 6, 17, 1, 0, 0),
                UpdatedAt = new DateTime(2026, 6, 17, 1, 0, 0)
            };
            var official = AppServerConversationSummaryMapper.BuildSummary(SummaryDictionary(), path => path);

            var merged = AppServerConversationSummaryMapper.ToThreadSummary(official, existing);

            Assert.That(merged.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(merged.Title, Is.EqualTo("Local title"));
            Assert.That(merged.Preview, Is.EqualTo("Preview text"));
            Assert.That(merged.SessionPath, Is.EqualTo(@"E:\sessions\local.jsonl"));
            Assert.That(merged.WorkingDirectory, Is.EqualTo(@"E:\workspace\local"));
            Assert.That(merged.ModelProviderId, Is.EqualTo("openai"));
            Assert.That(merged.UpdatedAt, Is.EqualTo(DateTimeOffset.Parse("2026-06-18T02:03:04Z").LocalDateTime));
        }

        private static IDictionary<string, object> SummaryDictionary()
        {
            return new Dictionary<string, object>
            {
                { "conversationId", "thread-1" },
                { "path", "/sessions/thread-1.jsonl" },
                { "preview", "Preview text" },
                { "timestamp", "2026-06-18T01:02:03Z" },
                { "updatedAt", "2026-06-18T02:03:04Z" },
                { "modelProvider", "openai" },
                { "cwd", "/workspace/demo" },
                { "cliVersion", "0.140.0" },
                { "source", "rollout" },
                { "gitInfo", new Dictionary<string, object>
                    {
                        { "sha", "abc123" },
                        { "branch", "main" },
                        { "origin_url", "https://example.com/repo.git" }
                    }
                }
            };
        }
    }
}
