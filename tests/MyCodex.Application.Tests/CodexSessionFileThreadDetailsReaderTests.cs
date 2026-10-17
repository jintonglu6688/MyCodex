using System;
using System.IO;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodexSessionFileThreadDetailsReaderTests
    {
        [TestCase(1, new[] { "keep", "answer", "new" })]
        [TestCase(99, new[] { "new" })]
        [TestCase(0, new[] { "keep", "answer", "remove", "steer", "answer", "new" })]
        public void Read_OldMarkerDropsWholeExplicitTurnsIncludingSteering(int count, string[] expected)
        {
            var details = Read(new[]
            {
                Start("one"), Message("user", "keep"), Message("assistant", "answer"),
                Start("two"), Message("user", "remove"), Message("user", "steer"), Message("assistant", "answer"),
                Marker(count), Start("three"), Message("user", "new")
            });
            Assert.That(details.Messages.Select(message => message.Content), Is.EqualTo(expected));
            Assert.That(details.Messages.Last().TurnId, Is.EqualTo("three"));
        }

        [Test]
        public void Read_LegacyWithoutTurnEventsDropsUserTurnAndAllItsReplies()
        {
            var details = Read(new[]
            {
                Message("user", "keep"), Message("assistant", "answer"),
                Message("user", "remove"), Message("assistant", "first reply"), Message("assistant", "second reply"),
                Marker(1), Message("user", "new")
            });
            Assert.That(details.Messages.Select(message => message.Content), Is.EqualTo(new[] { "keep", "answer", "new" }));
            Assert.That(details.TurnCount, Is.EqualTo(2));
        }

        private static AppServerThreadDetails Read(string[] lines)
        {
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                File.WriteAllLines(path, lines);
                AppServerThreadDetails details;
                Assert.That(new CodexSessionFileThreadDetailsReader().TryRead(new ChatSession
                    { ThreadId = "thread", SessionPath = path }, out details), Is.True);
                return details;
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string Start(string id)
        {
            return "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"" + id + "\"}}";
        }

        private static string Marker(int count)
        {
            return "{\"type\":\"event_msg\",\"payload\":{\"type\":\"thread_rolled_back\",\"num_turns\":" + count + "}}";
        }

        private static string Message(string role, string text)
        {
            return "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"" + role + "\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + text + "\"}]}}";
        }
    }
}
