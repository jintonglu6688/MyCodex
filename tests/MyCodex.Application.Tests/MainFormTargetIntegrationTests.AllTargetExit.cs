using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [Test]
        public void AllTargetExitHandoffFailureKeepsOwnerFrozenUntilPreparationIsCancelled()
        {
            using (var fixture = new TargetFormFixture())
            using (var timer = new Timer { Interval = 20 })
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var elapsed = Stopwatch.StartNew();
                var clicked = false;
                var timedOut = false;
                var callbackReached = false;
                var ownerWasFrozen = false;
                var preparationWasReady = false;
                timer.Tick += (sender, args) =>
                {
                    var dialog = FormTestDriver.Field<TargetExitAllForm>(fixture.Form, "_allTargetExitForm");
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        timedOut = true;
                        timer.Stop();
                        // Only cancel the isolated fixture's dialog; never interact with another application window.
                        if (dialog != null && !dialog.IsDisposed)
                        {
                            FormTestDriver.Field<Button>(dialog, "_cancel").PerformClick();
                            if (!dialog.IsDisposed) dialog.Dispose();
                        }
                        return;
                    }
                    if (clicked || dialog == null || !dialog.Visible || !FormTestDriver.Field<Button>(dialog, "_exit").Enabled) return;
                    clicked = true;
                    FormTestDriver.Field<Button>(dialog, "_exit").PerformClick();
                };
                Func<Task> handoff = () =>
                {
                    callbackReached = true;
                    ownerWasFrozen = !fixture.Form.Enabled;
                    preparationWasReady = FormTestDriver.Field<TargetExitPreparation>(fixture.Form, "_targetExitPreparation").IsReady;
                    Assert.That(ownerWasFrozen, Is.True, "The modal owner must remain disabled during the update handoff.");
                    throw new IOException("Injected installer handoff failure.");
                };

                timer.Start();
                var task = (Task<bool>)FormTestDriver.Invoke(fixture.Form, "RunAllTargetExitAsync", handoff, null);
                PumpUntil(() => task.IsCompleted);
                timer.Stop();

                Assert.That(timedOut, Is.False, "The isolated all-target exit dialog never became ready.");
                Assert.That(clicked, Is.True);
                Assert.That(callbackReached, Is.True);
                Assert.That(ownerWasFrozen, Is.True);
                Assert.That(preparationWasReady, Is.True);
                Assert.That(task.GetAwaiter().GetResult(), Is.False);
                Assert.That(fixture.Form.Enabled, Is.True);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                var preparation = FormTestDriver.Field<TargetExitPreparation>(fixture.Form, "_targetExitPreparation");
                Assert.That(preparation.IsPreparing, Is.False);
                Assert.That(preparation.IsReady, Is.False);
                Assert.That(fixture.Client.ConnectCalls, Is.Zero);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }
    }
}
