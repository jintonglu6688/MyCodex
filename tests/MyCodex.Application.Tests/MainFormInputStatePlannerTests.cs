using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormInputStatePlannerTests
    {
        [Test]
        public void Build_DisablesCompositionWhenWorkspaceUnavailable()
        {
            var plan = MainFormInputStatePlanner.Build(
                workspaceAvailable: false,
                hasCurrentSession: true,
                isThreadCompacting: false,
                canInterruptThread: true,
                historyItemCount: 1,
                pendingAttachmentCount: 1);

            Assert.That(plan.CanCompose, Is.False);
            Assert.That(plan.CanInteract, Is.False);
            Assert.That(plan.CanStop, Is.False);
            Assert.That(plan.ChatHistoryEnabled, Is.True);
            Assert.That(plan.InputEnabled, Is.False);
            Assert.That(plan.PendingAttachmentListEnabled, Is.False);
        }

        [Test]
        public void Build_EnablesCompositionAndStopWhenReady()
        {
            var plan = MainFormInputStatePlanner.Build(
                workspaceAvailable: true,
                hasCurrentSession: true,
                isThreadCompacting: false,
                canInterruptThread: true,
                historyItemCount: 0,
                pendingAttachmentCount: 2);

            Assert.That(plan.CanCompose, Is.True);
            Assert.That(plan.CanInteract, Is.True);
            Assert.That(plan.CanStop, Is.True);
            Assert.That(plan.ChatHistoryEnabled, Is.True);
            Assert.That(plan.InputEnabled, Is.True);
            Assert.That(plan.AttachmentButtonEnabled, Is.True);
            Assert.That(plan.SendButtonEnabled, Is.True);
            Assert.That(plan.PendingAttachmentListEnabled, Is.True);
        }

        [Test]
        public void Build_DisablesStopWithoutSessionOrInterrupt()
        {
            var noSession = MainFormInputStatePlanner.Build(true, false, false, true, 0, 0);
            var cannotInterrupt = MainFormInputStatePlanner.Build(true, true, false, false, 0, 0);

            Assert.That(noSession.CanStop, Is.False);
            Assert.That(cannotInterrupt.CanStop, Is.False);
        }
    }
}
