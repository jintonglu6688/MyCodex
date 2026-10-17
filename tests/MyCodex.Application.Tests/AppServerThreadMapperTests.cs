using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerThreadMapperTests
    {
        [Test]
        public void BuildThreadSummary_MapsCoreThreadFields()
        {
            var createdAt = 1710000000L;
            var updatedAt = 1710000123L;
            var thread = new Dictionary<string, object>
            {
                { "id", "thread-1" },
                { "name", "Demo Thread" },
                { "preview", "preview" },
                { "modelProvider", "mycodex_resp_702928a3f99f" },
                { "cwd", "/mnt/e/Projects/demo" },
                { "source", "official" },
                { "createdAt", createdAt },
                { "updatedAt", updatedAt },
                { "path", "/sessions/thread-1.jsonl" }
            };

            var summary = AppServerThreadMapper.BuildThreadSummary(thread, MapServerPathToClient);

            Assert.That(summary.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(summary.Title, Is.EqualTo("Demo Thread"));
            Assert.That(summary.Preview, Is.EqualTo("preview"));
            Assert.That(summary.ModelProviderId, Is.EqualTo("mycodex_resp_702928a3f99f"));
            Assert.That(summary.WorkingDirectory, Is.EqualTo(@"E:\Projects\demo"));
            Assert.That(summary.Source, Is.EqualTo("official"));
            Assert.That(summary.CreatedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(createdAt).LocalDateTime));
            Assert.That(summary.UpdatedAt, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(updatedAt).LocalDateTime));
            Assert.That(summary.SessionPath, Is.EqualTo(@"E:\sessions\thread-1.jsonl"));
        }

        [Test]
        public void BuildThreadSummary_MapsSubAgentMetadata()
        {
            var thread = new Dictionary<string, object>
            {
                { "id", "child-thread" },
                { "name", "Child Agent" },
                { "cwd", "/mnt/e/Projects/demo" },
                { "thread_source", "subagent" },
                { "agent_nickname", "Ampere" },
                { "agent_role", "default" },
                {
                    "source",
                    new Dictionary<string, object>
                    {
                        {
                            "subagent",
                            new Dictionary<string, object>
                            {
                                {
                                    "thread_spawn",
                                    new Dictionary<string, object>
                                    {
                                        { "parent_thread_id", "parent-thread" },
                                        { "depth", 1 }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var summary = AppServerThreadMapper.BuildThreadSummary(thread, MapServerPathToClient);

            Assert.That(summary.ThreadSource, Is.EqualTo("subagent"));
            Assert.That(summary.ParentThreadId, Is.EqualTo("parent-thread"));
            Assert.That(summary.SubagentDepth, Is.EqualTo(1));
            Assert.That(summary.AgentNickname, Is.EqualTo("Ampere"));
            Assert.That(summary.AgentRole, Is.EqualTo("default"));
        }

        [Test]
        public void BuildThreadDetails_FlattensTurnsAndAttachmentSummaries()
        {
            WithCulture("en-US", () =>
            {
                var thread = new Dictionary<string, object>
                {
                    { "id", "thread-1" },
                    { "name", "Demo Thread" },
                    { "cwd", @"E:\Projects\demo" },
                    {
                        "turns",
                        new object[]
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "turn-1" },
                                {
                                    "items",
                                    new object[]
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "type", "userMessage" },
                                            {
                                                "content",
                                                new object[]
                                                {
                                                    new Dictionary<string, object>
                                                    {
                                                        { "type", "text" },
                                                        { "text", "hello" }
                                                    },
                                                    new Dictionary<string, object>
                                                    {
                                                        { "type", "localImage" },
                                                        { "path", @"E:\Projects\demo\pic.jpg" }
                                                    },
                                                    new Dictionary<string, object>
                                                    {
                                                        { "type", "image" },
                                                        { "url", "https://example.com/cat.png" }
                                                    },
                                                    new Dictionary<string, object>
                                                    {
                                                        { "type", "mention" },
                                                        { "path", @"E:\Projects\demo\file.cs" }
                                                    }
                                                }
                                            }
                                        },
                                        new Dictionary<string, object>
                                        {
                                            { "type", "agentMessage" },
                                            { "text", "world" }
                                        }
                                    }
                                }
                            },
                            new Dictionary<string, object>
                            {
                                { "id", "turn-2" },
                                {
                                    "items",
                                    new object[]
                                    {
                                        new Dictionary<string, object>
                                        {
                                            { "type", "enteredReviewMode" },
                                            { "review", "Check the diff" }
                                        },
                                        new Dictionary<string, object>
                                        {
                                            { "type", "exitedReviewMode" },
                                            { "review", "Done" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var details = AppServerThreadMapper.BuildThreadDetails(thread, MapServerPathToClient);

                Assert.That(details.TurnCount, Is.EqualTo(2));
                Assert.That(details.Messages, Has.Count.EqualTo(4));
                Assert.That(details.Messages[0].Sender, Is.EqualTo("User"));
                Assert.That(
                    details.Messages[0].Content,
                    Is.EqualTo(
                        "hello" + Environment.NewLine +
                        "Attached 2 images: pic.jpg, cat.png" + Environment.NewLine +
                        "Attached 1 file: file.cs"));
                Assert.That(details.Messages[1].Sender, Is.EqualTo("Codex"));
                Assert.That(details.Messages[1].Content, Is.EqualTo("world"));
                Assert.That(details.Messages[2].Content, Is.EqualTo("Entered review mode: Check the diff"));
                Assert.That(details.Messages[3].Content, Is.EqualTo("Exited review mode: Done"));
            });
        }

        [Test]
        public void BuildThreadDetails_SkipsPlaceholderOnlyAgentMessages()
        {
            var thread = new Dictionary<string, object>
            {
                { "id", "thread-1" },
                { "name", "Demo Thread" },
                {
                    "turns",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "id", "turn-1" },
                            {
                                "items",
                                new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "type", "agentMessage" },
                                        { "text", "..." }
                                    },
                                    new Dictionary<string, object>
                                    {
                                        { "type", "agentMessage" },
                                        { "text", "正常回复" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var details = AppServerThreadMapper.BuildThreadDetails(thread, MapServerPathToClient);

            Assert.That(details.Messages, Has.Count.EqualTo(1));
            Assert.That(details.Messages[0].Sender, Is.EqualTo("Codex"));
            Assert.That(details.Messages[0].Content, Is.EqualTo("正常回复"));
        }

        private static string MapServerPathToClient(string path)
        {
            if (string.Equals(path, "/mnt/e/Projects/demo", StringComparison.Ordinal))
            {
                return @"E:\Projects\demo";
            }

            if (string.Equals(path, "/sessions/thread-1.jsonl", StringComparison.Ordinal))
            {
                return @"E:\sessions\thread-1.jsonl";
            }

            return path;
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        }
    }
}
