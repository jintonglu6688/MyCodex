using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerEventDiagnosticsTests
    {
        [TestCase("turn/started")]
        [TestCase("item/reasoning/textDelta")]
        [TestCase("agentMessage/delta")]
        [TestCase("error")]
        public void ShouldTraceNotification_ReturnsTrueForHighSignalMethods(string method)
        {
            Assert.That(AppServerEventDiagnostics.ShouldTraceNotification(method), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("hook/started")]
        [TestCase("process/exited")]
        public void ShouldTraceNotification_ReturnsFalseForLowSignalMethods(string method)
        {
            Assert.That(AppServerEventDiagnostics.ShouldTraceNotification(method), Is.False);
        }

        [TestCase("turn_started")]
        [TestCase("assistant_message_delta")]
        [TestCase("reasoning_summary_delta")]
        [TestCase("error")]
        public void ShouldTraceMappedEvent_ReturnsTrueForHighSignalEvents(string eventType)
        {
            Assert.That(AppServerEventDiagnostics.ShouldTraceMappedEvent(eventType), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("rate_limits_changed")]
        public void ShouldTraceMappedEvent_ReturnsFalseForLowSignalEvents(string eventType)
        {
            Assert.That(AppServerEventDiagnostics.ShouldTraceMappedEvent(eventType), Is.False);
        }

        [Test]
        public void BuildSummary_IncludesStableKeysAndLengthsWithoutContent()
        {
            var parameters = new Dictionary<string, object>
            {
                ["threadId"] = "thread-1",
                ["turnId"] = "turn-1",
                ["itemId"] = "item-1",
                ["delta"] = "hello\r\nworld",
                ["summaryIndex"] = 2,
                ["item"] = new Dictionary<string, object>
                {
                    ["id"] = "nested-1",
                    ["type"] = "reasoning",
                    ["summary"] = "secret summary"
                }
            };

            var summary = AppServerEventDiagnostics.BuildSummary(parameters);

            Assert.That(summary, Does.Contain("keys=delta,item,itemId,summaryIndex,threadId,turnId"));
            Assert.That(summary, Does.Contain("threadId=thread-1"));
            Assert.That(summary, Does.Contain("turnId=turn-1"));
            Assert.That(summary, Does.Contain("itemId=item-1"));
            Assert.That(summary, Does.Contain("summaryIndex=2"));
            Assert.That(summary, Does.Contain("deltaLength=12"));
            Assert.That(summary, Does.Contain("item.id=nested-1"));
            Assert.That(summary, Does.Contain("item.type=reasoning"));
            Assert.That(summary, Does.Contain("item.summaryLength=14"));
            Assert.That(summary, Does.Not.Contain("hello"));
            Assert.That(summary, Does.Not.Contain("world"));
            Assert.That(summary, Does.Not.Contain("secret summary"));
        }

        [Test]
        public void BuildMappedEventSummary_UsesLengthsWithoutContent()
        {
            var summary = AppServerEventDiagnostics.BuildMappedEventSummary(new AppServerEventArgs(
                "assistant_message_delta",
                "secret content",
                details: "secret details",
                threadId: "thread-1",
                turnId: "turn-1",
                itemId: "item-1"));

            Assert.That(summary, Is.EqualTo("EventType=assistant_message_delta; ThreadId=thread-1; TurnId=turn-1; ItemId=item-1; ContentLength=14; DetailsLength=14"));
            Assert.That(summary, Does.Not.Contain("secret content"));
            Assert.That(summary, Does.Not.Contain("secret details"));
        }
    }
}
