using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Codex;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class WslAppServerSettingsForm
    {
        private bool _targetEditing;
        private bool _isNewTarget;
        private bool _isTargetSaveBusy;
        private string _targetKind;
        private string _expectedRuntimeRevision;
        private TargetRuntimeSettings _initialTargetSettings;
        private TextBox _targetNameTextBox;
        private ComboBox _targetDistributionComboBox;
        private TableLayoutPanel _targetDistributionPanel;
        private Button _refreshDistributionsButton;
        private Func<TargetRuntimeSettings, Task<bool>> _saveTargetAsync;
        private Func<CancellationToken, Task<IList<string>>> _listWslDistributionsAsync;

        public WslAppServerSettingsForm(
            TargetProfileSnapshot target,
            bool isNew,
            TargetRuntimeSettings settings,
            CodexRuntimeManager runtimeManager,
            CodexRuntimeUpdateService runtimeUpdateService,
            IAppServerWindowsSandboxClient windowsSandboxClient,
            Action<string> announce,
            Func<TargetRuntimeSettings, Task<bool>> saveAsync,
            Func<CancellationToken, Task<IList<string>>> listWslDistributionsAsync = null)
            : this(
                GetTargetRuntimeSource(target, settings),
                settings?.CodexCustomExecutablePath,
                settings?.WindowsShellPreference,
                settings?.WslAppServer,
                null,
                CreateTargetRemoteProfiles(target, isNew, settings),
                target?.SshProfileId,
                runtimeManager,
                runtimeUpdateService,
                target != null && string.Equals(target.Kind, "Windows", StringComparison.Ordinal) ? windowsSandboxClient : null,
                announce)
        {
            _targetKind = target.Kind;
            _isNewTarget = isNew;
            _saveTargetAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
            _listWslDistributionsAsync = listWslDistributionsAsync;
            _expectedRuntimeRevision = settings?.ExpectedRuntimeRevision;
            ConfigureTargetEditor(settings ?? new TargetRuntimeSettings());
            _targetEditing = true;
            _initialTargetSettings = BuildTargetRuntimeSettings();
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
        }

        public void SetTargetSaveStatus(string text)
        {
            SetDiagnosticsText(text ?? string.Empty);
        }

        public void MarkTargetSaved(TargetRuntimeSettings saved)
        {
            if (saved == null)
            {
                throw new ArgumentNullException(nameof(saved));
            }
            BundledRuntimeUpdated = false;

            _initialTargetSettings = new TargetRuntimeSettings
            {
                ExpectedRuntimeRevision = saved.ExpectedRuntimeRevision,
                Name = saved.Name,
                CodexRuntimeSource = saved.CodexRuntimeSource,
                CodexCustomExecutablePath = saved.CodexCustomExecutablePath,
                WindowsShellPreference = saved.WindowsShellPreference,
                WslAppServer = CloneWslConfig(saved.WslAppServer),
                RemoteSsh = CloneRemoteMacConfig(saved.RemoteSsh)
            };
            _expectedRuntimeRevision = saved.ExpectedRuntimeRevision;
            _isNewTarget = false;
            _introLabel.Text = UiLocalization.Text("TargetSettingsForm.Intro.Existing", "Changes apply only to this target. Close an SSH target before changing its host, then detect the connection and confirm its host key. A different WSL distribution or SSH user requires a new target.");
            _targetNameTextBox.Text = saved.Name;
            if (string.Equals(_targetKind, "Ssh", StringComparison.Ordinal))
            {
                var profile = GetSelectedRemoteProfile();
                profile.Name = saved.Name;
                profile.Ssh = CloneRemoteMacConfig(saved.RemoteSsh);
                LoadSelectedRemoteProfileToFields();
            }
            else if (string.Equals(_targetKind, "Wsl", StringComparison.Ordinal))
            {
                _distributionTextBox.Text = saved.WslAppServer.DistributionName;
                if (_targetDistributionComboBox != null)
                {
                    _targetDistributionComboBox.Items.Clear();
                    _targetDistributionComboBox.Items.Add(saved.WslAppServer.DistributionName);
                    _targetDistributionComboBox.SelectedIndex = 0;
                }
            }

            UpdateTargetEditingControls();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isTargetSaveBusy)
            {
                e.Cancel = true;
            }

            base.OnFormClosing(e);
        }

        private void ConfigureTargetEditor(TargetRuntimeSettings settings)
        {
            Text = UiLocalization.Text("TargetSettingsForm.Title", "Target Settings");
            _introLabel.Text = _isNewTarget
                ? UiLocalization.Text("TargetSettingsForm.Intro.New", "Configure and test this target, then save it. Open saved targets from the Targets menu.")
                : UiLocalization.Text("TargetSettingsForm.Intro.Existing", "Changes apply only to this target. Close an SSH target before changing its host, then detect the connection and confirm its host key. A different WSL distribution or SSH user requires a new target.");
            _runtimeSourceComboBox.SelectedIndexChanged -= RuntimeSourceComboBox_SelectedIndexChanged;
            _runtimeSourceComboBox.Items.Clear();
            if (string.Equals(_targetKind, "Windows", StringComparison.Ordinal))
            {
                AddTargetRuntimeSource(CodexRuntimeSources.Bundled, UiLocalization.Text("WslAppServerSettingsForm.Source.Bundled", "Bundled"));
                AddTargetRuntimeSource(CodexRuntimeSources.System, UiLocalization.Text("WslAppServerSettingsForm.Source.System", "System"));
                AddTargetRuntimeSource(CodexRuntimeSources.Custom, UiLocalization.Text("WslAppServerSettingsForm.Source.Custom", "Custom"));
            }
            else if (string.Equals(_targetKind, "Wsl", StringComparison.Ordinal))
            {
                AddTargetRuntimeSource(CodexRuntimeSources.Wsl, UiLocalization.Text("WslAppServerSettingsForm.Source.Wsl", "WSL"));
            }
            else
            {
                AddTargetRuntimeSource(CodexRuntimeSources.RemoteUnixSsh, UiLocalization.Text("WslAppServerSettingsForm.Source.RemoteUnixSsh", "Remote Unix (SSH)"));
            }

            SelectRuntimeSource(GetTargetRuntimeSource(new TargetProfileSnapshot { Kind = _targetKind }, settings));
            _runtimeSourceComboBox.SelectedIndexChanged += RuntimeSourceComboBox_SelectedIndexChanged;
            _targetNameTextBox = CreateTextBox(UiLocalization.Text("TargetSettingsForm.Accessible.Name", "Name"), 0);
            _targetNameTextBox.MaxLength = 120;
            _targetNameTextBox.Text = settings.Name;
            _settingsLayout.SuspendLayout();
            _settingsLayout.RowCount++;
            for (var i = _settingsLayout.Controls.Count - 1; i >= 0; i--)
            {
                var control = _settingsLayout.Controls[i];
                var row = _settingsLayout.GetRow(control);
                if (row >= 1)
                {
                    _settingsLayout.SetRow(control, row + 1);
                }
            }

            _settingsLayout.RowStyles.Insert(1, new RowStyle(SizeType.AutoSize));
            AddRow(_settingsLayout, 1, UiLocalization.Text("TargetSettingsForm.Label.Name", "Name(&M)"), _targetNameTextBox);
            _settingsLayout.ResumeLayout(true);
            foreach (Control control in _settingsLayout.Controls)
            {
                control.TabIndex = _settingsLayout.GetRow(control);
            }
            _okButton.Text = UiLocalization.Text("TargetSettingsForm.Button.Save", "Save(&V)");
            _okButton.AccessibleName = UiLocalization.Text("TargetSettingsForm.Accessible.Save", "Save");
            _cancelButton.TabIndex = _okButton.TabIndex + 1;
            _settingsLayout.AutoScroll = true;
            if (_isNewTarget && string.Equals(_targetKind, "Wsl", StringComparison.Ordinal))
            {
                ConfigureNewWslDistributionPicker();
            }

            _windowsShellComboBox.SelectedIndexChanged += (sender, e) => UpdateTargetEditingControls();
            SetRowVisible(_windowsSandboxLabel, _windowsSandboxPanel, string.Equals(_targetKind, "Windows", StringComparison.Ordinal));
        }

        private void AddTargetRuntimeSource(string source, string text)
        {
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(source, text));
        }

        private void ConfigureNewWslDistributionPicker()
        {
            _targetDistributionComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.Distribution", "Distribution"),
                TabIndex = 4
            };
            UiTheme.ApplyComboBox(_targetDistributionComboBox);
            if (!string.IsNullOrWhiteSpace(_distributionTextBox.Text))
            {
                _targetDistributionComboBox.Items.Add(_distributionTextBox.Text.Trim());
                _targetDistributionComboBox.SelectedIndex = 0;
            }

            _targetDistributionComboBox.SelectedIndexChanged += (sender, e) =>
            {
                _distributionTextBox.Text = _targetDistributionComboBox.SelectedItem as string ?? string.Empty;
                UpdateDiagnosticsText(null);
            };
            _refreshDistributionsButton = new Button
            {
                Text = UiLocalization.Text("TargetSettingsForm.Button.RefreshDistributions", "Refresh Distributions(&R)"),
                AccessibleName = UiLocalization.Text("TargetSettingsForm.Accessible.RefreshDistributions", "Refresh distributions"),
                AutoSize = true,
                TabIndex = 5
            };
            UiTheme.ApplyDialogButton(_refreshDistributionsButton, UiGlyph.Refresh);
            FormInteractionConventions.ConfigureRefreshButton(_refreshDistributionsButton, true);
            _refreshDistributionsButton.Click += async (sender, e) => await RefreshTargetDistributionsAsync();
            _targetDistributionPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                TabIndex = _settingsLayout.GetRow(_distributionTextBox),
                TabStop = false
            };
            _targetDistributionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _targetDistributionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _targetDistributionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _targetDistributionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _targetDistributionPanel.Controls.Add(_targetDistributionComboBox, 0, 0);
            _targetDistributionPanel.Controls.Add(_refreshDistributionsButton, 1, 0);
            var row = _settingsLayout.GetRow(_distributionTextBox);
            _settingsLayout.Controls.Remove(_distributionTextBox);
            _distributionTextBox.Visible = false;
            _distributionTextBox.TabStop = false;
            _targetDistributionPanel.Controls.Add(_distributionTextBox, 0, 1);
            _settingsLayout.Controls.Add(_targetDistributionPanel, 1, row);
        }

        private async Task RefreshTargetDistributionsAsync()
        {
            if (_listWslDistributionsAsync == null || _isRuntimeOperationBusy || !_isNewTarget)
            {
                return;
            }

            var selected = _targetDistributionComboBox.SelectedItem as string;
            SetRuntimeUpdateBusy(true);
            SetDiagnosticsText(UiLocalization.Text("TargetSettingsForm.Status.RefreshingDistributions", "Refreshing WSL distributions..."));
            try
            {
                var distributions = await _listWslDistributionsAsync(_lifetimeCts.Token);
                if (IsDisposed)
                {
                    return;
                }

                _targetDistributionComboBox.Items.Clear();
                foreach (var distribution in distributions ?? new string[0])
                {
                    if (!string.IsNullOrWhiteSpace(distribution) && !_targetDistributionComboBox.Items.Contains(distribution.Trim()))
                    {
                        _targetDistributionComboBox.Items.Add(distribution.Trim());
                    }
                }

                _targetDistributionComboBox.SelectedItem = selected;
                _distributionTextBox.Text = _targetDistributionComboBox.SelectedItem as string ?? string.Empty;
                SetDiagnosticsText(UiLocalization.Text("TargetSettingsForm.Status.DistributionsRefreshed", "Distributions refreshed. Select a distribution, then test or save this target."));
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                {
                    SetDiagnosticsText(UiLocalization.Text("TargetSettingsForm.Status.RefreshCancelled", "Distribution refresh was cancelled."));
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    SetDiagnosticsText(string.Format(UiLocalization.Text("TargetSettingsForm.Status.RefreshFailed.Template", "Distribution refresh failed ({0}). Retry using Refresh Distributions."), ex.GetType().Name));
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    SetRuntimeUpdateBusy(false);
                }
            }
        }

        private void UpdateTargetEditingControls()
        {
            if (!_targetEditing)
            {
                return;
            }

            _runtimeSourceComboBox.Enabled = string.Equals(_targetKind, "Windows", StringComparison.Ordinal) && !_isRuntimeOperationBusy;
            _remoteProfileComboBox.Enabled = false;
            _addRemoteProfileButton.Enabled = false;
            _deleteRemoteProfileButton.Enabled = false;
            SetRowVisible(_remoteProfileLabel, _remoteProfilePanel, false);
            SetRowVisible(_remoteAliasLabel, _remoteAliasTextBox, false);
            _remoteHostTextBox.ReadOnly = false;
            _remoteUserNameTextBox.ReadOnly = !_isNewTarget;
            _distributionTextBox.ReadOnly = true;
            _targetNameTextBox.Enabled = !_isRuntimeOperationBusy;
            if (_targetDistributionPanel != null)
            {
                _distributionTextBox.Visible = !_isNewTarget;
                _distributionTextBox.TabStop = !_isNewTarget;
                _targetDistributionPanel.Visible = IsWslSelected();
                _targetDistributionComboBox.Visible = _isNewTarget;
                _refreshDistributionsButton.Visible = _isNewTarget;
                _targetDistributionComboBox.Enabled = _isNewTarget && !_isRuntimeOperationBusy;
                _refreshDistributionsButton.Enabled = _isNewTarget && !_isRuntimeOperationBusy && _listWslDistributionsAsync != null;
            }
        }

        private bool HasUnsavedTargetWindowsLaunchSettings()
        {
            return _targetEditing && string.Equals(_targetKind, "Windows", StringComparison.Ordinal) && _initialTargetSettings != null &&
                (!string.Equals(GetSelectedWindowsShellPreference(), _initialTargetSettings.WindowsShellPreference, StringComparison.OrdinalIgnoreCase) ||
                 (IsCustomSelected() && !string.Equals((_customExecutableTextBox.Text ?? string.Empty).Trim(), _initialTargetSettings.CodexCustomExecutablePath ?? string.Empty, StringComparison.OrdinalIgnoreCase)));
        }

        private bool IsWindowsSandboxTargetAllowed()
        {
            return !_targetEditing || string.Equals(_targetKind, "Windows", StringComparison.Ordinal);
        }

        private async void SaveTargetFromButtonAsync()
        {
            await SaveTargetAsync();
        }

        private async Task SaveTargetAsync()
        {
            if (_isRuntimeOperationBusy || _isTargetSaveBusy || _isWindowsSandboxOperationBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_targetNameTextBox.Text))
            {
                ShowRuntimeValidationMessage(UiLocalization.Text("TargetSettingsForm.Message.NameRequired", "Enter a target name."), _targetNameTextBox);
                return;
            }

            if (IsWslSelected() && string.IsNullOrWhiteSpace(_distributionTextBox.Text))
            {
                ShowRuntimeValidationMessage(UiLocalization.Text("TargetSettingsForm.Message.DistributionRequired", "Refresh and select a WSL distribution."), _targetDistributionComboBox ?? (Control)_distributionTextBox);
                return;
            }

            if (!ValidateSelectedRuntimeSettings())
            {
                return;
            }

            var settings = BuildTargetRuntimeSettings();
            var savingText = UiLocalization.Text("TargetSettingsForm.Status.Saving", "Saving target settings...");
            _isTargetSaveBusy = true;
            _cancelButton.Enabled = false;
            SetRuntimeUpdateBusy(true);
            SetDiagnosticsText(savingText);
            var close = false;
            try
            {
                close = await _saveTargetAsync(settings);
                if (!close && string.Equals(_diagnosticsTextBox.Text, savingText, StringComparison.Ordinal))
                {
                    SetDiagnosticsText(UiLocalization.Text("TargetSettingsForm.Status.SaveNotCompleted", "The operation was not completed. Your draft is retained; retry or cancel."));
                }
            }
            catch (Exception ex)
            {
                SetDiagnosticsText(string.Format(UiLocalization.Text("TargetSettingsForm.Status.SaveFailed.Template", "The operation failed ({0}). Your draft is retained; retry or cancel."), ex.GetType().Name));
            }
            finally
            {
                _isTargetSaveBusy = false;
                if (!IsDisposed)
                {
                    _cancelButton.Enabled = true;
                    SetRuntimeUpdateBusy(false);
                }
            }

            if (close && !IsDisposed)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private TargetRuntimeSettings BuildTargetRuntimeSettings()
        {
            var isWsl = string.Equals(_targetKind, "Wsl", StringComparison.Ordinal);
            var isSsh = string.Equals(_targetKind, "Ssh", StringComparison.Ordinal);
            var settings = new TargetRuntimeSettings
            {
                ExpectedRuntimeRevision = _expectedRuntimeRevision,
                BundledRuntimeUpdated = BundledRuntimeUpdated,
                Name = (_targetNameTextBox.Text ?? string.Empty).Trim(),
                CodexRuntimeSource = GetSelectedRuntimeSource(),
                CodexCustomExecutablePath = string.IsNullOrWhiteSpace(_customExecutableTextBox.Text) ? null : _customExecutableTextBox.Text.Trim(),
                WindowsShellPreference = GetSelectedWindowsShellPreference(),
                WslAppServer = isWsl ? BuildWslConfigFromFields() : null,
                RemoteSsh = isSsh ? BuildRemoteMacConfigFromFields() : null
            };
            if (!_isNewTarget && _initialTargetSettings != null)
            {
                if (isWsl)
                {
                    settings.WslAppServer.DistributionName = _initialTargetSettings.WslAppServer.DistributionName;
                }
                else if (isSsh)
                {
                    settings.RemoteSsh.UserName = _initialTargetSettings.RemoteSsh.UserName;
                }
            }

            return settings;
        }

        private static string GetTargetRuntimeSource(TargetProfileSnapshot target, TargetRuntimeSettings settings)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (string.Equals(target.Kind, "Windows", StringComparison.Ordinal))
            {
                var source = CodexRuntimeSources.CanonicalizeForExecution(settings?.CodexRuntimeSource);
                return source == CodexRuntimeSources.System || source == CodexRuntimeSources.Custom ? source : CodexRuntimeSources.Bundled;
            }

            if (string.Equals(target.Kind, "Wsl", StringComparison.Ordinal))
            {
                return CodexRuntimeSources.Wsl;
            }

            if (string.Equals(target.Kind, "Ssh", StringComparison.Ordinal))
            {
                return CodexRuntimeSources.RemoteUnixSsh;
            }

            throw new ArgumentException("Unsupported target kind.", nameof(target));
        }

        private static IList<RemoteUnixSshProfileConfigSnapshot> CreateTargetRemoteProfiles(TargetProfileSnapshot target, bool isNew, TargetRuntimeSettings settings)
        {
            var config = CloneRemoteMacConfig(settings?.RemoteSsh);
            if (isNew)
            {
                config.ProtectedPassword = null;
                config.HostKeyFingerprint = null;
            }

            return new[]
            {
                new RemoteUnixSshProfileConfigSnapshot
                {
                    Id = string.IsNullOrWhiteSpace(target?.SshProfileId) ? "remote-unix-" + Guid.NewGuid().ToString("N") : target.SshProfileId,
                    Name = settings?.Name,
                    Ssh = config
                }
            };
        }

        internal Task SaveTargetForTestsAsync()
        {
            return SaveTargetAsync();
        }

        internal Task RefreshTargetDistributionsForTestsAsync()
        {
            return RefreshTargetDistributionsAsync();
        }
    }
}
