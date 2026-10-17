using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormAutomationNavigationPlannerTests
    {
        [Test]
        public void CanOpenThread_RequiresThreadId()
        {
            Assert.That(MainFormAutomationNavigationPlanner.CanOpenThread(null), Is.False);
            Assert.That(MainFormAutomationNavigationPlanner.CanOpenThread(new AutomationRunSnapshot()), Is.False);
            Assert.That(MainFormAutomationNavigationPlanner.CanOpenThread(new AutomationRunSnapshot { ThreadId = "thread-1" }), Is.True);
        }

        [Test]
        public void BuildPathDecision_ReturnsMissingForBlankPath()
        {
            var decision = MainFormAutomationNavigationPlanner.BuildPathDecision("  ", path => true, path => true);

            Assert.That(decision.Kind, Is.EqualTo(MainFormAutomationNavigationDecisionKind.MissingPath));
            Assert.That(decision.Path, Is.EqualTo(string.Empty));
        }

        [TestCase("/home/user/file.txt")]
        [TestCase("   /repo/file.txt")]
        public void BuildPathDecision_ReturnsRemoteUnsupportedForUnixPaths(string path)
        {
            var decision = MainFormAutomationNavigationPlanner.BuildPathDecision(path, item => true, item => true);

            Assert.That(decision.Kind, Is.EqualTo(MainFormAutomationNavigationDecisionKind.RemotePathUnsupported));
            Assert.That(decision.Path, Is.EqualTo(path.Trim()));
        }

        [Test]
        public void BuildPathDecision_ReturnsOpenLocalPathWhenFileOrDirectoryExists()
        {
            var fileDecision = MainFormAutomationNavigationPlanner.BuildPathDecision(@"E:\Repo\a.txt", path => path.EndsWith("a.txt"), path => false);
            var directoryDecision = MainFormAutomationNavigationPlanner.BuildPathDecision(@"E:\Repo", path => false, path => path.EndsWith("Repo"));

            Assert.That(fileDecision.Kind, Is.EqualTo(MainFormAutomationNavigationDecisionKind.OpenLocalPath));
            Assert.That(fileDecision.Path, Is.EqualTo(@"E:\Repo\a.txt"));
            Assert.That(directoryDecision.Kind, Is.EqualTo(MainFormAutomationNavigationDecisionKind.OpenLocalPath));
            Assert.That(directoryDecision.Path, Is.EqualTo(@"E:\Repo"));
        }

        [Test]
        public void BuildPathDecision_ReturnsNotFoundWhenLocalPathDoesNotExist()
        {
            var decision = MainFormAutomationNavigationPlanner.BuildPathDecision(@"E:\Repo\missing.txt", path => false, path => false);

            Assert.That(decision.Kind, Is.EqualTo(MainFormAutomationNavigationDecisionKind.PathNotFound));
            Assert.That(decision.Path, Is.EqualTo(@"E:\Repo\missing.txt"));
        }
    }
}
