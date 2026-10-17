using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class LocalSessionDeleteConfirmationFormTests
    {
        [Test]
        public void Constructor_DefaultsDeleteLocalHistoryOptionToOff()
        {
            using (var form = new LocalSessionDeleteConfirmationForm("Delete this session?", true))
            {
                Assert.That(form.DeleteLocalHistoryOptionEnabled, Is.True);
                Assert.That(form.DeleteLocalHistoryOptionChecked, Is.False);
                Assert.That(form.DeleteLocalHistory, Is.False);
            }
        }

        [Test]
        public void Constructor_CanDefaultDeleteLocalHistoryOptionToOn()
        {
            using (var form = new LocalSessionDeleteConfirmationForm("Delete this session?", true, true))
            {
                Assert.That(form.DeleteLocalHistoryOptionEnabled, Is.True);
                Assert.That(form.DeleteLocalHistoryOptionChecked, Is.True);
            }
        }

        [Test]
        public void Constructor_DisablesDeleteLocalHistoryWhenUnavailable()
        {
            using (var form = new LocalSessionDeleteConfirmationForm("Delete this session?", false))
            {
                Assert.That(form.DeleteLocalHistoryOptionEnabled, Is.False);
                Assert.That(form.DeleteLocalHistoryOptionChecked, Is.False);
            }
        }

        [Test]
        public void DeleteButton_AppliesCheckedDeleteLocalHistoryOption()
        {
            using (var form = new LocalSessionDeleteConfirmationForm("Delete this session?", true))
            {
                FindCheckBox(form).Checked = true;
                InvokeDeleteButton(form);

                Assert.That(form.DeleteLocalHistory, Is.True);
            }
        }

        [Test]
        public void DeleteButton_IgnoresDisabledDeleteLocalHistoryOption()
        {
            using (var form = new LocalSessionDeleteConfirmationForm("Delete this session?", false))
            {
                FindCheckBox(form).Checked = true;
                InvokeDeleteButton(form);

                Assert.That(form.DeleteLocalHistory, Is.False);
            }
        }

        private static CheckBox FindCheckBox(Control root)
        {
            foreach (Control control in root.Controls)
            {
                var checkBox = control as CheckBox;
                if (checkBox != null)
                {
                    return checkBox;
                }

                var nested = FindCheckBoxOrNull(control);
                if (nested != null)
                {
                    return nested;
                }
            }

            Assert.Fail("CheckBox not found.");
            return null;
        }

        private static CheckBox FindCheckBoxOrNull(Control root)
        {
            return root.Controls
                .Cast<Control>()
                .Select(control => control as CheckBox ?? FindCheckBoxOrNull(control))
                .FirstOrDefault(checkBox => checkBox != null);
        }

        private static void InvokeDeleteButton(LocalSessionDeleteConfirmationForm form)
        {
            var method = typeof(LocalSessionDeleteConfirmationForm).GetMethod(
                "DeleteButton_Click",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[] { null, EventArgs.Empty });
        }
    }
}
