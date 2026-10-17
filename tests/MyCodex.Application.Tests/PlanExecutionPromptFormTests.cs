using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PlanExecutionPromptFormTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void CloseAsExternallyResolved_ExecuteMarksDialogAccepted()
        {
            using (var form = new PlanExecutionPromptForm("# Plan"))
            {
                form.CloseAsExternallyResolved(true);

                Assert.That(form.WasExternallyResolved, Is.True);
                Assert.That(form.ShouldExecutePlan, Is.True);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CloseAsExternallyResolved_ContinueMarksDialogCanceled()
        {
            using (var form = new PlanExecutionPromptForm("# Plan"))
            {
                form.CloseAsExternallyResolved(false);

                Assert.That(form.WasExternallyResolved, Is.True);
                Assert.That(form.ShouldExecutePlan, Is.False);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.Cancel));
            }
        }
    }
}
