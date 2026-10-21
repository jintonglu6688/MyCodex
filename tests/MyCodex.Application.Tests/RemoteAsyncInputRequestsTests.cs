using System.Collections.Generic;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteAsyncInputRequestsTests
    {
        [Test]
        public void CreateRequestId_UsesItemIdAndIsRecognizedAsAsync()
        {
            var source = new AppServerEventArgs("async_input_requested", "reply", threadId: "thread-1", turnId: "turn-1", itemId: " item-1 ");

            var requestId = RemoteAsyncInputRequests.CreateRequestId(source);

            Assert.That(requestId, Is.EqualTo("async:thread-1:item-1"));
            Assert.That(RemoteAsyncInputRequests.IsAsyncRequestId(requestId), Is.True);
            Assert.That(RemoteAsyncInputRequests.IsAsyncRequestId("input-1"), Is.False);
        }

        [Test]
        public void CreateRequestId_WithoutItemIdIsStableAndDistinguishesContent()
        {
            var first = new AppServerEventArgs("async_input_requested", "reply a", threadId: "thread-1", turnId: "turn-1");
            var same = new AppServerEventArgs("async_input_requested", "reply a", threadId: "thread-1", turnId: "turn-1");
            var other = new AppServerEventArgs("async_input_requested", "reply b", threadId: "thread-1", turnId: "turn-1");

            Assert.That(RemoteAsyncInputRequests.CreateRequestId(first), Is.EqualTo(RemoteAsyncInputRequests.CreateRequestId(same)));
            Assert.That(RemoteAsyncInputRequests.CreateRequestId(first), Is.Not.EqualTo(RemoteAsyncInputRequests.CreateRequestId(other)));
            Assert.That(RemoteAsyncInputRequests.CreateRequestId(first), Does.StartWith("async:thread-1:h"));
        }

        [Test]
        public void FlattenAnswers_JoinsValuesAndDropsBlankAnswers()
        {
            var answers = RemoteAsyncInputRequests.FlattenAnswers(new Dictionary<string, string[]>
            {
                { "async-1", new[] { " SQLite " } },
                { "async-2", new[] { "A", "", "B" } },
                { "async-3", new[] { " " } }
            });

            Assert.That(answers["async-1"], Is.EqualTo("SQLite"));
            Assert.That(answers["async-2"], Is.EqualTo("A, B"));
            Assert.That(answers.ContainsKey("async-3"), Is.False);
        }
    }
}
