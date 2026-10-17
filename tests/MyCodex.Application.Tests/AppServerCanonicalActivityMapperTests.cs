using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCanonicalActivityMapperTests
    {
        [TestCase(false, "image_generation_started")]
        [TestCase(true, "image_generation_completed")]
        public void Map_ImageGenerationMapsSafeSummaryAndClientPath(bool completed, string expectedEventType)
        {
            var item = new Dictionary<string, object>
            {
                { "type", "imageGeneration" },
                { "id", "image-1" },
                { "status", completed ? "completed" : "inProgress" },
                { "revisedPrompt", "A concise revised prompt" },
                { "savedPath", "/server/output/image.png" },
                { "result", "data:image/png;base64,SECRET_IMAGE_PAYLOAD" }
            };

            var result = AppServerCanonicalActivityMapper.Map(
                item,
                completed,
                "thread-1",
                "turn-1",
                path => "client:" + path);

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.EventType, Is.EqualTo(expectedEventType));
                Assert.That(result.Content, Does.Contain("A concise revised prompt"));
                Assert.That(result.Content, Does.Not.Contain("SECRET_IMAGE_PAYLOAD"));
                Assert.That(result.Details, Is.EqualTo("client:/server/output/image.png"));
                Assert.That(result.Details, Does.Not.Contain("SECRET_IMAGE_PAYLOAD"));
                Assert.That(result.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(result.TurnId, Is.EqualTo("turn-1"));
                Assert.That(result.ItemId, Is.EqualTo("image-1"));
                Assert.That(result.ItemStatus, Is.EqualTo(completed ? "completed" : "inProgress"));
            });
        }

        [TestCase("started")]
        [TestCase("interacted")]
        [TestCase("interrupted")]
        public void Map_CompletedSubAgentActivityPreservesKindAndAgentPath(string kind)
        {
            var item = new Dictionary<string, object>
            {
                { "type", "subAgentActivity" },
                { "id", "activity-1" },
                { "kind", kind },
                { "agentThreadId", "agent-thread-1" },
                { "agentPath", "reviewer" }
            };

            var result = AppServerCanonicalActivityMapper.Map(
                item,
                true,
                "thread-1",
                "turn-1",
                path => path);

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result.EventType, Is.EqualTo("subagent_activity"));
                Assert.That(result.Content, Does.Contain("reviewer"));
                Assert.That(result.Reason, Is.EqualTo(kind));
                Assert.That(result.ItemStatus, Is.EqualTo(kind));
            });
        }

        [Test]
        public void Map_SubAgentActivityFallsBackToAgentThreadId()
        {
            var item = new Dictionary<string, object>
            {
                { "type", "subAgentActivity" },
                { "id", "activity-1" },
                { "kind", "started" },
                { "agentThreadId", "agent-thread-1" }
            };

            var result = AppServerCanonicalActivityMapper.Map(
                item,
                true,
                "thread-1",
                "turn-1",
                path => path);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Does.Contain("agent-thread-1"));
        }

        [Test]
        public void Map_HookPromptIsIgnored()
        {
            var item = new Dictionary<string, object>
            {
                { "type", "hookPrompt" },
                { "id", "hook-1" }
            };

            Assert.That(
                AppServerCanonicalActivityMapper.Map(item, true, "thread-1", "turn-1", path => path),
                Is.Null);
        }
    }
}
