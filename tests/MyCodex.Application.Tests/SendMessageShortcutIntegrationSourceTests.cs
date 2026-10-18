using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SendMessageShortcutIntegrationSourceTests
    {
        [Test]
        public void MainFormAndSubAgentWorkbenchUseConfiguredSendMessageShortcutMode()
        {
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var workbenchSource = RepositorySource.ReadSourceFile("ChatWorkbenchControl.cs");

            Assert.That(mainFormSource, Does.Contain("SendMessageShortcutPlanner.ResolveAction(GetConfiguredSendMessageShortcutMode(), e.KeyCode, e.Control)"));
            Assert.That(mainFormSource, Does.Contain("GetSendMessageShortcutMode = GetConfiguredSendMessageShortcutMode"));
            Assert.That(workbenchSource, Does.Contain("SendMessageShortcutPlanner.ResolveAction(GetSendMessageShortcutMode(), e.KeyCode, e.Control)"));
        }
    }
}
