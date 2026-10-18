using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private Task OpenPermissionSettingsAsync(ChatSession session, bool isGlobalDefault)
        {
            var title = isGlobalDefault
                ? UiLocalization.Text("MainForm.SessionSettings.PermissionSettings.Title.Default", "Default Permission")
                : UiLocalization.Text("MainForm.SessionSettings.PermissionSettings.Title.Session", "Session Permission");
            var inheritText = UiLocalization.Text("MainForm.SessionSettings.Common.FollowDefault", "Follow Default");
            var inheritDescription = isGlobalDefault
                ? string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.PermissionSettings.DefaultDescription.Template", "Current app default permission: {0}."),
                    BuildDefaultPermissionSummaryText())
                : BuildSessionPermissionDefaultDescription(session);
            var selectedMode = isGlobalDefault
                ? (ApprovalMode?)GetConfiguredDefaultApprovalMode()
                : (session != null ? session.ApprovalMode : null);
            var effectiveMode = isGlobalDefault ? GetEffectiveApprovalMode(null) : GetEffectiveApprovalMode(session);

            using (var form = new PermissionSettingsForm(
                title,
                !isGlobalDefault,
                inheritText,
                inheritDescription,
                selectedMode,
                effectiveMode))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return Task.CompletedTask;
                }

                if (isGlobalDefault)
                {
                    try
                    {
                        SetConfiguredDefaultApprovalMode(form.SelectedApprovalMode ?? GetBuiltInDefaultApprovalMode());
                        if (!SaveAppConfig())
                        {
                            return Task.CompletedTask;
                        }

                        RefreshSessionSettingsState();
                        AddChatHistoryItem(
                            GetStatusSenderText(),
                            string.Format(
                                UiLocalization.Text("MainForm.SessionSettings.Chat.DefaultPermissionUpdated.Template", "Default permission updated to: {0}"),
                                BuildDefaultPermissionSummaryText()),
                            ChatHistoryCategory.System);
                        SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.DefaultPermissionUpdated", "Default permission updated"));
                    }
                    catch (Exception ex)
                    {
                        AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                        SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.UpdateDefaultPermissionFailed", "Failed to update default permission"));
                    }

                    return Task.CompletedTask;
                }

                if (session == null)
                {
                    return Task.CompletedTask;
                }

                session.ApprovalMode = form.SelectedApprovalMode;
                PersistState();
                RefreshSessionSettingsState();

                if (_currentSession != null && string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AddChatHistoryItem(
                        GetStatusSenderText(),
                        string.Format(
                            UiLocalization.Text("MainForm.SessionSettings.Chat.SessionPermissionUpdated.Template", "Current session permission updated to: {0}"),
                            BuildSessionPermissionMenuSummary(session)),
                        ChatHistoryCategory.System);
                }

                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.SessionPermissionUpdated", "Session permission updated"));
            }

            return Task.CompletedTask;
        }

        private string BuildDefaultPermissionSummaryText()
        {
            return GetApprovalModeText(GetConfiguredDefaultApprovalMode());
        }

        private string BuildSessionPermissionMenuSummary(ChatSession session)
        {
            if (session == null)
            {
                return UiLocalization.Text("Common.Default", "Default");
            }

            if (!HasApprovalOverride(session))
            {
                return string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.Summary.FollowDefault.Template", "Follow default - {0}"),
                    BuildDefaultPermissionSummaryText());
            }

            return GetApprovalModeText(session.ApprovalMode ?? ApprovalMode.Auto);
        }

        private string BuildSessionPermissionDefaultDescription(ChatSession session)
        {
            return string.Format(
                UiLocalization.Text("MainForm.SessionSettings.PermissionSettings.SessionDefaultDescription.Template", "After saving, permission override for current session will be removed and switch to follow default config. Currently effective: {0}"),
                BuildEffectivePermissionSummaryText(session));
        }

        private string BuildEffectivePermissionSummaryText(ChatSession session)
        {
            return GetApprovalModeText(GetEffectiveApprovalMode(session));
        }

    }
}
