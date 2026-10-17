using System.Windows.Forms;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SendMessageShortcutPlannerTests
    {
        [TestCase(SendMessageShortcutMode.EnterNewLineCtrlEnterSend, Keys.Enter, false, SendMessageShortcutAction.NewLine)]
        [TestCase(SendMessageShortcutMode.EnterNewLineCtrlEnterSend, Keys.Enter, true, SendMessageShortcutAction.Send)]
        [TestCase(SendMessageShortcutMode.CtrlEnterNewLineEnterSend, Keys.Enter, false, SendMessageShortcutAction.Send)]
        [TestCase(SendMessageShortcutMode.CtrlEnterNewLineEnterSend, Keys.Enter, true, SendMessageShortcutAction.NewLine)]
        [TestCase(SendMessageShortcutMode.CtrlEnterNewLineEnterSend, Keys.A, false, SendMessageShortcutAction.None)]
        public void ResolveAction_MapsEnterAndCtrlEnterByConfiguredMode(
            string mode,
            Keys keyCode,
            bool control,
            SendMessageShortcutAction expectedAction)
        {
            Assert.That(SendMessageShortcutPlanner.ResolveAction(mode, keyCode, control), Is.EqualTo(expectedAction));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        public void ResolveAction_FallsBackToLegacyDefaultMode(string mode)
        {
            Assert.That(
                SendMessageShortcutPlanner.ResolveAction(mode, Keys.Enter, false),
                Is.EqualTo(SendMessageShortcutAction.NewLine));
            Assert.That(
                SendMessageShortcutPlanner.ResolveAction(mode, Keys.Enter, true),
                Is.EqualTo(SendMessageShortcutAction.Send));
        }
    }
}
