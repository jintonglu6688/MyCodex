using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SendMessageShortcutIntegrationSourceTests
    {
        [Test]
        public void MainFormAndSubAgentWorkbenchUseConfiguredSendMessageShortcutMode()
        {
            var mainFormSource = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Forms", "MainForm.cs"));
            var workbenchSource = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Controls", "ChatWorkbenchControl.cs"));

            Assert.That(mainFormSource, Does.Contain("SendMessageShortcutPlanner.ResolveAction(GetConfiguredSendMessageShortcutMode(), e.KeyCode, e.Control)"));
            Assert.That(mainFormSource, Does.Contain("GetSendMessageShortcutMode = GetConfiguredSendMessageShortcutMode"));
            Assert.That(workbenchSource, Does.Contain("SendMessageShortcutPlanner.ResolveAction(GetSendMessageShortcutMode(), e.KeyCode, e.Control)"));
        }

        private static string GetPath(params string[] parts)
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                Path.Combine(parts)));
        }
    }
}
