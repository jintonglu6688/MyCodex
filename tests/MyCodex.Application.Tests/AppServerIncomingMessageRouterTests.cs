using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerIncomingMessageRouterTests
    {
        [Test]
        public void Classify_NullMessage_ReturnsIgnored()
        {
            Assert.That(AppServerIncomingMessageRouter.Classify(null), Is.EqualTo(AppServerIncomingMessageKind.Ignored));
        }

        [Test]
        public void Classify_EmptyMessage_ReturnsIgnored()
        {
            Assert.That(
                AppServerIncomingMessageRouter.Classify(new Dictionary<string, object>()),
                Is.EqualTo(AppServerIncomingMessageKind.Ignored));
        }

        [Test]
        public void Classify_MethodAndId_ReturnsServerRequest()
        {
            var message = new Dictionary<string, object>
            {
                { "method", "item/request" },
                { "id", "request-1" }
            };

            Assert.That(AppServerIncomingMessageRouter.Classify(message), Is.EqualTo(AppServerIncomingMessageKind.ServerRequest));
        }

        [Test]
        public void Classify_MethodOnly_ReturnsNotification()
        {
            var message = new Dictionary<string, object>
            {
                { "method", "thread/started" }
            };

            Assert.That(AppServerIncomingMessageRouter.Classify(message), Is.EqualTo(AppServerIncomingMessageKind.Notification));
        }

        [Test]
        public void Classify_IdOnly_ReturnsResponse()
        {
            var message = new Dictionary<string, object>
            {
                { "id", "request-1" }
            };

            Assert.That(AppServerIncomingMessageRouter.Classify(message), Is.EqualTo(AppServerIncomingMessageKind.Response));
        }

        [Test]
        public void Classify_BlankMethodAndBlankId_ReturnsIgnored()
        {
            var message = new Dictionary<string, object>
            {
                { "method", " " },
                { "id", string.Empty }
            };

            Assert.That(AppServerIncomingMessageRouter.Classify(message), Is.EqualTo(AppServerIncomingMessageKind.Ignored));
        }

        [Test]
        public void Classify_NonStringMethodWithId_ReturnsResponse()
        {
            var message = new Dictionary<string, object>
            {
                { "method", 123 },
                { "id", "request-1" }
            };

            Assert.That(AppServerIncomingMessageRouter.Classify(message), Is.EqualTo(AppServerIncomingMessageKind.Response));
        }
    }
}
