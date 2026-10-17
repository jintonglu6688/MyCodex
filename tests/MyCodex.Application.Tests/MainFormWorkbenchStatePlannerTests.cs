using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormWorkbenchStatePlannerTests
    {
        [Test]
        public void ResolveAutomationTerminalStatus_MapsCompletedRouteToCompleted()
        {
            var status = MainFormWorkbenchStatePlanner.ResolveAutomationTerminalStatus(AppServerEventRoute.TurnCompleted);

            Assert.That(status, Is.EqualTo(AutomationRunStatus.Completed));
        }

        [Test]
        public void ResolveAutomationTerminalStatus_MapsInterruptedRouteToCanceled()
        {
            var status = MainFormWorkbenchStatePlanner.ResolveAutomationTerminalStatus(AppServerEventRoute.TurnInterrupted);

            Assert.That(status, Is.EqualTo(AutomationRunStatus.Canceled));
        }

        [Test]
        public void ResolveAutomationTerminalStatus_MapsFailedRouteToFailed()
        {
            var status = MainFormWorkbenchStatePlanner.ResolveAutomationTerminalStatus(AppServerEventRoute.TurnFailed);

            Assert.That(status, Is.EqualTo(AutomationRunStatus.Failed));
        }

        [Test]
        public void ResolveAutomationTerminalStatus_MapsNonTerminalFallbackRouteToFailed()
        {
            var status = MainFormWorkbenchStatePlanner.ResolveAutomationTerminalStatus(AppServerEventRoute.Default);

            Assert.That(status, Is.EqualTo(AutomationRunStatus.Failed));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("   ", false)]
        [TestCase("thread-1", true)]
        public void ShouldEnsureTurnSummaryState_RequiresNonBlankThreadId(string threadId, bool expected)
        {
            Assert.That(MainFormWorkbenchStatePlanner.ShouldEnsureTurnSummaryState(threadId), Is.EqualTo(expected));
        }

        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void ShouldRefreshAutomationForm_RequiresAutomationUpdateAndAvailableForm(
            bool automationUpdated,
            bool automationsFormAvailable,
            bool expected)
        {
            Assert.That(
                MainFormWorkbenchStatePlanner.ShouldRefreshAutomationForm(automationUpdated, automationsFormAvailable),
                Is.EqualTo(expected));
        }
    }
}
