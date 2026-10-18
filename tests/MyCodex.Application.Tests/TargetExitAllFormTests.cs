using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TargetExitAllFormTests
    {
        [TestCase(TargetInstanceLifecycle.Starting, true)]
        [TestCase(TargetInstanceLifecycle.Closing, true)]
        [TestCase(TargetInstanceLifecycle.Running, false)]
        public void FailedInspectionShowsTheBlockingTargetWithoutEnablingExit(TargetInstanceLifecycle lifecycle, bool reachable)
        {
            var local = new TargetInstanceRecord { TargetId = "windows", InstanceId = "test" };
            var replies = new[] { new TargetInstanceReply
            {
                Instance = local, Success = reachable,
                Status = reachable ? new TargetInstanceStatus { Lifecycle = lifecycle } : null
            } };
            var coordinator = new TargetExitAllCoordinator(local, () => replies,
                (instance, request) => throw new AssertionException("Inspection must not send an exit request."));
            using (var form = new TargetExitAllForm(coordinator, instance => "This PC", "MyCodex Test", true))
            {
                var load = (Task)typeof(TargetExitAllForm).GetMethod("LoadScopeAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, null);
                var watch = Stopwatch.StartNew();
                while (!load.IsCompleted && watch.ElapsedMilliseconds < 3000)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(1);
                }
                Assert.That(load.IsCompleted, Is.True);
                load.GetAwaiter().GetResult();
                Assert.That(FormTestDriver.Field<ListView>(form, "_targets").Items.Count, Is.EqualTo(1), "Do not hide the reason installation was blocked.");
                Assert.That(FormTestDriver.Field<ListView>(form, "_targets").Items[0].Text, Is.EqualTo("This PC"));
                Assert.That(FormTestDriver.Field<ListView>(form, "_targets").Items[0].SubItems[1].Text, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<Button>(form, "_exit").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_cancel").Enabled, Is.True);
            }
        }

        [Test]
        public void ExitAllUsesNativeAccessibleListAndDefaultsToCancelWithoutStartingWork()
        {
            var requests = 0;
            var local = new TargetInstanceRecord { TargetId = "windows", InstanceId = "test" };
            var coordinator = new TargetExitAllCoordinator(local, () => new TargetInstanceReply[0], (instance, request) =>
            {
                requests++;
                return Task.FromResult(TargetInstanceReply.Failure("No real process is controlled by this UI test."));
            });
            using (var form = new TargetExitAllForm(coordinator, instance => "Local", "MyCodex Test"))
            {
                var targets = FormTestDriver.Field<ListView>(form, "_targets");
                var cancel = FormTestDriver.Field<Button>(form, "_cancel");
                Assert.That(targets.View, Is.EqualTo(View.Details));
                Assert.That(targets.MultiSelect, Is.False);
                Assert.That(targets.AccessibleName, Is.Not.Empty);
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(640));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(340));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.AcceptButton, Is.SameAs(cancel));
                Assert.That(form.CancelButton, Is.SameAs(cancel));
                Assert.That(form.ActiveControl, Is.SameAs(cancel));
                Assert.That(FormTestDriver.Field<Button>(form, "_exit").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_wait").Visible, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_status").TabStop, Is.False);
                Assert.That(cancel.Parent.TabStop, Is.False);
                var previous = -1;
                foreach (var field in new[] { "_exit", "_wait", "_cancel" })
                {
                    var button = FormTestDriver.Field<Button>(form, field);
                    Assert.That(button.AccessibleName, Is.Not.Empty);
                    Assert.That(button.TabIndex, Is.GreaterThan(previous));
                    previous = button.TabIndex;
                }
                Assert.That(requests, Is.Zero);
            }
        }
    }
}
