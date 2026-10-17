using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ThreadFollowUpQueueTests
    {
        [Test]
        public void Queue_DispatchesOneItemAtATimeInFirstInFirstOutOrder()
        {
            var queue = new ThreadFollowUpQueue<string>();
            queue.Enqueue("thread-1", "first");
            queue.Enqueue("thread-1", "second");

            string item;
            Assert.That(queue.TryBeginDispatch("thread-1", out item), Is.True);
            Assert.That(item, Is.EqualTo("first"));
            Assert.That(queue.TryBeginDispatch("thread-1", out item), Is.False);

            queue.CompleteDispatch("thread-1");

            Assert.That(queue.TryBeginDispatch("thread-1", out item), Is.True);
            Assert.That(item, Is.EqualTo("second"));
        }

        [Test]
        public void MovePending_MovesRemainingItemsToRecreatedThread()
        {
            var queue = new ThreadFollowUpQueue<string>();
            queue.Enqueue("old-thread", "first");
            queue.Enqueue("old-thread", "second");
            queue.Enqueue("new-thread", "later");

            string item;
            Assert.That(queue.TryBeginDispatch("old-thread", out item), Is.True);
            queue.MovePending("old-thread", "new-thread");
            queue.CompleteDispatch("old-thread");

            Assert.That(queue.TryBeginDispatch("old-thread", out item), Is.False);
            Assert.That(queue.TryBeginDispatch("new-thread", out item), Is.True);
            Assert.That(item, Is.EqualTo("second"));
            Assert.That(queue.HasPending("new-thread"), Is.True);
            queue.CompleteDispatch("new-thread");
            Assert.That(queue.TryBeginDispatch("new-thread", out item), Is.True);
            Assert.That(item, Is.EqualTo("later"));
        }
    }
}
