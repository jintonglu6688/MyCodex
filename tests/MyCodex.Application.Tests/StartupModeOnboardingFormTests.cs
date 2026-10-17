using System.Reflection;
using System.Threading;
using System.Windows.Forms;
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
                GetField<RadioButton>(form, "_codingRadioButton").Checked = true;
                InvokeOk(form);

                Assert.That(form.SelectedMode, Is.EqualTo(AppInteractionMode.Coding));
            }
        }

        [Test]
        public void StartupModeOnboardingForm_HasAccessibleModeChoices()
        {
            using (var form = new StartupModeOnboardingForm(AppInteractionMode.Coding))
            {
                Assert.That(GetField<RadioButton>(form, "_codingRadioButton").AccessibleName, Is.EqualTo(UiLocalization.Text("StartupModeOnboardingForm.Accessible.CodingMode", "Coding Mode")));
                Assert.That(GetField<RadioButton>(form, "_officeRadioButton").AccessibleName, Is.EqualTo(UiLocalization.Text("StartupModeOnboardingForm.Accessible.OfficeMode", "Office Mode")));
            }
        }

        private static T GetField<T>(StartupModeOnboardingForm form, string fieldName) where T : class
        {
            var field = typeof(StartupModeOnboardingForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(form);
        }

        private static void InvokeOk(StartupModeOnboardingForm form)
        {
            var method = typeof(StartupModeOnboardingForm).GetMethod("OkButton_Click", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[] { null, System.EventArgs.Empty });
        }
    }
}
