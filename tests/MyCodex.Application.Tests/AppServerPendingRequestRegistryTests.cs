using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPendingRequestRegistryTests
    {
        [Test]
        public void RegisterApproval_StoresAndRemoves()
        {
            var registry = new AppServerPendingRequestRegistry();
            var request = new AppServerPendingApprovalRequest
            {
                RequestId = "approval-1",
                ThreadId = "thread-1"
            };

            registry.RegisterApproval("approval-1", request);

            AppServerPendingApprovalRequest loaded;
            Assert.That(registry.TryGetApproval("approval-1", out loaded), Is.True);
            Assert.That(loaded.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(registry.RemoveApproval("approval-1"), Is.True);
            Assert.That(registry.TryGetApproval("approval-1", out loaded), Is.False);
        }

        [Test]
        public void RegisterToolInput_StoresAndRemoves()
        {
            var registry = new AppServerPendingRequestRegistry();
            var request = new AppServerPendingToolUserInputRequest
            {
                RequestId = "tool-1",
                ThreadId = "thread-1"
            };

            registry.RegisterToolInput("tool-1", request);

            AppServerPendingToolUserInputRequest loaded;
            Assert.That(registry.TryGetToolInput("tool-1", out loaded), Is.True);
            Assert.That(loaded.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(registry.RemoveToolInput("tool-1"), Is.True);
            Assert.That(registry.TryGetToolInput("tool-1", out loaded), Is.False);
        }

        [Test]
        public void TryTakeApproval_ReturnsRequestOnlyOnce()
        {
            var registry = new AppServerPendingRequestRegistry();
            var request = new AppServerPendingApprovalRequest { RequestId = "approval-1" };
            registry.RegisterApproval("approval-1", request);

            AppServerPendingApprovalRequest taken;
            Assert.That(registry.TryTakeApproval("approval-1", out taken), Is.True);
            Assert.That(taken, Is.SameAs(request));
            Assert.That(registry.TryTakeApproval("approval-1", out taken), Is.False);
        }

        [Test]
        public void TryTakeToolInput_ReturnsRequestOnlyOnce()
        {
            var registry = new AppServerPendingRequestRegistry();
            var request = new AppServerPendingToolUserInputRequest { RequestId = "tool-1" };
            registry.RegisterToolInput("tool-1", request);

            AppServerPendingToolUserInputRequest taken;
            Assert.That(registry.TryTakeToolInput("tool-1", out taken), Is.True);
            Assert.That(taken, Is.SameAs(request));
            Assert.That(registry.TryTakeToolInput("tool-1", out taken), Is.False);
        }
    }
}
