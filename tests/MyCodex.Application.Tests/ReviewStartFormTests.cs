using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ReviewStartFormTests
    {
        [TestCase(0, "", AppServerReviewTargetKind.UncommittedChanges)]
        [TestCase(1, " main ", AppServerReviewTargetKind.BaseBranch)]
        [TestCase(2, " abcdef1 ", AppServerReviewTargetKind.Commit)]
        [TestCase(3, " Check error handling\r\nand cancellation ", AppServerReviewTargetKind.Custom)]
        public void SelectTarget_ValidatesRequiredInputAndPreservesDelivery(int index, string input, AppServerReviewTargetKind kind)
        {
            using (var form = new ReviewStartForm())
            {
                var combos = Controls<ComboBox>(form).OrderBy(c => c.TabIndex).ToArray();
                combos[0].SelectedIndex = index;
                Controls<TextBox>(form).Single().Text = input;
                var target = form.BuildValidatedTarget();
                Assert.That(target.Kind, Is.EqualTo(kind));
                Assert.That(target.Branch ?? target.Sha ?? target.Instructions ?? "", Is.EqualTo(input.Trim()));
                Assert.That(form.RunInNewSession, Is.True);
                combos[1].SelectedIndex = 1;
                Assert.That(form.RunInNewSession, Is.False);
                Assert.That(form.BuildValidatedTarget().Kind, Is.EqualTo(kind));
                Assert.That(MainForm.BuildReviewTargetMessage(target, false), Does.Contain(input.Trim()));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void EmptyRequiredInput_DoesNotAcceptDialog(int index)
        {
            using (var form = new ReviewStartForm())
            {
                Controls<ComboBox>(form).OrderBy(c => c.TabIndex).First().SelectedIndex = index;
                Controls<TextBox>(form).Single().Text = " \t ";
                Assert.Throws<InvalidOperationException>(() => form.BuildValidatedTarget());
                Assert.That(form.SelectedTarget, Is.Null);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
            }
        }

        [Test]
        public void StartButton_AcceptsSelectedTargetAndDelivery()
        {
            using (var form = new ReviewStartForm())
            {
                form.Show();
                var combos = Controls<ComboBox>(form).OrderBy(c => c.TabIndex).ToArray();
                combos[0].SelectedIndex = 2;
                combos[1].SelectedIndex = 1;
                Controls<TextBox>(form).Single().Text = " abcdef1 ";
                ((Button)form.AcceptButton).PerformClick();
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(form.SelectedTarget.Kind, Is.EqualTo(AppServerReviewTargetKind.Commit));
                Assert.That(form.SelectedTarget.Sha, Is.EqualTo("abcdef1"));
                Assert.That(form.RunInNewSession, Is.False);
            }
        }

        [Test]
        public void DynamicLayout_IsResizableAndKeyboardAccessible()
        {
            using (var form = new ReviewStartForm())
            {
                form.Show();
                var combos = Controls<ComboBox>(form).OrderBy(c => c.TabIndex).ToArray();
                var input = Controls<TextBox>(form).Single();
                Assert.That(combos[0].Items.Count, Is.EqualTo(4));
                Assert.That(input.Visible, Is.False);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize, Is.EqualTo(form.Size));
                for (var index = 1; index < 4; index++)
                {
                    combos[0].SelectedIndex = index;
                    form.PerformLayout();
                    Assert.That(input.Visible, Is.True);
                    Assert.That(input.Multiline, Is.EqualTo(index == 3));
                    Assert.That(input.AccessibleName, Is.Not.Empty.And.Not.Contains("&"));
                    Assert.That(input.TabIndex, Is.GreaterThan(combos[0].TabIndex).And.LessThan(combos[1].TabIndex));
                    Assert.That(input.Height, Is.GreaterThanOrEqualTo(index == 3 ? 90 : 20));
                    Assert.That(input.Bottom, Is.LessThanOrEqualTo(combos[1].Top));
                }
                var oldWidth = input.Width;
                form.Width += 180;
                form.PerformLayout();
                Assert.That(input.Width, Is.GreaterThan(oldWidth));
                foreach (var control in Controls<Control>(form).Where(c => c is ComboBox || c is TextBox || c is Button))
                    Assert.That(control.AccessibleName, Is.Not.Null.And.Not.Empty);
                foreach (var label in Controls<Label>(form)) Assert.That(label.TabStop, Is.False);
                Assert.That(form.AcceptButton, Is.Not.Null);
                Assert.That(form.CancelButton, Is.Not.Null);
            }
        }

        private static IEnumerable<T> Controls<T>(Control parent) where T : Control
        {
            foreach (Control child in parent.Controls)
            {
                if (child is T match) yield return match;
                foreach (var descendant in Controls<T>(child)) yield return descendant;
            }
        }
    }
}
