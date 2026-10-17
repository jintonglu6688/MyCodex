using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TextBoxInputBehaviorTests
    {
        [Test]
        public void NormalizePastedMultilineText_ConvertsMixedLineEndingsToEnvironmentNewLine()
        {
            var normalized = ThemedTextBox.NormalizePastedMultilineText("one\ntwo\rthree\r\nfour");

            Assert.That(normalized, Is.EqualTo("one" + System.Environment.NewLine + "two" + System.Environment.NewLine + "three" + System.Environment.NewLine + "four"));
        }

        [Test]
        public void SetTextAndRestoreSelection_ClampsSelectionWithinUpdatedText()
        {
            using (var textBox = new TextBox())
            {
                TextBoxCaretHelper.SetTextAndRestoreSelection(textBox, "abc", 10, 10);

                Assert.That(textBox.Text, Is.EqualTo("abc"));
                Assert.That(textBox.SelectionStart, Is.EqualTo(3));
                Assert.That(textBox.SelectionLength, Is.EqualTo(0));
            }
        }

        [Test]
        public void ApplyTextBox_NormalizesExistingMultilineLfText()
        {
            using (var textBox = new TextBox())
            {
                textBox.Multiline = true;
                textBox.Text = "one\ntwo\nthree";

                UiTheme.ApplyTextBox(textBox, true, false);

                Assert.That(textBox.Text, Is.EqualTo("one" + System.Environment.NewLine + "two" + System.Environment.NewLine + "three"));
                Assert.That(textBox.Lines, Has.Length.EqualTo(3));
            }
        }

        [Test]
        public void ApplyTextBox_NormalizesLaterMultilineLfAssignments()
        {
            using (var textBox = new TextBox())
            {
                textBox.Multiline = true;
                UiTheme.ApplyTextBox(textBox, true, false);

                textBox.Text = "one\ntwo\nthree";

                Assert.That(textBox.Text, Is.EqualTo("one" + System.Environment.NewLine + "two" + System.Environment.NewLine + "three"));
                Assert.That(textBox.Lines, Has.Length.EqualTo(3));
            }
        }

        [Test]
        public void ThemedTextBox_NormalizesMultilineLfAssignments()
        {
            using (var textBox = new ThemedTextBox())
            {
                textBox.Multiline = true;

                textBox.Text = "one\ntwo\nthree";

                Assert.That(textBox.Text, Is.EqualTo("one" + System.Environment.NewLine + "two" + System.Environment.NewLine + "three"));
                Assert.That(textBox.Lines, Has.Length.EqualTo(3));
            }
        }
    }
}
