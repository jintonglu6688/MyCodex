using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ApprovalRequestFormTests
    {
        [Test]
        public void ApprovalRequestForm_UsesDirectDecisionButtonsAndPlainSummary()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-1",
                workingDirectory: @"E:\MyCodex",
                details: "Deleted src\\old file.txt\r\nWrite root: E:\\MyCodex",
                approvalKind: AppServerApprovalRequestKind.FileChange,
                canApproveForSession: true);

            using (var form = new ApprovalRequestForm(request))
            {
                Assert.That(form.Text, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Change", "Change Request")));
                Assert.That(FindFirst<ComboBox>(form), Is.Null);

                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(640));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(320));

                var expectedQuestion = string.Format(
                    UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                    string.Format(
                        UiLocalization.Text("ApprovalRequestSummary.FileChange.Target.Delete.Template", "delete {0}"),
                        "src\\old file.txt"));
                var question = FindControl<Control>(form, expectedQuestion);
                Assert.That(question, Is.Not.InstanceOf<TextBox>());
                Assert.That(question.TabStop, Is.True);
                Assert.That(question.TabIndex, Is.EqualTo(0));

                var details = FindControl<TextBox>(form, UiLocalization.Text("ApprovalRequestForm.Accessible.Details", "Details"));
                Assert.That(details.Text, Does.Contain(UiLocalization.Text("ApprovalRequestSummary.FileChange.Action.Change", "change files")));
                Assert.That(details.Text, Does.Contain("Deleted src\\old file.txt"));
                Assert.That(details.ReadOnly, Is.True);
                Assert.That(details.Multiline, Is.True);
                Assert.That(details.ScrollBars, Is.EqualTo(ScrollBars.Vertical));
                Assert.That(details.TabStop, Is.False);

                var allowOnce = FindControl<Button>(form, UiLocalization.Text("ApprovalRequestForm.Accessible.AllowOnce.Change", "Allow change once"));
                var allowForSession = FindControl<Button>(form, UiLocalization.Text("ApprovalRequestForm.Accessible.AllowForSession.Change", "Allow change for this session"));
                var decline = FindControl<Button>(form, UiLocalization.Text("ApprovalRequestForm.Accessible.Decline.Change", "Decline change"));

                Assert.That(allowOnce.TabIndex, Is.EqualTo(1));
                Assert.That(allowForSession.TabIndex, Is.EqualTo(2));
                Assert.That(decline.TabIndex, Is.EqualTo(3));
            }
        }

        [TestCase(150)]
        [TestCase(400)]
        public void ApprovalRequestForm_LongQuestionGrowsWithoutCoveringDetails(int wordCount)
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: Write-Output",
                requestId: "approval-long",
                commandText: "Write-Output " + string.Join(" ", Enumerable.Repeat("segment", wordCount)),
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            using (var form = new ApprovalRequestForm(request))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var summary = FindFirst<AccessibleStaticTextControl>(form);
                var details = FindControl<TextBox>(form, UiLocalization.Text("ApprovalRequestForm.Accessible.Details", "Details"));
                var needed = TextRenderer.MeasureText(
                    summary.Text,
                    summary.Font,
                    new Size(summary.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.Left).Height;

                Assert.That(needed, Is.GreaterThan(86), "the question must be longer than the old fixed row");
                Assert.That(summary.Height, Is.GreaterThanOrEqualTo(Math.Min(needed, summary.MaximumSize.Height)));
                Assert.That(summary.Height, Is.LessThanOrEqualTo(form.ClientSize.Height / 2));
                Assert.That(details.Top, Is.GreaterThanOrEqualTo(summary.Bottom));
                Assert.That(details.Height, Is.GreaterThan(0));
                form.Hide();
            }
        }

        private static T FindControl<T>(Control root, string accessibleName)
            where T : Control
        {
            var control = FindControlOrNull<T>(root, accessibleName);
            Assert.That(control, Is.Not.Null, typeof(T).Name + " with accessible name " + accessibleName + " should exist.");
            return control;
        }

        private static T FindControlOrNull<T>(Control root, string accessibleName)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && string.Equals(typed.AccessibleName, accessibleName, System.StringComparison.Ordinal))
                {
                    return typed;
                }

                var nested = FindControlOrNull<T>(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static T FindFirst<T>(Control root)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null)
                {
                    return typed;
                }

                var nested = FindFirst<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
