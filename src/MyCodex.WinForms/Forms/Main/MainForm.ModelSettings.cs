using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task OpenModelSettingsAsync(ChatSession session, bool isGlobalDefault)
        {
            var workspace = session != null ? FindWorkspaceById(session.WorkspaceId) : null;
            var observation = await RefreshModelConfigurationAsync(workspace != null ? workspace.RootPath : null, true);
            if (observation == null)
            {
                return;
            }
            var editScope = BuildModelConfigurationScope();

            var title = isGlobalDefault
                ? UiLocalization.Text("MainForm.SessionSettings.ModelSettings.Title.Default", "Default Model")
                : UiLocalization.Text("MainForm.SessionSettings.ModelSettings.Title.Session", "Session Model");
            var defaultOptionText = UiLocalization.Text("MainForm.SessionSettings.Common.FollowDefault", "Follow Default");
            var defaultDescriptionText = isGlobalDefault
                ? string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.ModelSettings.DefaultDescription.Template", "Current official default: {0}. Saving updates Codex configuration for subsequent turns; session overrides are kept."),
                    BuildDefaultModelSummaryText())
                : BuildSessionModelDefaultDescription(session);
            var selectedModelId = isGlobalDefault
                ? GetConfiguredDefaultModelId()
                : (session != null ? session.ModelId : null);
            var selectedReasoningEffort = isGlobalDefault
                ? GetConfiguredDefaultReasoningEffort()
                : (session != null ? session.ReasoningEffort : null);

            using (var form = new ModelSettingsForm(
                title,
                MainFormModelOptionComposer.BuildOrderedModelOptions(_availableModels),
                selectedModelId,
                selectedReasoningEffort,
                defaultOptionText,
                defaultDescriptionText,
                !isGlobalDefault,
                true,
                SupportsReasoningEffortForModel))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (isGlobalDefault)
                {
                    try
                    {
                        if (!await SaveOfficialModelDefaultsAsync(observation.GlobalConfig, editScope,
                            form.SelectedModelId, form.SelectedReasoningEffort))
                        {
                            return;
                        }

                        RefreshSessionSettingsState();
                        AddChatHistoryItem(
                            GetStatusSenderText(),
                            string.Format(
                                UiLocalization.Text("MainForm.SessionSettings.Chat.DefaultModelUpdated.Template", "Default model updated to: {0}"),
                                BuildDefaultModelSummaryText()),
                            ChatHistoryCategory.System);
                        SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.DefaultModelUpdated", "Default model updated"));
                    }
                    catch (Exception)
                    {
                        ShowOfficialConfigWriteWarning(UiLocalization.Text("MainForm.OfficialSettings.ConfigWriteConflict",
                            "Official Codex settings were not changed. Another client may have changed them. Reload the target, review the settings, and try again."));
                        SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.UpdateDefaultModelFailed", "Failed to update default model"));
                    }

                    return;
                }

                if (session == null)
                {
                    return;
                }

                session.ModelId = form.SelectedModelId;
                session.ReasoningEffort = form.SelectedReasoningEffort;
                session.AutomationModelSettingsInitialized = true;
                PersistState();
                RefreshSessionSettingsState();

                if (_currentSession != null && string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
                {
                    AddChatHistoryItem(
                        GetStatusSenderText(),
                        string.Format(
                            UiLocalization.Text("MainForm.SessionSettings.Chat.SessionModelUpdated.Template", "Current session model updated to: {0}"),
                            BuildSessionModelMenuSummary(session)),
                        ChatHistoryCategory.System);
                }

                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.SessionModelUpdated", "Session model updated"));
            }
        }

        private string BuildDefaultModelSummaryText()
        {
            return BuildModelSummaryText(
                GetConfiguredDefaultModelId(),
                GetConfiguredDefaultReasoningEffort(),
                UiLocalization.Text("Common.Default", "Default"));
        }

        private string BuildSessionModelMenuSummary(ChatSession session)
        {
            if (session == null)
            {
                return UiLocalization.Text("Common.Default", "Default");
            }

            if (!HasModelOverride(session))
            {
                return string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.Summary.FollowDefault.Template", "Follow default - {0}"),
                    BuildDefaultModelSummaryText());
            }

            return BuildModelSummaryText(session.ModelId, session.ReasoningEffort, BuildDefaultModelSummaryText());
        }

        private string BuildEffectiveModelSummaryText(ChatSession session)
        {
            return BuildModelSummaryText(GetEffectiveModelId(session), GetEffectiveReasoningEffort(session), BuildDefaultModelSummaryText());
        }

        private string BuildSessionModelDefaultDescription(ChatSession session)
        {
            return string.Format(
                UiLocalization.Text("MainForm.SessionSettings.ModelSettings.SessionDefaultDescription.Template", "After saving, model override for current session will be removed and switch to follow default config. Currently effective: {0}"),
                BuildEffectiveModelSummaryText(session));
        }
    }
}
