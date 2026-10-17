using System.Linq;
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
    public sealed class PermissionSettingsFormTests
    {
        [Test]
        public void PermissionSettingsForm_PresetChoicesIncludeBriefDescriptions()
        {
            using (var form = new PermissionSettingsForm(
                "Permissions",
                false,
                null,
                null,
                ApprovalMode.Auto,
                ApprovalMode.Auto))
            {
                var permissionComboBox = FindControl<ComboBox>(
                    form,
                    UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"));

                CollectionAssert.AreEqual(
                    new[]
                    {
                        UiLocalization.Text(
                            "ApprovalMode.ReadOnlyWithDescription",
                            "Read Only - read files only; asks before changes."),
                        UiLocalization.Text(
                            "ApprovalMode.AutoWithDescription",
                            "Auto - can work in workspace; asks for broader access."),
                        UiLocalization.Text(
                            "ApprovalMode.FullAccessWithDescription",
                            "Full Access (Auto-approve) - automatically approves commands and file operations, including deletion; no model review."),
                        UiLocalization.Text(
                            "ApprovalMode.CustomWithDescription",
                            "Custom - starts read-only; selected permission categories are allowed automatically.")
                    },
                    permissionComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
            }
        }

        [TestCase(ApprovalMode.FullAccess, 4)]
        [TestCase(ApprovalMode.FullAccessAutoApprove, 4)]
        public void PermissionSettingsForm_PreservesExplicitSelectionAndAccessibleNavigation(ApprovalMode mode, int count)
        {
            using (var form = new PermissionSettingsForm("Permissions", false, null, null, mode, mode))
            {
                var combo = FindControl<ComboBox>(form, UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"));
                Assert.That(combo.Items.Count, Is.EqualTo(count));
                Assert.That(combo.SelectedItem.ToString(), Is.EqualTo(ApprovalModeDisplayText.NameWithDescription(mode)));
                Assert.That(combo.TabIndex, Is.EqualTo(0));
                Assert.That(form.SelectedApprovalMode, Is.Null, "Browsing choices must not apply permissions.");
                typeof(PermissionSettingsForm).GetMethod("OkButton_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(form, new object[] { null, System.EventArgs.Empty });
                Assert.That(form.SelectedApprovalMode, Is.EqualTo(ApprovalMode.FullAccess));
            }
        }

        [Test]
        public void PermissionSettingsForm_CanSelectCustom()
        {
            using (var form = new PermissionSettingsForm(
                "Permissions",
                false,
                null,
                null,
                ApprovalMode.Custom,
                ApprovalMode.Custom))
            {
                var permissionComboBox = FindControl<ComboBox>(
                    form,
                    UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"));

                Assert.That(permissionComboBox.SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text(
                    "ApprovalMode.CustomWithDescription",
                    "Custom - starts read-only; selected permission categories are allowed automatically.")));
            }
        }

        [Test]
        public void PermissionSettingsForm_InheritAndKeepCurrentRemainConcise()
        {
            using (var form = new PermissionSettingsForm(
                "Permissions",
                true,
                null,
                null,
                null,
                ApprovalMode.Auto,
                true))
            {
                var permissionComboBox = FindControl<ComboBox>(
                    form,
                    UiLocalization.Text("PermissionSettingsForm.Accessible.Permission", "Permission"));

                Assert.That(permissionComboBox.Items[0].ToString(), Is.EqualTo(UiLocalization.Text(
                    "PermissionSettingsForm.KeepCurrentText",
                    "Keep Current Custom Setting")));
                Assert.That(permissionComboBox.Items[1].ToString(), Is.EqualTo(UiLocalization.Text(
                    "PermissionSettingsForm.InheritText",
                    "Follow Default")));
                Assert.That(permissionComboBox.Items[2].ToString(), Is.EqualTo(UiLocalization.Text(
                    "ApprovalMode.ReadOnlyWithDescription",
                    "Read Only - read files only; asks before changes.")));
            }
        }

        private static T FindControl<T>(Control root, string accessibleName)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var match = child as T;
                if (match != null && match.AccessibleName == accessibleName)
                {
                    return match;
                }

                var nested = FindControl<T>(child, accessibleName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
