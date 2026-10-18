using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal enum SendInputValidationOutcome
    {
        Continue = 0,
        EmptyInput,
        AttachmentsUnavailable,
        WorkspaceUnavailable
    }

    internal sealed class SendSessionBootstrapDecision
    {
        public SendSessionBootstrapDecision(bool requiresNewSession, string statusText)
        {
            RequiresNewSession = requiresNewSession;
            StatusText = statusText ?? string.Empty;
        }

        public bool RequiresNewSession { get; }

        public string StatusText { get; }

        public bool IsBlocked
        {
            get { return !RequiresNewSession && !string.IsNullOrWhiteSpace(StatusText); }
        }
    }

    internal sealed class SendInputValidationResult
    {
        public SendInputValidationResult(SendInputValidationOutcome outcome, string statusText, string warningMessage)
        {
            Outcome = outcome;
            StatusText = statusText ?? string.Empty;
            WarningMessage = warningMessage ?? string.Empty;
        }

        public SendInputValidationOutcome Outcome { get; }

        public string StatusText { get; }

        public string WarningMessage { get; }

        public bool CanContinue
        {
            get { return Outcome == SendInputValidationOutcome.Continue; }
        }
    }

    internal static class SendInputPreflight
    {
        internal static SendSessionBootstrapDecision EvaluateSessionBootstrap(bool hasCurrentSession, bool hasCurrentWorkspace)
        {
            if (hasCurrentSession)
            {
                return new SendSessionBootstrapDecision(false, string.Empty);
            }

            if (!hasCurrentWorkspace)
            {
                return new SendSessionBootstrapDecision(
                    false,
                    UiLocalization.Text("MainForm.Status.CreateWorkspaceBeforeSending", "Please create a workspace first"));
            }

            return new SendSessionBootstrapDecision(true, string.Empty);
        }

        internal static SendInputValidationResult EvaluateAttachmentAvailability(IReadOnlyList<PendingAttachmentItem> unavailableAttachments)
        {
            if (unavailableAttachments == null || unavailableAttachments.Count == 0)
            {
                return new SendInputValidationResult(SendInputValidationOutcome.Continue, string.Empty, string.Empty);
            }

            var unavailableText = string.Join(
                UiLocalization.Text("MainForm.Separator.List", ", "),
                unavailableAttachments
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                    .Select(item => item.DisplayName)
                    .ToArray());
            return new SendInputValidationResult(
                SendInputValidationOutcome.AttachmentsUnavailable,
                UiLocalization.Text("MainForm.Status.AttachmentsUnavailable", "Attachments are unavailable. Please add them again"),
                string.Format(
                    UiLocalization.Text("MainForm.Message.AttachmentsUnavailableBeforeSend.Template", "The following attachments are unavailable. Re-add them before sending: {0}"),
                    unavailableText));
        }

        internal static SendInputValidationResult EvaluateReadyToDispatch(bool isCurrentWorkspaceAvailable, int inputItemCount)
        {
            if (inputItemCount == 0)
            {
                return new SendInputValidationResult(SendInputValidationOutcome.EmptyInput, string.Empty, string.Empty);
            }

            if (!isCurrentWorkspaceAvailable)
            {
                return new SendInputValidationResult(
                    SendInputValidationOutcome.WorkspaceUnavailable,
                    UiLocalization.Text("MainForm.Status.WorkspacePathUnavailable", "Current workspace path is unavailable"),
                    string.Empty);
            }

            return new SendInputValidationResult(SendInputValidationOutcome.Continue, string.Empty, string.Empty);
        }
    }
}
