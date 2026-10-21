using System;
using System.Drawing;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Controls;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ToolUserInputFormTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpApproval_FocusesReadablePromptAndRequiresExplicitSubmitForRememberedChoice()
        {
            var request = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(42, "42", McpToolApprovalTests.Request());
            using (var form = new ToolUserInputForm(request.Questions, "MCP Confirmation", request.Content, true))
            {
                var controls = Descendants(form).ToList();
                var summary = controls.OfType<AccessibleStaticTextControl>().Single();
                var choices = controls.OfType<ComboBox>().Single();
                Assert.That(summary.Text, Is.EqualTo(request.Content));
                Assert.That(summary.AccessibleName, Is.EqualTo(request.Content));
                Assert.That(summary.TabStop, Is.True);
                Assert.That(summary.TabIndex, Is.Zero);
                Assert.That(choices.AccessibleName, Is.EqualTo(request.Questions[0].Question));
                Assert.That(choices.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(620));
                Assert.That(controls.Where(c => c is Panel || c is Label).All(c => !c.TabStop), Is.True);
                Assert.That(controls.OfType<Button>().All(c => !string.IsNullOrWhiteSpace(c.AccessibleName)), Is.True);
                form.Show();
                Assert.That(summary.Focused, Is.True);
                choices.SelectedIndex = 2;
                Assert.That(form.Answers, Is.Null, "Selecting a scope must not submit approval.");
                var ok = (Button)typeof(ToolUserInputForm).GetField("_okButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                ok.PerformClick();
                Assert.That(form.Answers[request.Questions[0].Id], Is.EqualTo(new[] { "always" }));
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
            }
        }

        [TestCase(150)]
        [TestCase(400)]
        [Apartment(ApartmentState.STA)]
        public void Confirmation_LongContentGrowsWithoutCoveringQuestions(int wordCount)
        {
            var content = "Allow the tool to continue? " + string.Join(" ", Enumerable.Repeat("detail", wordCount));
            var questions = new[]
            {
                new AppServerToolUserInputQuestion { Id = "choice", Question = "Continue?" }
            };
            using (var form = new ToolUserInputForm(questions, "MCP Confirmation", content, true))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var summary = Descendants(form).OfType<AccessibleStaticTextControl>().Single();
                var scrollPanel = summary.Parent.Controls.OfType<Panel>().Single(panel => panel.AutoScroll);
                var needed = TextRenderer.MeasureText(
                    summary.Text,
                    summary.Font,
                    new Size(summary.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.Left).Height;

                Assert.That(needed, Is.GreaterThan(120), "the content must be longer than the old fixed row");
                Assert.That(summary.Height, Is.GreaterThanOrEqualTo(Math.Min(needed, summary.MaximumSize.Height)));
                Assert.That(summary.Height, Is.LessThanOrEqualTo(form.ClientSize.Height / 2));
                Assert.That(scrollPanel.Top, Is.GreaterThanOrEqualTo(summary.Bottom));
                Assert.That(scrollPanel.Height, Is.GreaterThan(0));
                form.Hide();
            }
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CloseAsExternallyResolved_MarksAbortWithoutAnswers()
        {
            using (var form = new ToolUserInputForm(new[]
                   {
                       new AppServerToolUserInputQuestion
                       {
                           Id = "choice",
                           Question = "Continue?"
                       }
                   }))
            {
                form.CloseAsExternallyResolved();

                Assert.That(form.WasExternallyResolved, Is.True);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.Abort));
                Assert.That(form.Answers, Is.Null);
            }
        }
    }
}
