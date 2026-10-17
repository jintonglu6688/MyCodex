using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using TextFileMode = MyCodex.Shared.Constants.TextFileOpenMode;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task OpenOptionsAsync()
        {
            if (_appConfigSnapshot == null)
            {
                LoadAppConfig();
            }

            await RefreshGlobalServerConfigAsync(false);
            var officialSnapshot = _globalServerConfigSnapshot;
            var permissionProfiles = await ReadPermissionProfilesForOptionsAsync(officialSnapshot).ConfigureAwait(true);
            var officeTarget = GetActiveOfficeRuntimeTarget();
            var modeSettingsAvailable = await LoadModeSettingsForCurrentRuntimeAsync();
            var officeDirectoryContext = CreateOfficeDirectoryOptionsContext(
                officeTarget,
                modeSettingsAvailable);
            using (var form = new OptionsForm(
                _appConfigSnapshot,
                officialSnapshot,
                permissionProfiles,
                _availableModels,
                officeDirectoryContext,
                _activeModeSettings))
            {
                OfficialCodexSettingsSelection officialSettings = null;
                var updatedLocalOptions = false;
                var previousTheme = GetConfiguredUiThemePreference();
                var previousLanguage = GetConfiguredUiLanguagePreference();
                var previousSessionListDisplayOrder = SessionListDisplayOrder.Normalize(_appConfigSnapshot.SessionListDisplayOrder);
                form.SetTargetDisplayName(_fixedTarget == null
                    ? GetCurrentRuntimeDisplayName() : TargetManagerForm.DisplayName(_fixedTarget));
                form.SetMainWindowHotKeyStateText(GetMainWindowHotKeyStateText());
                form.ValidateSelection = () =>
                {
                    officialSettings = form.HasOfficialCodexSettings && officialSnapshot != null
                        ? CreateOfficialCodexSettingsSelection(form.OfficialCodexSettings)
                        : null;
                    if (officialSettings != null &&
                        OfficialCodexSettingsEditPlanner.BuildEdits(officialSnapshot, officialSettings).Count == 0)
                    {
                        officialSettings = null;
                    }

                    if (officialSettings != null && !ConfirmOfficialOptionsUpdate(form))
                    {
                        return false;
                    }

                    string error;
                    if (TrySaveOptionsLocalSettings(form, out updatedLocalOptions, out error))
                    {
                        return true;
                    }

                    form.SetMainWindowHotKeyStateText(GetMainWindowHotKeyStateText());
                    MessageBox.Show(form, error, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                };
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (updatedLocalOptions)
                {
                    if (previousSessionListDisplayOrder != SessionListDisplayOrder.Normalize(_appConfigSnapshot.SessionListDisplayOrder))
                    {
                        RefreshSessionList();
                    }
                    UpdateChatHistorySpeechMenuState();
                    ApplyModeVisibility();
                    if (!string.Equals(previousTheme, GetConfiguredUiThemePreference(), StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyConfiguredTheme();
                    }
                    if (!string.Equals(previousLanguage, GetConfiguredUiLanguagePreference(), StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyConfiguredUiLanguage();
                    }
                }
                var updatedOfficialOptions = false;
                var updatedModeSettings = false;
                var officeRootChanged = false;
                var modeSettingsSaveFailed = false;
                if (modeSettingsAvailable)
                {
                    try
                    {
                        var modeSettingsResult = await SaveModeSettingsForCurrentRuntimeAsync(
                            form,
                            officeTarget);
                        updatedModeSettings = modeSettingsResult.Item1;
                        officeRootChanged = modeSettingsResult.Item2;
                    }
                    catch (Exception ex)
                    {
                        modeSettingsSaveFailed = true;
                        AppendLog(ex.Message);
                        var message = string.Format(
                            UiLocalization.Text(
                                "OptionsForm.ModeSettings.SaveFailed.Template",
                                "The mode settings for the current runtime could not be saved: {0}"),
                            ex.Message);
                        MessageBox.Show(
                            this,
                            message,
                            AppConstants.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        SetHighPriorityStatus(message);
                    }
                }
                if (officialSettings != null)
                {
                    updatedOfficialOptions = await TryWriteOfficialCodexSettingsAsync(officialSettings);
                }

                if (officeRootChanged)
                {
                    await RefreshActiveOfficeRuntimeRootAsync();
                }

                if (!modeSettingsSaveFailed &&
                    (updatedLocalOptions || updatedModeSettings || updatedOfficialOptions))
                {
                    SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.OptionsUpdated", "Options updated"));
                }
                else if (!modeSettingsSaveFailed)
                {
                    SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.OptionsUnchanged", "Options unchanged"));
                }

                if (officeRootChanged && !modeSettingsSaveFailed)
                {
                    SetHighPriorityStatus(UiLocalization.Text(
                        "MainForm.Status.OfficeDirectoryChangedFilesNotMoved",
                        "Office directory changed. Existing files were not moved."));
                }

                if (form.OfficeCliEnvironmentChanged)
                {
                    await RestartAppServerAfterOfficeCliEnvironmentChangeAsync();
                }
            }
        }

        private async Task<Tuple<bool, bool>> SaveModeSettingsForCurrentRuntimeAsync(
            OptionsForm form,
            OfficeRuntimeTarget officeTarget)
        {
            var officePathComparison = officeTarget.HostPlatform == AppServerHostPlatform.Windows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var officeRootChanged = !string.Equals(
                NormalizeOptionalText(_activeModeSettings.OfficeRootPath),
                NormalizeOptionalText(form.OfficeRootPath),
                officePathComparison);
            var startupModeChanged = _activeModeSettings.DefaultStartupMode != form.DefaultStartupMode;
            if (!officeRootChanged && !startupModeChanged)
            {
                return Tuple.Create(false, false);
            }

            if (officeRootChanged)
            {
                await ResolveAndEnsureOfficeRootAsync(officeTarget, form.OfficeRootPath);
            }

            await SaveModeSettingsForCurrentRuntimeAsync(new ModeSettingsSnapshot
            {
                DefaultStartupMode = form.DefaultStartupMode,
                OfficeRootPath = form.OfficeRootPath
            });
            return Tuple.Create(true, officeRootChanged);
        }

        private bool TrySaveOptionsLocalSettings(OptionsForm form, out bool updated, out string error)
        {
            var previous = _appConfigSnapshot;
            updated = false;
            error = null;
            try
            {
                // Keep the live snapshot untouched until the complete target preference write succeeds.
                var serializer = new DataContractJsonSerializer(typeof(AppConfigSnapshot));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, previous ?? new AppConfigSnapshot());
                    stream.Position = 0;
                    _appConfigSnapshot = (AppConfigSnapshot)serializer.ReadObject(stream);
                }

                var hotKeyChanged = (_appConfigSnapshot.MainWindowHotKeyEnabled != false) != form.MainWindowHotKeyEnabled ||
                    MainWindowHotKeyConfig.NormalizeModifiers(_appConfigSnapshot.MainWindowHotKeyModifiers) != form.MainWindowHotKeyModifiers ||
                    MainWindowHotKeyConfig.NormalizeVirtualKey(_appConfigSnapshot.MainWindowHotKeyVirtualKey) != form.MainWindowHotKeyVirtualKey;
                updated = ApplyOptionsFormLocalSettings(form) || hotKeyChanged;
                Action persist = () =>
                {
                    if (!SaveAppConfig())
                    {
                        throw new InvalidOperationException(UiLocalization.Text(
                            "MainForm.Status.SaveConfigFailedPrefix", "Save config failed: "));
                    }
                };
                if (hotKeyChanged)
                {
                    if (!TryUpdateMainWindowHotKey(form.MainWindowHotKeyEnabled,
                        form.MainWindowHotKeyModifiers, form.MainWindowHotKeyVirtualKey, persist, out error))
                    {
                        _appConfigSnapshot = previous;
                        updated = false;
                        return false;
                    }
                }
                else if (updated)
                {
                    persist();
                }
                else
                {
                    _appConfigSnapshot = previous;
                }

                return true;
            }
            catch (Exception ex)
            {
                _appConfigSnapshot = previous;
                updated = false;
                error = ex.Message;
                return false;
            }
        }

        private bool ApplyOptionsFormLocalSettings(OptionsForm form)
        {
            var updated = false;
            var nextThemePreference = UiThemePreference.Normalize(form.UiThemePreference);
            var nextLanguagePreference = UiLocalization.NormalizePreference(form.UiLanguagePreference);
            var themeChanged = !string.Equals(GetConfiguredUiThemePreference(), nextThemePreference, StringComparison.OrdinalIgnoreCase);
            var languageChanged = !string.Equals(GetConfiguredUiLanguagePreference(), nextLanguagePreference, StringComparison.OrdinalIgnoreCase);

            if (_appConfigSnapshot == null)
            {
                _appConfigSnapshot = new AppConfigSnapshot();
            }

            if (_appConfigSnapshot.UiFontScalePercent != form.UiFontScalePercent)
            {
                _appConfigSnapshot.UiFontScalePercent = form.UiFontScalePercent;
                updated = true;
            }

            if (themeChanged)
            {
                _appConfigSnapshot.UiThemePreference = nextThemePreference;
                updated = true;
            }

            if (languageChanged)
            {
                _appConfigSnapshot.UiLanguagePreference = nextLanguagePreference;
                updated = true;
            }

            if (_appConfigSnapshot.TurnCompletedNotificationMode != form.TurnCompletedNotificationMode)
            {
                _appConfigSnapshot.TurnCompletedNotificationMode = form.TurnCompletedNotificationMode;
                updated = true;
            }

            if (_appConfigSnapshot.SubAgentTurnCompletedNotificationMode != form.SubAgentTurnCompletedNotificationMode)
            {
                _appConfigSnapshot.SubAgentTurnCompletedNotificationMode = form.SubAgentTurnCompletedNotificationMode;
                updated = true;
            }

            if (_appConfigSnapshot.ApprovalRequestNotificationMode != form.ApprovalRequestNotificationMode)
            {
                _appConfigSnapshot.ApprovalRequestNotificationMode = form.ApprovalRequestNotificationMode;
                updated = true;
            }

            if (_appConfigSnapshot.SendMessageSoundEnabled != form.SendMessageSoundEnabled)
            {
                _appConfigSnapshot.SendMessageSoundEnabled = form.SendMessageSoundEnabled;
                updated = true;
            }

            if (_appConfigSnapshot.CloseSessionWorkbenchOnEscape != form.CloseSessionWorkbenchOnEscape)
            {
                _appConfigSnapshot.CloseSessionWorkbenchOnEscape = form.CloseSessionWorkbenchOnEscape;
                updated = true;
            }

            var nextActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Normalize(form.ActiveTurnFollowUpMode);
            if (!string.Equals(
                    ActiveTurnFollowUpMode.Normalize(_appConfigSnapshot.ActiveTurnFollowUpMode),
                    nextActiveTurnFollowUpMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _appConfigSnapshot.ActiveTurnFollowUpMode = nextActiveTurnFollowUpMode;
                updated = true;
            }

            var nextSessionListDisplayOrder = SessionListDisplayOrder.Normalize(form.SessionListDisplayOrder);
            if (SessionListDisplayOrder.Normalize(_appConfigSnapshot.SessionListDisplayOrder) != nextSessionListDisplayOrder)
            {
                _appConfigSnapshot.SessionListDisplayOrder = nextSessionListDisplayOrder;
                updated = true;
            }

            var nextMessageDetailPreviewMode = MessageDetailPreviewMode.Normalize(form.MessageDetailPreviewMode);
            if (!string.Equals(
                    MessageDetailPreviewMode.Normalize(_appConfigSnapshot.MessageDetailPreviewMode),
                    nextMessageDetailPreviewMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _appConfigSnapshot.MessageDetailPreviewMode = nextMessageDetailPreviewMode;
                updated = true;
            }

            var nextSendMessageShortcutMode = SendMessageShortcutMode.Normalize(form.SendMessageShortcutMode);
            if (!string.Equals(
                    SendMessageShortcutMode.Normalize(_appConfigSnapshot.SendMessageShortcutMode),
                    nextSendMessageShortcutMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _appConfigSnapshot.SendMessageShortcutMode = nextSendMessageShortcutMode;
                updated = true;
            }

            var nextTextFileOpenMode = TextFileMode.Normalize(form.TextFileOpenMode);
            if (!string.Equals(
                    TextFileMode.Normalize(_appConfigSnapshot.TextFileOpenMode),
                    nextTextFileOpenMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _appConfigSnapshot.TextFileOpenMode = nextTextFileOpenMode;
                updated = true;
            }

            if (!string.Equals(
                    NormalizeOptionalText(_appConfigSnapshot.TextFileExternalEditorPath),
                    NormalizeOptionalText(form.TextFileExternalEditorPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                _appConfigSnapshot.TextFileExternalEditorPath = form.TextFileExternalEditorPath;
                updated = true;
            }

            if (!string.Equals(
                    NormalizeOptionalText(_appConfigSnapshot.TextFileExternalEditorArguments),
                    NormalizeOptionalText(form.TextFileExternalEditorArguments),
                    StringComparison.Ordinal))
            {
                _appConfigSnapshot.TextFileExternalEditorArguments = form.TextFileExternalEditorArguments;
                updated = true;
            }

            if (_appConfigSnapshot.ScreenReaderProvider != form.ScreenReaderProvider)
            {
                _appConfigSnapshot.ScreenReaderProvider = form.ScreenReaderProvider;
                updated = true;
            }

            if (_appConfigSnapshot.EnableAutomaticChatReading != form.EnableAutomaticChatReading)
            {
                _appConfigSnapshot.EnableAutomaticChatReading = form.EnableAutomaticChatReading;
                updated = true;
            }

            if (_appConfigSnapshot.ManualBrowseUseReaderMainChannel != form.ManualBrowseUseReaderMainChannel)
            {
                _appConfigSnapshot.ManualBrowseUseReaderMainChannel = form.ManualBrowseUseReaderMainChannel;
                updated = true;
            }

            if (!string.Equals(
                    NormalizeOptionalText(_appConfigSnapshot.ScreenReaderIndependentChannelName),
                    NormalizeOptionalText(form.ScreenReaderIndependentChannelName),
                    StringComparison.Ordinal))
            {
                _appConfigSnapshot.ScreenReaderIndependentChannelName = form.ScreenReaderIndependentChannelName;
                updated = true;
            }

            if (_appConfigSnapshot.OfficeShowActivityPane != form.OfficeShowActivityPane)
            {
                _appConfigSnapshot.OfficeShowActivityPane = form.OfficeShowActivityPane;
                updated = true;
            }

            if (!AreCustomApprovalSettingsEqual(_appConfigSnapshot.CustomApprovalSettings, form.CustomApprovalSettings))
            {
                _appConfigSnapshot.CustomApprovalSettings = CloneCustomApprovalSettings(form.CustomApprovalSettings);
                updated = true;
            }

            return updated;
        }

        private static bool AreCustomApprovalSettingsEqual(
            CustomApprovalSettingsSnapshot left,
            CustomApprovalSettingsSnapshot right)
        {
            var leftValues = new HashSet<CustomApprovalPermissionKind>(
                left == null ? new CustomApprovalPermissionKind[0] : left.EnabledPermissions);
            var rightValues = new HashSet<CustomApprovalPermissionKind>(
                right == null ? new CustomApprovalPermissionKind[0] : right.EnabledPermissions);
            return leftValues.SetEquals(rightValues) &&
                AreCustomApprovalCommandPrefixesEqual(
                    left == null ? new string[0] : left.CommandPrefixes,
                    right == null ? new string[0] : right.CommandPrefixes);
        }

        private static bool AreCustomApprovalCommandPrefixesEqual(
            IEnumerable<string> left,
            IEnumerable<string> right)
        {
            var leftPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(left);
            var rightPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(right);
            if (leftPrefixes.Count != rightPrefixes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftPrefixes.Count; i++)
            {
                if (!string.Equals(leftPrefixes[i], rightPrefixes[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static CustomApprovalSettingsSnapshot CloneCustomApprovalSettings(CustomApprovalSettingsSnapshot source)
        {
            return new CustomApprovalSettingsSnapshot
            {
                EnabledPermissions = new List<CustomApprovalPermissionKind>(
                    source == null ? new CustomApprovalPermissionKind[0] : source.EnabledPermissions),
                CommandPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(
                    source == null ? new string[0] : source.CommandPrefixes).ToList()
            };
        }

        private TextFileOpenPreference BuildTextFileOpenPreference()
        {
            if (_appConfigSnapshot == null)
            {
                LoadAppConfig();
            }

            var snapshot = _appConfigSnapshot ?? new AppConfigSnapshot();
            return TextFileOpenPreferenceComposer.FromSnapshot(snapshot);
        }
    }
}
