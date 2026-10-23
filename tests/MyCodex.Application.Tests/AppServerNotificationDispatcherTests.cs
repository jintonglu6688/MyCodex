using NUnit.Framework;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerNotificationDispatcherTests
    {
        [TestCase("hook/started")]
        [TestCase("hook/completed")]
        [TestCase("app/list/updated")]
        [TestCase("process/outputDelta")]
        [TestCase("process/exited")]
        [TestCase("rawResponseItem/completed")]
        [TestCase("remoteControl/status/changed")]
        [TestCase("thread/realtime/started")]
        [TestCase("thread/realtime/delta")]
        [TestCase("thread/realtime/completed")]
        public void IsLowNoiseCompatibilityNotification_KnownCompatibilityMethod_ReturnsTrue(string method)
        {
            Assert.That(AppServerNotificationDispatcher.IsLowNoiseCompatibilityNotification(method), Is.True);
            Assert.That(AppServerNotificationDispatcher.Classify(method), Is.EqualTo(AppServerNotificationAction.Ignore));
        }

        [TestCase("thread/started")]
        [TestCase("item/agentMessage/delta")]
        [TestCase("error")]
        public void IsLowNoiseCompatibilityNotification_RegularNotification_ReturnsFalse(string method)
        {
            Assert.That(AppServerNotificationDispatcher.IsLowNoiseCompatibilityNotification(method), Is.False);
        }

        [TestCase("thread/started", "ThreadStarted")]
        [TestCase("thread/deleted", "ThreadDeleted")]
        [TestCase("thread/name/updated", "ThreadNameUpdated")]
        [TestCase("turn/started", "TurnStarted")]
        [TestCase("item/agentMessage/delta", "AgentMessageDelta")]
        [TestCase("item/fileChange/patchUpdated", "FileChangePatchUpdated")]
        [TestCase("item/autoApprovalReview/started", "AutoApprovalReviewStarted")]
        [TestCase("mcpServer/startupStatus/updated", "McpServerStartupStatusUpdated")]
        [TestCase("turn/moderationMetadata", "TurnModerationMetadata")]
        [TestCase("configWarning", "ConfigWarning")]
        [TestCase("serverRequest/resolved", "ServerRequestResolved")]
        [TestCase("error", "Error")]
        public void Classify_KnownMethod_ReturnsStableAction(string method, string expectedActionName)
        {
            Assert.That(AppServerNotificationDispatcher.Classify(method).ToString(), Is.EqualTo(expectedActionName));
        }

        [TestCase("thread/settings/updated")]
        [TestCase("thread/readState/changed")]
        public void Classify_QuietKnownMethod_ReturnsIgnore(string method)
        {
            Assert.That(AppServerNotificationDispatcher.Classify(method), Is.EqualTo(AppServerNotificationAction.Ignore));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("runtime/newNotice")]
        public void Classify_UnknownOrEmptyMethod_ReturnsUnhandledWithoutThrowing(string method)
        {
            Assert.That(AppServerNotificationDispatcher.IsLowNoiseCompatibilityNotification(method), Is.False);
            Assert.That(AppServerNotificationDispatcher.Classify(method), Is.EqualTo(AppServerNotificationAction.Unhandled));
        }
    }
}
