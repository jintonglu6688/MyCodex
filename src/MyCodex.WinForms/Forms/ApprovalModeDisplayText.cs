using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class ApprovalModeDisplayText
    {
        public static string Name(ApprovalMode approvalMode)
        {
            switch (approvalMode)
            {
                case ApprovalMode.ReadOnly:
                    return UiLocalization.Text("ApprovalMode.ReadOnly", "Read Only");
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return UiLocalization.Text("ApprovalMode.FullAccess", "Full Access (Auto-approve)");
                case ApprovalMode.Custom:
                    return UiLocalization.Text("ApprovalMode.Custom", "Custom");
                default:
                    return UiLocalization.Text("ApprovalMode.Auto", "Standard");
            }
        }

        public static string NameWithDescription(ApprovalMode approvalMode)
        {
            switch (approvalMode)
            {
                case ApprovalMode.ReadOnly:
                    return UiLocalization.Text(
                        "ApprovalMode.ReadOnlyWithDescription",
                        "Read Only - read files only; asks before changes.");
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return UiLocalization.Text(
                        "ApprovalMode.FullAccessWithDescription",
                        "Full Access (Auto-approve) - automatically approves commands and file operations, including deletion; no model review.");
                case ApprovalMode.Custom:
                    return UiLocalization.Text(
                        "ApprovalMode.CustomWithDescription",
                        "Custom - starts read-only; selected permission categories are allowed automatically.");
                default:
                    return UiLocalization.Text(
                        "ApprovalMode.AutoWithDescription",
                        "Standard - can work in workspace; asks for broader access.");
            }
        }
    }
}
