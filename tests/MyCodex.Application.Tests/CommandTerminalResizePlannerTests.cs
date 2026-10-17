using System;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CommandTerminalResizePlannerTests
    {
        [Test]
        public void Plan_DeduplicatesThrottlesAndDisablesProcess()
        {
            var now = DateTime.UtcNow;
            var planner = new CommandTerminalResizePlanner(TimeSpan.FromMilliseconds(250));

            var first = planner.Plan("proc-1", new AppServerCommandTerminalSize(24, 100), now);
            Assert.That(first.ShouldSend, Is.True);
            Assert.That(first.ProcessId, Is.EqualTo("proc-1"));
            Assert.That(first.Size.Rows, Is.EqualTo(24));
            Assert.That(first.Size.Cols, Is.EqualTo(100));

            var duplicate = planner.Plan("proc-1", new AppServerCommandTerminalSize(24, 100), now.AddMilliseconds(50));
            Assert.That(duplicate.ShouldSend, Is.False);

            var changedTooSoon = planner.Plan("proc-1", new AppServerCommandTerminalSize(25, 100), now.AddMilliseconds(100));
            Assert.That(changedTooSoon.ShouldSend, Is.False);

            var changedAfterThrottle = planner.Plan("proc-1", new AppServerCommandTerminalSize(25, 100), now.AddMilliseconds(300));
            Assert.That(changedAfterThrottle.ShouldSend, Is.True);

            planner.DisableForProcess("proc-1");
            var disabled = planner.Plan("proc-1", new AppServerCommandTerminalSize(26, 100), now.AddMilliseconds(600));
            Assert.That(disabled.ShouldSend, Is.False);
        }
    }
}
