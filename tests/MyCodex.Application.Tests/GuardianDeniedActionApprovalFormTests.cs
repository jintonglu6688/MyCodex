using System;
using System.Globalization;
using System.Collections.Generic;
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
    public sealed class GuardianDeniedActionApprovalFormTests
    {
        [TestCase("en-US")]
        [TestCase("zh-CN")]
        [Apartment(ApartmentState.STA)]
        public void GuardianDeniedActionApprovalForm_ShowsReadOnlyDetailsAndRequiresExplicitApprove(string cultureName)
        {
            WithCulture(cultureName, () =>
            {
                var request = new AppServerGuardianDeniedActionApprovalRequest
                {
                    ThreadId = "thread-1",
                    RiskLevel = "high",
                    Rationale = "Denied high risk command",
                    Event = new Dictionary<string, object>
                    {
                        { "riskLevel", "high" },
                        { "action", new Dictionary<string, object> { { "type", "execve" }, { "command", "rm -rf temp" } } }
                    }
                };

                using (var form = new GuardianDeniedActionApprovalForm(request))
                {
                    var details = FindControl<TextBox>(form, UiLocalization.Text("GuardianDeniedActionApprovalForm.Accessible.Details", "Denied action details"));
                    var approve = FindControl<Button>(form, UiLocalization.Text("GuardianDeniedActionApprovalForm.Accessible.Approve", "Approve denied action"));
                    var summary = (AccessibleStaticTextControl)typeof(GuardianDeniedActionApprovalForm)
                        .GetField("_summaryControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .GetValue(form);

                    Assert.That(details.ReadOnly, Is.True);
                    Assert.That(details.Text, Does.Contain("thread-1"));
                    Assert.That(details.Text, Does.Contain("Denied high risk command"));
                    Assert.That(details.Text, Does.Contain("execve"));
                    Assert.That(approve.Enabled, Is.True);
                    Assert.That(form.AcceptButton, Is.Not.SameAs(approve));
                    Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                    Assert.That(details.TabStop, Is.False);
                    Assert.That(summary.TabStop, Is.True);
                    Assert.That(summary.TabIndex, Is.Zero);
                    Assert.That(summary.AccessibleRole, Is.EqualTo(AccessibleRole.StaticText));
                    Assert.That(summary.AccessibleName, Is.EqualTo(summary.Text));
                    Assert.That(summary.Text, Does.Contain("Denied high risk command"));
                    Assert.That(approve.TabIndex, Is.EqualTo(1));
                    Assert.That(((Control)form.CancelButton).TabIndex, Is.EqualTo(2));
                    Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                    Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(640));
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Assert.That(summary.Focused, Is.True);
                    form.CloseAsExternallyResolved();
                    Assert.That(form.WasExternallyResolved, Is.True);
                    Assert.That(form.DialogResult, Is.EqualTo(DialogResult.Abort));
                }
            });
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private static T FindControl<T>(Control root, string accessibleName)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && string.Equals(typed.AccessibleName, accessibleName, StringComparison.Ordinal))
                {
                    return typed;
                }

                var nested = FindControlOrDefault<T>(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            Assert.Fail("Control not found: " + accessibleName);
            return null;
        }

        private static T FindControlOrDefault<T>(Control root, string accessibleName)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null && string.Equals(typed.AccessibleName, accessibleName, StringComparison.Ordinal))
                {
                    return typed;
                }

                var nested = FindControlOrDefault<T>(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
