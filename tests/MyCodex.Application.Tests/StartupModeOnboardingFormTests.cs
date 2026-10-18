using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StartupModeOnboardingFormTests
    {
        [Test]
        public void StartupModeOnboardingForm_DefaultsToProvidedModeAndAppliesSelection()
        {
            using (var form = new StartupModeOnboardingForm(AppInteractionMode.Office))
            {
                Assert.That(form.SelectedMode, Is.EqualTo(AppInteractionMode.Office));
                FormTestDriver.Field<RadioButton>(form, "_codingRadioButton").Checked = true;
                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedMode, Is.EqualTo(AppInteractionMode.Coding));
            }
        }

        [Test]
        public void StartupModeOnboardingForm_HasAccessibleModeChoices()
        {
            using (var form = new StartupModeOnboardingForm(AppInteractionMode.Coding))
            {
                Assert.That(FormTestDriver.Field<RadioButton>(form, "_codingRadioButton").AccessibleName, Is.EqualTo(UiLocalization.Text("StartupModeOnboardingForm.Accessible.CodingMode", "Coding Mode")));
                Assert.That(FormTestDriver.Field<RadioButton>(form, "_officeRadioButton").AccessibleName, Is.EqualTo(UiLocalization.Text("StartupModeOnboardingForm.Accessible.OfficeMode", "Office Mode")));
            }
        }
    }
}
