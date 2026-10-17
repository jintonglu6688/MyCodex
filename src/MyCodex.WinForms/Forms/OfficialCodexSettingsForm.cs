using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class OfficialCodexSettingsForm : Form
    {
        private readonly ComboBox _reasoningSummaryComboBox;
        private readonly ComboBox _verbosityComboBox;
        private readonly CheckBox _serviceTierCheckBox;
        private readonly ComboBox _webSearchModeComboBox;
        private readonly ComboBox _webSearchContextSizeComboBox;
        private readonly ComboBox _viewImageToolComboBox;
        private readonly ComboBox _toolOutputTokenLimitComboBox;
        private readonly ComboBox _projectDocMaxBytesComboBox;
        private readonly TextBox _descriptionTextBox;
        private readonly string _permissionProfilesText;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private Control _activeSettingControl;
        private string _initialServiceTier;
        private bool _serviceTierSelectionChanged;

        public OfficialCodexSettingsForm(AppServerConfigSnapshot snapshot)
            : this(snapshot, null, null, true)
        {
        }

        internal OfficialCodexSettingsForm(AppServerConfigSnapshot snapshot, bool showDialogButtons)
            : this(snapshot, null, null, showDialogButtons)
        {
        }

        internal OfficialCodexSettingsForm(
            AppServerConfigSnapshot snapshot,
            IEnumerable<AppServerPermissionProfileSummary> permissionProfiles,
            bool showDialogButtons)
            : this(snapshot, null, permissionProfiles, showDialogButtons)
        {
        }

        internal OfficialCodexSettingsForm(
            AppServerConfigSnapshot snapshot,
            IEnumerable<AppServerModelOption> availableModels,
            IEnumerable<AppServerPermissionProfileSummary> permissionProfiles,
            bool showDialogButtons)
        {
            Text = UiLocalization.Text("OfficialCodexSettingsForm.Title", "Codex Official Settings");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(680, 560);
            Size = new Size(760, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 212F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            for (var i = 0; i < 8; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _reasoningSummaryComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ReasoningSummary", "Reasoning summary"), 0);
            _verbosityComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.Verbosity", "Verbosity"), 1);
            _serviceTierCheckBox = CreateCheckBox(
                UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ServiceTier", "Fast mode"),
                UiLocalization.Text("OfficialCodexSettingsForm.ServiceTier.FastMode", "Enable fast mode"),
                2);
            _webSearchModeComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.WebSearchMode", "Web search mode"), 3);
            _webSearchContextSizeComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.WebSearchContext", "Search context"), 4);
            _viewImageToolComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ViewImage", "Image viewing"), 5);
            _toolOutputTokenLimitComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ToolOutputLimit", "Tool output limit"), 6);
            _projectDocMaxBytesComboBox = CreateComboBox(UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ProjectDocLimit", "Project document limit"), 7);

            _descriptionTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("OfficialCodexSettingsForm.Accessible.Description", "Description"),
                TabIndex = 8
            };
            UiTheme.ApplyTextBox(_descriptionTextBox, false, true);
            _permissionProfilesText = permissionProfiles == null
                ? null
                : FormatPermissionProfiles(permissionProfiles);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 9
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 10,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            AddRow(root, 0, UiLocalization.Text("OfficialCodexSettingsForm.Label.ReasoningSummary", "Reasoning Summary"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ReasoningSummary", "Reasoning summary"), _reasoningSummaryComboBox);
            AddRow(root, 1, UiLocalization.Text("OfficialCodexSettingsForm.Label.Verbosity", "Verbosity"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.Verbosity", "Verbosity"), _verbosityComboBox);
            AddRow(root, 2, UiLocalization.Text("OfficialCodexSettingsForm.Label.ServiceTier", "Fast Mode"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ServiceTier", "Fast mode"), _serviceTierCheckBox);
            AddRow(root, 3, UiLocalization.Text("OfficialCodexSettingsForm.Label.WebSearchMode", "Web Search"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.WebSearchMode", "Web search mode"), _webSearchModeComboBox);
            AddRow(root, 4, UiLocalization.Text("OfficialCodexSettingsForm.Label.WebSearchContext", "Search Context"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.WebSearchContext", "Search context"), _webSearchContextSizeComboBox);
            AddRow(root, 5, UiLocalization.Text("OfficialCodexSettingsForm.Label.ViewImage", "Image Viewing"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ViewImage", "Image viewing"), _viewImageToolComboBox);
            AddRow(root, 6, UiLocalization.Text("OfficialCodexSettingsForm.Label.ToolOutputLimit", "Tool Output Limit"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ToolOutputLimit", "Tool output limit"), _toolOutputTokenLimitComboBox);
            AddRow(root, 7, UiLocalization.Text("OfficialCodexSettingsForm.Label.ProjectDocLimit", "Project Document Limit"), UiLocalization.Text("OfficialCodexSettingsForm.Accessible.ProjectDocLimit", "Project document limit"), _projectDocMaxBytesComboBox);

            var descriptionLabel = new Label
            {
                Text = UiLocalization.Text("OfficialCodexSettingsForm.Label.Description", "Description"),
                AccessibleName = UiLocalization.Text("OfficialCodexSettingsForm.Accessible.Description", "Description"),
                AutoSize = true,
                TextAlign = ContentAlignment.TopLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(descriptionLabel, true);
            root.Controls.Add(descriptionLabel, 0, 8);
            root.Controls.Add(_descriptionTextBox, 1, 8);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Visible = showDialogButtons;
            root.Controls.Add(buttonPanel, 0, 9);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            PopulateChoices();
            ApplySnapshot(snapshot ?? new AppServerConfigSnapshot());
            HookChangeEvents();
            UpdateDescription();
        }

        public string ModelReasoningSummary { get; private set; }

        public string ModelVerbosity { get; private set; }

        public string ServiceTier { get; private set; }

        public string WebSearchMode { get; private set; }

        public string WebSearchContextSize { get; private set; }

        public bool? ViewImageToolEnabled { get; private set; }

        public long? ToolOutputTokenLimit { get; private set; }

        public long? ProjectDocMaxBytes { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _reasoningSummaryComboBox.Focus();
        }

        private static ComboBox CreateComboBox(string accessibleName, int tabIndex)
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static CheckBox CreateCheckBox(string accessibleName, string text, int tabIndex)
        {
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Text = text,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        private static void AddRow(TableLayoutPanel root, int row, string text, string accessibleName, Control control)
        {
            var label = new Label
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
        }

        private void PopulateChoices()
        {
            PopulateStringChoices(
                _reasoningSummaryComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem("auto", UiLocalization.Text("OfficialCodexSettingsForm.ReasoningSummary.Auto", "Auto")),
                    new SettingChoiceItem("concise", UiLocalization.Text("OfficialCodexSettingsForm.ReasoningSummary.Concise", "Concise")),
                    new SettingChoiceItem("detailed", UiLocalization.Text("OfficialCodexSettingsForm.ReasoningSummary.Detailed", "Detailed")),
                    new SettingChoiceItem("none", UiLocalization.Text("OfficialCodexSettingsForm.ReasoningSummary.None", "None"))
                });
            PopulateStringChoices(
                _verbosityComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem("low", UiLocalization.Text("OfficialCodexSettingsForm.Verbosity.Low", "Low")),
                    new SettingChoiceItem("medium", UiLocalization.Text("OfficialCodexSettingsForm.Verbosity.Medium", "Medium")),
                    new SettingChoiceItem("high", UiLocalization.Text("OfficialCodexSettingsForm.Verbosity.High", "High"))
                });
            PopulateStringChoices(
                _webSearchModeComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem("disabled", UiLocalization.Text("OfficialCodexSettingsForm.WebSearch.Disabled", "Disabled")),
                    new SettingChoiceItem("cached", UiLocalization.Text("OfficialCodexSettingsForm.WebSearch.Cached", "Cached")),
                    new SettingChoiceItem(
                        "indexed",
                        UiLocalization.Text(
                            "OfficialCodexSettingsForm.WebSearch.Indexed",
                            "Indexed")),
                    new SettingChoiceItem("live", UiLocalization.Text("OfficialCodexSettingsForm.WebSearch.Live", "Live"))
                });
            PopulateStringChoices(
                _webSearchContextSizeComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem("low", UiLocalization.Text("OfficialCodexSettingsForm.SearchContext.Low", "Low")),
                    new SettingChoiceItem("medium", UiLocalization.Text("OfficialCodexSettingsForm.SearchContext.Medium", "Medium")),
                    new SettingChoiceItem("high", UiLocalization.Text("OfficialCodexSettingsForm.SearchContext.High", "High"))
                });
            PopulateStringChoices(
                _viewImageToolComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem("true", UiLocalization.Text("OfficialCodexSettingsForm.Boolean.Enabled", "Enabled")),
                    new SettingChoiceItem("false", UiLocalization.Text("OfficialCodexSettingsForm.Boolean.Disabled", "Disabled"))
                });
            PopulateChoiceItems(
                _toolOutputTokenLimitComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem(8192L, UiLocalization.Text("OfficialCodexSettingsForm.ToolOutputLimit.8K", "8K")),
                    new SettingChoiceItem(16384L, UiLocalization.Text("OfficialCodexSettingsForm.ToolOutputLimit.16K", "16K")),
                    new SettingChoiceItem(32768L, UiLocalization.Text("OfficialCodexSettingsForm.ToolOutputLimit.32K", "32K"))
                });
            PopulateChoiceItems(
                _projectDocMaxBytesComboBox,
                new[]
                {
                    new SettingChoiceItem(null, UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default")),
                    new SettingChoiceItem(65536L, UiLocalization.Text("OfficialCodexSettingsForm.ProjectDocLimit.64KB", "64 KB")),
                    new SettingChoiceItem(131072L, UiLocalization.Text("OfficialCodexSettingsForm.ProjectDocLimit.128KB", "128 KB")),
                    new SettingChoiceItem(262144L, UiLocalization.Text("OfficialCodexSettingsForm.ProjectDocLimit.256KB", "256 KB"))
                });
        }

        private static void PopulateStringChoices(ComboBox comboBox, SettingChoiceItem[] items)
        {
            PopulateChoiceItems(comboBox, items);
        }

        private static void PopulateChoiceItems(ComboBox comboBox, SettingChoiceItem[] items)
        {
            comboBox.Items.Clear();
            foreach (var item in items)
            {
                comboBox.Items.Add(item);
            }

            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }

        private void ApplySnapshot(AppServerConfigSnapshot snapshot)
        {
            SelectStringChoice(_reasoningSummaryComboBox, GetUserStringSetting(snapshot, "model_reasoning_summary", snapshot.ModelReasoningSummary));
            SelectStringChoice(_verbosityComboBox, GetUserStringSetting(snapshot, "model_verbosity", snapshot.ModelVerbosity));
            _initialServiceTier = GetUserStringSetting(snapshot, "service_tier", snapshot.ServiceTier);
            _serviceTierCheckBox.ThreeState = false;
            _serviceTierCheckBox.Checked = StartupFastModePromptPlanner.IsFastServiceTier(_initialServiceTier);
            SelectStringChoice(_webSearchModeComboBox, GetUserStringSetting(snapshot, "web_search", snapshot.WebSearchMode));
            SelectStringChoice(_webSearchContextSizeComboBox, GetUserStringSetting(snapshot, "tools.web_search.context_size", snapshot.WebSearchContextSize));
            SelectStringChoice(_viewImageToolComboBox, GetUserBoolSetting(snapshot, "tools.view_image", snapshot.ViewImageToolEnabled));
            SelectLongChoice(_toolOutputTokenLimitComboBox, GetUserLongSetting(snapshot, "tool_output_token_limit", snapshot.ToolOutputTokenLimit));
            SelectLongChoice(_projectDocMaxBytesComboBox, GetUserLongSetting(snapshot, "project_doc_max_bytes", snapshot.ProjectDocMaxBytes));
        }

        private static long? GetUserLongSetting(AppServerConfigSnapshot snapshot, string keyPath, long? value)
        {
            return HasUserConfigOrigin(snapshot, keyPath) ? value : null;
        }

        private static string GetUserStringSetting(AppServerConfigSnapshot snapshot, string keyPath, string value)
        {
            return HasUserConfigOrigin(snapshot, keyPath) ? value : null;
        }

        private static string GetUserBoolSetting(AppServerConfigSnapshot snapshot, string keyPath, bool? value)
        {
            return HasUserConfigOrigin(snapshot, keyPath) && value.HasValue
                ? value.Value.ToString().ToLowerInvariant()
                : null;
        }

        private static bool HasUserConfigOrigin(AppServerConfigSnapshot snapshot, string keyPath)
        {
            if (snapshot == null || snapshot.Origins == null || string.IsNullOrWhiteSpace(keyPath))
            {
                return false;
            }

            AppServerConfigOrigin origin;
            return snapshot.Origins.TryGetValue(keyPath, out origin) &&
                origin != null &&
                string.Equals(origin.SourceType, "user", StringComparison.OrdinalIgnoreCase);
        }

        private void HookChangeEvents()
        {
            _reasoningSummaryComboBox.SelectedIndexChanged += SettingControl_Changed;
            _verbosityComboBox.SelectedIndexChanged += SettingControl_Changed;
            _serviceTierCheckBox.CheckedChanged += ServiceTierCheckBox_CheckedChanged;
            _webSearchModeComboBox.SelectedIndexChanged += SettingControl_Changed;
            _webSearchContextSizeComboBox.SelectedIndexChanged += SettingControl_Changed;
            _viewImageToolComboBox.SelectedIndexChanged += SettingControl_Changed;
            _toolOutputTokenLimitComboBox.SelectedIndexChanged += SettingControl_Changed;
            _projectDocMaxBytesComboBox.SelectedIndexChanged += SettingControl_Changed;

            _reasoningSummaryComboBox.Enter += SettingControl_Entered;
            _verbosityComboBox.Enter += SettingControl_Entered;
            _serviceTierCheckBox.Enter += SettingControl_Entered;
            _webSearchModeComboBox.Enter += SettingControl_Entered;
            _webSearchContextSizeComboBox.Enter += SettingControl_Entered;
            _viewImageToolComboBox.Enter += SettingControl_Entered;
            _toolOutputTokenLimitComboBox.Enter += SettingControl_Entered;
            _projectDocMaxBytesComboBox.Enter += SettingControl_Entered;
        }

        private void SettingControl_Changed(object sender, EventArgs e)
        {
            UpdateDescription();
        }

        private void ServiceTierCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _serviceTierSelectionChanged = true;
            UpdateDescription();
        }

        private void SettingControl_Entered(object sender, EventArgs e)
        {
            _activeSettingControl = sender as Control;
            UpdateDescription();
        }

        private void UpdateDescription()
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiLocalization.Text("OfficialCodexSettingsForm.Description.DefaultHint", "Choose a preset value. Follow Default removes the user override and lets Codex use its built-in default."));
            builder.AppendLine();
            builder.AppendLine(UiLocalization.Text("OfficialCodexSettingsForm.Description.Reasoning", "Reasoning Summary and Verbosity affect model output style when the selected model supports those options."));
            builder.AppendLine(UiLocalization.Text("OfficialCodexSettingsForm.Description.Search", "Web Search controls whether Codex may use cached results, the indexed web corpus, or live web search. Search Context Size adjusts how much search context is supplied to the model."));
            builder.AppendLine(UiLocalization.Text("OfficialCodexSettingsForm.Description.Tools", "Image Viewing enables the local image inspection tool. Tool Output Limit and Project Document Limit can reduce context pressure from tool output and project instructions."));
            builder.AppendLine();
            builder.AppendLine(string.Format(
                UiLocalization.Text("OfficialCodexSettingsForm.Description.Current.Template", "Current: search {0}; search context {1}"),
                GetSelectedChoiceText(_webSearchModeComboBox),
                GetSelectedChoiceText(_webSearchContextSizeComboBox)));
            var activeDescription = GetActiveSettingDescription();
            if (!string.IsNullOrWhiteSpace(activeDescription))
            {
                builder.AppendLine();
                builder.AppendLine(activeDescription);
            }

            if (!string.IsNullOrWhiteSpace(_permissionProfilesText))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("OfficialCodexSettingsForm.PermissionProfiles.Title", "Official permission profiles:"));
                builder.AppendLine(_permissionProfilesText);
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_descriptionTextBox, builder.ToString().Trim());
        }

        internal static string FormatPermissionProfilesForTests(IEnumerable<AppServerPermissionProfileSummary> profiles)
        {
            return FormatPermissionProfiles(profiles);
        }

        private static string FormatPermissionProfiles(IEnumerable<AppServerPermissionProfileSummary> profiles)
        {
            var builder = new StringBuilder();
            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(profile.Id.Trim());
                    if (!string.IsNullOrWhiteSpace(profile.Description))
                    {
                        builder.Append(" - ");
                        builder.Append(profile.Description.Trim());
                    }
                }
            }

            return builder.Length == 0
                ? UiLocalization.Text("OfficialCodexSettingsForm.PermissionProfiles.Empty", "Current runtime did not return permission profiles.")
                : builder.ToString();
        }

        private string GetActiveSettingDescription()
        {
            if (_activeSettingControl == null)
            {
                return null;
            }

            if (object.ReferenceEquals(_activeSettingControl, _reasoningSummaryComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.ReasoningSummary", "Reasoning Summary"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.ReasoningSummaryHint", "Controls whether supported models return reasoning summaries. Follow Default is safest unless you specifically need shorter, detailed, or no summaries."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _verbosityComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.Verbosity", "Verbosity"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.VerbosityHint", "Controls response detail for supported GPT-5 models. Higher values may produce longer answers and use more output tokens."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _serviceTierCheckBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.ServiceTier", "Fast Mode"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.ServiceTierHint", "Turns Codex fast mode on or off. Turning it off removes the service tier override."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _webSearchModeComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.WebSearchMode", "Web Search"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.WebSearchModeHint", "Controls whether Codex may use web search. Cached uses stored results, Indexed searches the web corpus, Live can fetch newer information, and Disabled turns the feature off."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _webSearchContextSizeComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.WebSearchContext", "Search Context"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.WebSearchContextHint", "Controls how much web-search context is supplied to the model. Higher values can improve recall but consume more context."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _viewImageToolComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.ViewImage", "Image Viewing"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.ViewImageHint", "Controls the local image inspection tool. Follow Default keeps Codex's built-in behavior."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _toolOutputTokenLimitComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.ToolOutputLimit", "Tool Output Limit"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.ToolOutputLimitHint", "Choose how much tool output may be kept in context. Lower values save context but may truncate logs, diffs, or diagnostics."));
            }

            if (object.ReferenceEquals(_activeSettingControl, _projectDocMaxBytesComboBox))
            {
                return BuildActiveSettingDescription(
                    UiLocalization.Text("OfficialCodexSettingsForm.Label.ProjectDocLimit", "Project Document Limit"),
                    UiLocalization.Text("OfficialCodexSettingsForm.Description.ProjectDocLimitHint", "Choose how much project instruction content may be loaded. Lower values reduce context use but may omit important guidance."));
            }

            return null;
        }

        private static string BuildActiveSettingDescription(string label, string description)
        {
            return string.Format(
                UiLocalization.Text("OfficialCodexSettingsForm.Description.Active.Template", "Focused setting: {0}\r\n{1}"),
                label,
                description);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            ApplySelection();
            DialogResult = DialogResult.OK;
            Close();
        }

        internal void ApplySelection()
        {
            ToolOutputTokenLimit = GetSelectedLongValue(_toolOutputTokenLimitComboBox);
            ProjectDocMaxBytes = GetSelectedLongValue(_projectDocMaxBytesComboBox);
            ModelReasoningSummary = GetSelectedStringValue(_reasoningSummaryComboBox);
            ModelVerbosity = GetSelectedStringValue(_verbosityComboBox);
            ServiceTier = !_serviceTierSelectionChanged &&
                !string.IsNullOrWhiteSpace(_initialServiceTier) &&
                !StartupFastModePromptPlanner.IsFastServiceTier(_initialServiceTier)
                ? _initialServiceTier
                : _serviceTierCheckBox.Checked
                    ? StartupFastModePromptPlanner.FastServiceTierId
                    : null;
            WebSearchMode = GetSelectedStringValue(_webSearchModeComboBox);
            WebSearchContextSize = GetSelectedStringValue(_webSearchContextSizeComboBox);
            ViewImageToolEnabled = GetSelectedBoolValue(_viewImageToolComboBox);
        }

        private static void SelectLongChoice(ComboBox comboBox, long? value)
        {
            if (SelectLongChoiceByValue(comboBox, value))
            {
                return;
            }

            if (value.HasValue)
            {
                comboBox.Items.Add(new SettingChoiceItem(
                    value.Value,
                    string.Format(
                        UiLocalization.Text("OfficialCodexSettingsForm.Value.Custom.Template", "Current custom value ({0})"),
                        value.Value)));
                comboBox.SelectedIndex = comboBox.Items.Count - 1;
                return;
            }

            SelectLongChoiceByValue(comboBox, null);
        }

        private static bool SelectLongChoiceByValue(ComboBox comboBox, long? value)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i] as SettingChoiceItem;
                if (item != null && AreNullableLongValuesEqual(item.Value, value))
                {
                    comboBox.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static void SelectStringChoice(ComboBox comboBox, string value)
        {
            var normalized = NormalizeChoiceValue(value);
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i] as SettingChoiceItem;
                if (item != null && string.Equals(NormalizeChoiceValue(item.Value), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                comboBox.Items.Add(new SettingChoiceItem(
                    value,
                    string.Format(
                        UiLocalization.Text("OfficialCodexSettingsForm.Value.Custom.Template", "Current custom value ({0})"),
                        value)));
                comboBox.SelectedIndex = comboBox.Items.Count - 1;
                return;
            }

            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }

        private static string GetSelectedStringValue(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as SettingChoiceItem;
            var text = item != null ? item.Value as string : null;
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static long? GetSelectedLongValue(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as SettingChoiceItem;
            if (item == null || item.Value == null)
            {
                return null;
            }

            if (item.Value is long)
            {
                return (long)item.Value;
            }

            return null;
        }

        private static bool? GetSelectedBoolValue(ComboBox comboBox)
        {
            bool value;
            return bool.TryParse(GetSelectedStringValue(comboBox), out value) ? (bool?)value : null;
        }

        private static string GetSelectedChoiceText(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem as SettingChoiceItem;
            return item != null ? item.Text : UiLocalization.Text("OfficialCodexSettingsForm.Value.FollowDefault", "Follow Default");
        }

        private static bool AreNullableLongValuesEqual(object itemValue, long? expectedValue)
        {
            if (itemValue == null)
            {
                return !expectedValue.HasValue;
            }

            if (!expectedValue.HasValue || !(itemValue is long))
            {
                return false;
            }

            return (long)itemValue == expectedValue.Value;
        }

        private static string NormalizeChoiceValue(object value)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
        }

        private sealed class SettingChoiceItem
        {
            public SettingChoiceItem(object value, string text)
            {
                Value = value;
                Text = text;
            }

            public object Value { get; }

            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
