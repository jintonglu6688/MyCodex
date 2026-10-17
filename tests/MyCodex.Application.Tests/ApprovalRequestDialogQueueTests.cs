using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ApprovalRequestDialogQueueTests
    {
        [Test]
        public void Enqueue_WhileRequestIsActive_DefersNextRequestUntilCurrentCompletes()
        {
            var queue = new ApprovalRequestDialogQueue();

            AppServerEventArgs nextRequest;
            Assert.That(queue.Enqueue(CreateRequest("approval-1"), out nextRequest), Is.True);
            Assert.That(nextRequest.RequestId, Is.EqualTo("approval-1"));

            Assert.That(queue.Enqueue(CreateRequest("approval-2"), out nextRequest), Is.False);
            Assert.That(nextRequest, Is.Null);

            Assert.That(queue.CompleteCurrent(out nextRequest), Is.True);
            Assert.That(nextRequest.RequestId, Is.EqualTo("approval-2"));

            Assert.That(queue.CompleteCurrent(out nextRequest), Is.False);
            Assert.That(nextRequest, Is.Null);
        }

        [Test]
        public void CompleteCurrent_SkipsQueuedRequestThatWasResolvedExternally()
        {
            var queue = new ApprovalRequestDialogQueue();

            AppServerEventArgs nextRequest;
            Assert.That(queue.Enqueue(CreateRequest("approval-1"), out nextRequest), Is.True);
            Assert.That(queue.Enqueue(CreateRequest("approval-2"), out nextRequest), Is.False);

            queue.MarkResolved("approval-2");

            Assert.That(queue.CompleteCurrent(out nextRequest), Is.False);
            Assert.That(nextRequest, Is.Null);
        }

        [Test]
        public void CompleteCurrent_AfterActiveRequestIsResolvedExternally_ShowsNextQueuedRequest()
        {
            var queue = new ApprovalRequestDialogQueue();

            AppServerEventArgs nextRequest;
            Assert.That(queue.Enqueue(CreateRequest("approval-1"), out nextRequest), Is.True);
            Assert.That(queue.Enqueue(CreateRequest("approval-2"), out nextRequest), Is.False);

            queue.MarkResolved("approval-1");

            Assert.That(queue.CompleteCurrent(out nextRequest), Is.True);
            Assert.That(nextRequest.RequestId, Is.EqualTo("approval-2"));
        }

        private static AppServerEventArgs CreateRequest(string requestId)
        {
            return new AppServerEventArgs("approval_requested", "approval", requestId: requestId);
        }
    }
}
