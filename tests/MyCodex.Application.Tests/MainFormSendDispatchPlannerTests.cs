using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormSendDispatchPlannerTests
    {
        [Test]
        public void Choose_UsesSteerForRunningOrdinaryMessage()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = true,
                InputItemCount = 1
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnSteer));
        }

        [Test]
        public void Choose_UsesTurnStartWhenNoActiveTurn()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = false,
                InputItemCount = 1
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnStart));
        }

        [Test]
        public void Choose_QueuesRunningMessageWhenGlobalQueueSettingIsEnabled()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = true,
                ActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Queue,
                InputItemCount = 1
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.Queue));
            Assert.That(decision.Reason, Is.EqualTo("follow-up-queue"));
        }

        [Test]
        public void Choose_StartsImmediatelyWhenQueueSettingIsEnabledButThreadIsIdle()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = false,
                ActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Queue,
                InputItemCount = 1
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnStart));
        }

        [Test]
        public void Choose_UsesTurnStartForPlanCommand()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = true,
                InputItemCount = 1,
                IsPlanCommand = true
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnStart));
        }

        [Test]
        public void Choose_UsesTurnStartWhenSpecialCollaborationPayloadMustBeSent()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = true,
                InputItemCount = 1,
                HasSpecialCollaborationPayload = true
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnStart));
        }

        [Test]
        public void Choose_UsesTurnStartWhenThreadWasRecreated()
        {
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = "thread-1",
                IsThreadRunning = true,
                InputItemCount = 1,
                ThreadWasRecreatedDuringSend = true
            });

            Assert.That(decision.Mode, Is.EqualTo(MainFormSendDispatchMode.TurnStart));
        }

        [TestCase("target", "target", true)]
        [TestCase("TARGET", "target", true)]
        [TestCase("target", "other", false)]
        [TestCase("target", null, false)]
        [TestCase(null, "target", false)]
        public void ShouldApplyVisibleSessionUi_RequiresTheCapturedTargetSession(
            string targetSessionId,
            string visibleSessionId,
            bool expected)
        {
            Assert.That(
                MainFormSendDispatchPlanner.ShouldApplyVisibleSessionUi(targetSessionId, visibleSessionId),
                Is.EqualTo(expected));
        }
    }
}
