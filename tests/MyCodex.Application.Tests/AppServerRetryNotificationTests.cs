using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerRetryNotificationTests
    {
        [TestCase(true, "turn_retrying")]
        [TestCase(false, "error")]
        public void ErrorNotification_PreservesRuntimeRetryDecision(bool willRetry, string expectedType)
        {
            var events = new List<AppServerEventArgs>();
            var processor = new AppServerNotificationEventProcessor(new AppServerNotificationEventContext(
                events.Add, _ => { }, _ => { }, (_, __) => { }, _ => { }, path => path, _ => { }));
            processor.HandleNotification("error", new Dictionary<string, object>
            {
                { "threadId", "thread-1" },
                { "turnId", "turn-1" },
                { "willRetry", willRetry },
                { "error", new Dictionary<string, object>
                    {
                        { "message", "Model overloaded" },
                        { "codexErrorInfo", "serverOverloaded" }
                    }
                }
            });

            Assert.That(events, Has.Count.EqualTo(1));
            var notification = events[0];
            Assert.That(notification.EventType, Is.EqualTo(expectedType));
            Assert.That(notification.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(notification.TurnId, Is.EqualTo("turn-1"));
            Assert.That(notification.Reason, Is.EqualTo("serverOverloaded"));
            Assert.That(notification.Content, Does.Contain("Model overloaded"));
            var route = AppServerEventRouting.Resolve(notification);
            Assert.That(AppServerEventRouting.IsTurnTerminal(route), Is.False);
            Assert.That(AppServerEventRouting.IsErrorDisplayEvent(route), Is.EqualTo(!willRetry));
            if (willRetry)
                Assert.That(AppServerEventRouting.GetFallbackCategory(route), Is.EqualTo(ChatHistoryCategory.Processing));
        }
    }
}
