using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class DonateFormTests
    {
        [Test]
        public void DonateForm_QrCodeParticipatesInTabOrder()
        {
            using (var form = new DonateForm())
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var infoTextBox = FindSingleControl<TextBox>(form);
                var qrPictureBox = FindSingleControl<PictureBox>(form);
                var closeButton = FindSingleControl<Button>(form);

                Assert.That(infoTextBox.TabStop, Is.True);
                Assert.That(infoTextBox.TabIndex, Is.EqualTo(0));
                Assert.That(qrPictureBox.TabStop, Is.True);
                Assert.That(qrPictureBox.TabIndex, Is.EqualTo(1));
                Assert.That(qrPictureBox.AccessibleRole, Is.EqualTo(AccessibleRole.Graphic));
                Assert.That(closeButton.TabIndex, Is.EqualTo(2));

                infoTextBox.Focus();
                System.Windows.Forms.Application.DoEvents();

                var moved = form.SelectNextControl(infoTextBox, true, true, true, true);
                System.Windows.Forms.Application.DoEvents();

                Assert.That(moved, Is.True);
                Assert.That(qrPictureBox.Focused, Is.True);
            }
        }

        private static T FindSingleControl<T>(Control root)
            where T : Control
        {
            return root
                .Controls
                .Cast<Control>()
                .SelectMany(Flatten)
                .OfType<T>()
                .Single();
        }

        private static IEnumerable<Control> Flatten(Control control)
        {
            yield return control;

            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
