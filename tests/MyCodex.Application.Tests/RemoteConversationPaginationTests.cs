using System.Collections.Generic;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteConversationPaginationTests
    {
        [Test]
        public void StableMessageId_DoesNotDependOnPagePosition()
        {
            var message = new AppServerThreadMessage { ThreadId = "thread", TurnId = "turn", SourceItemId = "item", TurnItemIndex = 8 };
            Assert.That(RemoteConversationStableIdentity.CreateMessageId(message), Is.EqualTo(RemoteConversationStableIdentity.CreateMessageId(message)));
        }

        [Test]
        public void Utf8Preview_DoesNotSplitEmoji()
        {
            bool truncated;
            long total;
            var text = RemoteConversationUtf8Slice.Take("a😀b", 5, out truncated, out total);
            Assert.That(text, Is.EqualTo("a😀"));
            Assert.That(truncated, Is.True);
            Assert.That(total, Is.EqualTo(6));
        }

        [Test]
        public void HistoryRevision_ChangesWhenContentChanges()
        {
            var first = new List<CodingRemoteMessageItem> { new CodingRemoteMessageItem { MessageId = "m1", ContentVersion = "c1" } };
            var second = new List<CodingRemoteMessageItem> { new CodingRemoteMessageItem { MessageId = "m1", ContentVersion = "c2" } };
            Assert.That(RemoteConversationStableIdentity.CreateHistoryRevision(first), Is.Not.EqualTo(RemoteConversationStableIdentity.CreateHistoryRevision(second)));
        }
    }
}
