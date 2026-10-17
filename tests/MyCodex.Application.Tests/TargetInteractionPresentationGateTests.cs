using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class TargetInteractionPresentationGateTests
    {
        [Test]
        public void BackgroundRequestRemainsPendingUntilItsTargetCanPresent()
        {
            var foreground = false;
            var modalOpen = false;
            var dialogs = new RemoteInteractionDialogRegistry();
            using (var lifetime = new Form())
            {
                var wait = TargetInteractionPresentationGate.WaitAsync(lifetime, dialogs, "request",
                    () => foreground && !modalOpen, () => true);
                Assert.That(wait.IsCompleted, Is.False);
                Assert.That(dialogs.HasPending, Is.True);
                foreground = true;
                modalOpen = true;
                PumpFor(150);
                Assert.That(wait.IsCompleted, Is.False, "Do not nest a background request over another modal operation.");
                modalOpen = false;
                PumpUntil(wait);
                Assert.That(wait.Result, Is.True);
                Assert.That(dialogs.HasPending, Is.False);
            }
        }

        [Test]
        public void RemoteResolutionCompletesDeferredApprovalAndSkipsResolvedQueuedRequest()
        {
            var queue = new ApprovalRequestDialogQueue();
            var dialogs = new RemoteInteractionDialogRegistry();
            queue.Enqueue(Request("one"), out _);
            queue.Enqueue(Request("two"), out _);
            using (var lifetime = new Form())
            {
                var wait = TargetInteractionPresentationGate.WaitAsync(lifetime, dialogs, "one", () => false, () => true);
                queue.MarkResolved("two");
                queue.MarkResolved("one");
                Assert.That(dialogs.Complete("one"), Is.True);
                PumpUntil(wait);
                Assert.That(wait.Result, Is.False);
                Assert.That(queue.CompleteCurrent(out _), Is.False);
                Assert.That(queue.HasPending, Is.False);
                Assert.That(dialogs.HasPending, Is.False);
            }
        }

        [TestCase("disconnect")]
        [TestCase("dispose")]
        [TestCase("close-all")]
        public void DeferredRequestsCancelWhenTheirLifecycleEnds(string ending)
        {
            var current = true;
            var dialogs = new RemoteInteractionDialogRegistry();
            using (var lifetime = new Form())
            {
                var wait = TargetInteractionPresentationGate.WaitAsync(lifetime, dialogs, "request", () => false, () => current);
                if (ending == "dispose") lifetime.Dispose();
                else if (ending == "close-all") dialogs.CloseAll();
                else current = false;
                PumpUntil(wait);
                Assert.That(wait.Result, Is.False);
                Assert.That(dialogs.HasPending, Is.False);
            }
        }

        private static AppServerEventArgs Request(string id) => new AppServerEventArgs("approval_requested", "approval", requestId: id);

        private static void PumpUntil(Task task)
        {
            var timer = Stopwatch.StartNew();
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 2000)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.That(task.IsCompleted, Is.True);
        }

        private static void PumpFor(int milliseconds)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
        }
    }
}
