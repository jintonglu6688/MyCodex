using System;
using System.IO;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class OptionsForm
    {
        private void OfficeRootBrowseButton_Click(object sender, EventArgs e)
        {
            string selectedPath;
            var initialPath = GetOfficeRootBrowseInitialPath();
            if (_officeDirectoryOptionsContext.HostPlatform == AppServerHostPlatform.Windows)
            {
                if (!WindowsFolderPicker.TryPickFolder(
                    this,
                    UiLocalization.Text("OptionsForm.OfficeRootPath.BrowseDescription", "Select office directory"),
                    initialPath,
                    out selectedPath))
                {
                    return;
                }
            }
            else
            {
                selectedPath = _officeDirectoryOptionsContext.BrowseDirectory == null
                    ? null
                    : _officeDirectoryOptionsContext.BrowseDirectory(this, initialPath);
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            SetOfficeRootPathText(selectedPath.Trim(), false);
        }

        private void OfficeRootRestoreDefaultButton_Click(object sender, EventArgs e)
        {
            SetOfficeRootPathText(GetDefaultOfficeRootDisplayPath(), true);
            _officeRootPathTextBox.Focus();
        }

        private void OfficeRootPathTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingOfficeRootPath)
            {
                return;
            }

            _officeRootRepresentsDefault = false;
            UpdateOfficeRootStatus(false);
        }

        internal static bool TryNormalizeOfficeRootPath(
            AppServerHostPlatform hostPlatform,
            string value,
            out string normalizedPath)
        {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                var officeWorkspaceService = new OfficeWorkspaceService();
                if (hostPlatform == AppServerHostPlatform.Windows)
                {
                    var trimmed = value.Trim();
                    if (!WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(trimmed))
                    {
                        return false;
                    }

                    normalizedPath = Path.GetFullPath(trimmed);
                    return WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(normalizedPath);
                }

                normalizedPath = officeWorkspaceService.NormalizeConfiguredRootPath(hostPlatform, value);
                return !string.IsNullOrWhiteSpace(normalizedPath);
            }
            catch (ArgumentException)
            {
                normalizedPath = null;
                return false;
            }
            catch (NotSupportedException)
            {
                normalizedPath = null;
                return false;
            }
            catch (IOException)
            {
                normalizedPath = null;
                return false;
            }
            catch (System.Security.SecurityException)
            {
                normalizedPath = null;
                return false;
            }
        }

        private static OfficeDirectoryOptionsContext CreateLocalOfficeDirectoryOptionsContext(
            ModeSettingsSnapshot modeSettings)
        {
            var officeWorkspaceService = new OfficeWorkspaceService();
            var configuredRootPath = modeSettings == null ? null : modeSettings.OfficeRootPath;
            return new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = UiLocalization.Text("MainForm.Runtime.Windows", "Windows"),
                HostPlatform = AppServerHostPlatform.Windows,
                ConfiguredRootPath = configuredRootPath,
                EffectiveRootPath = officeWorkspaceService.ResolveOfficeRootPath(configuredRootPath)
            };
        }

        private void ApplyModeSettingsAvailability()
        {
            var available = _officeDirectoryOptionsContext.ModeSettingsAvailable;
            _startupModeComboBox.Enabled = available;
            _officeRootPathTextBox.Enabled = available;
            _officeRootBrowseButton.Enabled = available;
            _officeRootRestoreDefaultButton.Enabled = available;
            if (!available)
            {
                _officeRootStatusLabel.Text = string.IsNullOrWhiteSpace(
                        _officeDirectoryOptionsContext.ModeSettingsError)
                    ? UiLocalization.Text(
                        "OptionsForm.ModeSettings.Unavailable",
                        "Mode settings are unavailable for the current runtime.")
                    : _officeDirectoryOptionsContext.ModeSettingsError;
            }
        }

        private void InitializeOfficeRootPath()
        {
            _officeRootRepresentsDefault = string.IsNullOrWhiteSpace(
                _officeDirectoryOptionsContext.ConfiguredRootPath);
            var displayPath = _officeRootRepresentsDefault
                ? GetDefaultOfficeRootDisplayPath()
                : _officeDirectoryOptionsContext.ConfiguredRootPath.Trim();
            SetOfficeRootPathText(displayPath, _officeRootRepresentsDefault);
        }

        private string GetDefaultOfficeRootDisplayPath()
        {
            if (string.IsNullOrWhiteSpace(_officeDirectoryOptionsContext.ConfiguredRootPath) &&
                !string.IsNullOrWhiteSpace(_officeDirectoryOptionsContext.EffectiveRootPath))
            {
                return _officeDirectoryOptionsContext.EffectiveRootPath.Trim();
            }

            return _officeDirectoryOptionsContext.HostPlatform == AppServerHostPlatform.Windows
                ? new OfficeWorkspaceService().ResolveOfficeRootPath(null)
                : "~/MyCodex-office";
        }

        private string GetOfficeRootBrowseInitialPath()
        {
            if (_officeDirectoryOptionsContext.HostPlatform == AppServerHostPlatform.Windows)
            {
                return string.IsNullOrWhiteSpace(_officeRootPathTextBox.Text)
                    ? new OfficeWorkspaceService().ResolveOfficeRootPath(null)
                    : _officeRootPathTextBox.Text.Trim();
            }

            if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(_officeRootPathTextBox.Text))
            {
                return WorkspacePathUtilities.NormalizeUnixAbsolutePath(_officeRootPathTextBox.Text);
            }

            if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(
                _officeDirectoryOptionsContext.EffectiveRootPath))
            {
                return WorkspacePathUtilities.NormalizeUnixAbsolutePath(
                    _officeDirectoryOptionsContext.EffectiveRootPath);
            }

            return "/";
        }

        private void SetOfficeRootPathText(string value, bool representsDefault)
        {
            _isUpdatingOfficeRootPath = true;
            try
            {
                _officeRootPathTextBox.Text = value ?? string.Empty;
                _officeRootRepresentsDefault = representsDefault;
                UpdateOfficeRootStatus(representsDefault);
            }
            finally
            {
                _isUpdatingOfficeRootPath = false;
            }
        }

        private void UpdateOfficeRootStatus(bool representsDefault)
        {
            var isUnix = _officeDirectoryOptionsContext.HostPlatform != AppServerHostPlatform.Windows;
            var configuredPath = _officeRootPathTextBox.Text == null
                ? string.Empty
                : _officeRootPathTextBox.Text.Trim().Trim('"');
            var requiresHomeResolution = representsDefault ||
                string.Equals(configuredPath, "~", StringComparison.Ordinal) ||
                configuredPath.StartsWith("~/", StringComparison.Ordinal) ||
                string.Equals(configuredPath, "$HOME", StringComparison.Ordinal) ||
                configuredPath.StartsWith("$HOME/", StringComparison.Ordinal);
            var capturedConfiguredPath = string.IsNullOrWhiteSpace(
                    _officeDirectoryOptionsContext.ConfiguredRootPath)
                ? string.Empty
                : _officeDirectoryOptionsContext.ConfiguredRootPath.Trim().Trim('"');
            var effectivePathMatchesDisplay = representsDefault
                ? string.IsNullOrWhiteSpace(capturedConfiguredPath)
                : string.Equals(configuredPath, capturedConfiguredPath, StringComparison.Ordinal);
            var isUnavailable = isUnix &&
                requiresHomeResolution &&
                (!effectivePathMatchesDisplay ||
                 string.IsNullOrWhiteSpace(_officeDirectoryOptionsContext.EffectiveRootPath));
            _officeRootStatusLabel.Text = isUnavailable
                ? UiLocalization.Text(
                    "OptionsForm.OfficeRootPath.DefaultUnavailable",
                    "The effective office directory is unavailable until the runtime home directory can be resolved.")
                : string.Empty;
        }
    }
}
