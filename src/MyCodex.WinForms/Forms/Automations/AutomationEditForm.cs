using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationEditForm : Form
    {
        private readonly AutomationCreationContext _context;
        private readonly AutomationInputSelectionCallbacks _inputSelectionCallbacks;
        private readonly IReadOnlyList<AppServerModelOption> _modelOptions;
        private readonly Func<string, bool> _supportsReasoningEffortForModel;
        private readonly List<AutomationInputItemSnapshot> _inputItems;
        private readonly TextBox _nameTextBox;
        private readonly ComboBox _kindComboBox;
        private readonly ComboBox _scheduleKindComboBox;
        private readonly NumericUpDown _intervalInput;
        private readonly Label _intervalUnitLabel;
        private readonly DateTimePicker _timePicker;
        private readonly ComboBox _dayOfWeekComboBox;
        private readonly ComboBox _modelComboBox;
        private readonly ComboBox _reasoningEffortComboBox;
        private readonly ComboBox _approvalModeComboBox;
        private readonly TextBox _promptTextBox;
        private readonly Button _insertPluginButton;
        private readonly Button _insertSkillButton;
        private readonly Button _removeInputButton;
        private readonly ListView _inputItemsListView;
        private readonly CheckBox _autoArchiveCheckBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public AutomationEditForm(AutomationCreationContext context)
            : this(context, null)
        {
        }

        public AutomationEditForm(AutomationCreationContext context, AutomationInputSelectionCallbacks inputSelectionCallbacks)
            : this(context, inputSelectionCallbacks, null, null)
        {
        }

        public AutomationEditForm(
            AutomationCreationContext context,
            AutomationInputSelectionCallbacks inputSelectionCallbacks,
            IReadOnlyList<AppServerModelOption> modelOptions,
            Func<string, bool> supportsReasoningEffortForModel)
        {
            _context = context ?? new AutomationCreationContext { Kind = AutomationKind.Standalone };
            _inputSelectionCallbacks = inputSelectionCallbacks;
            _modelOptions = modelOptions ?? new List<AppServerModelOption>();
            _supportsReasoningEffortForModel = supportsReasoningEffortForModel;
            _inputItems = new List<AutomationInputItemSnapshot>();

            Text = UiLocalization.Text("AutomationEditForm.Title.New", "New automation");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 520);
            Size = new Size(760, 620);
            AutoScroll = true;
            UiTheme.ApplyForm(this);

            _nameTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name"), 0);
            _kindComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.Kind", "Automation type"), 1, DockStyle.Fill);
            _scheduleKindComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.ScheduleKind", "Schedule"), 2, DockStyle.Fill);
            _intervalInput = new NumericUpDown
            {
                AccessibleName = UiLocalization.Text("AutomationEditForm.Accessible.Interval", "Interval"),
                Dock = DockStyle.Left,
                Minimum = 1,
                Maximum = 1440,
                Value = 1,
                TabIndex = 3
            };
            _intervalUnitLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(_intervalUnitLabel, false);

            _timePicker = new DateTimePicker
            {
                AccessibleName = UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time"),
                Dock = DockStyle.Left,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                Width = 110,
                TabIndex = 4,
                Value = DateTime.Today.AddHours(8)
            };

            _dayOfWeekComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.DayOfWeek", "Day of week"), 5, DockStyle.Fill);

            _modelComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model"), 6, DockStyle.Fill);
            _reasoningEffortComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort"), 7, DockStyle.Fill);
            _approvalModeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("AutomationEditForm.Accessible.ApprovalMode", "Approval mode"), 8, DockStyle.Fill);
            _modelComboBox.SelectedIndexChanged += (sender, e) => UpdateReasoningEffortChoices(null);

            _promptTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt"), 9);
            _promptTextBox.Multiline = true;
            _promptTextBox.ScrollBars = ScrollBars.Vertical;

            _insertPluginButton = CreateButton(
                UiLocalization.Text("AutomationEditForm.Button.InsertPlugin", "Plugin(&G)"),
                UiLocalization.Text("AutomationEditForm.Accessible.InsertPlugin", "Insert plugin"),
                10,
                DialogResult.None);
            _insertSkillButton = CreateButton(
                UiLocalization.Text("AutomationEditForm.Button.InsertSkill", "Skill(&K)"),
                UiLocalization.Text("AutomationEditForm.Accessible.InsertSkill", "Insert skill"),
                11,
                DialogResult.None);
            _removeInputButton = CreateButton(
                UiLocalization.Text("AutomationEditForm.Button.RemoveInput", "Remove(&R)"),
                UiLocalization.Text("AutomationEditForm.Accessible.RemoveInput", "Remove inserted item"),
                13,
                DialogResult.None);
            _inputItemsListView = CreateInputItemsListView();
            _inputItemsListView.TabIndex = 12;

            _autoArchiveCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("AutomationEditForm.Check.AutoArchive", "Allow no-findings runs to archive automatically"),
                AccessibleName = UiLocalization.Text("AutomationEditForm.Accessible.AutoArchive", "Allow no-findings runs to archive automatically"),
                AutoSize = true,
                TabIndex = 14,
                UseMnemonic = true
            };
            UiTheme.ApplyCheckBox(_autoArchiveCheckBox);

            _okButton = CreateButton(UiLocalization.Text("Common.OK", "&OK"), UiLocalization.Text("Common.Accessible.OK", "OK"), 15, DialogResult.OK);
            _cancelButton = CreateButton(UiLocalization.Text("Common.Cancel", "&Cancel"), UiLocalization.Text("Common.Accessible.Cancel", "Cancel"), 16, DialogResult.Cancel);
            _okButton.Click += OkButton_Click;
            _insertPluginButton.Click += async (sender, e) => await InsertInputItemAsync(_inputSelectionCallbacks != null ? _inputSelectionCallbacks.SelectPluginAsync : null);
            _insertSkillButton.Click += async (sender, e) => await InsertInputItemAsync(_inputSelectionCallbacks != null ? _inputSelectionCallbacks.SelectSkillAsync : null);
            _removeInputButton.Click += RemoveInputButton_Click;
            _inputItemsListView.SelectedIndexChanged += (sender, e) => RefreshInputItemButtons();
            RefreshInputItemButtons();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            BindCombos();
            BuildLayout();
        }

        public AutomationEditForm(
            AutomationDefinitionSnapshot definition,
            AutomationInputSelectionCallbacks inputSelectionCallbacks,
            IReadOnlyList<AppServerModelOption> modelOptions,
            Func<string, bool> supportsReasoningEffortForModel)
            : this(BuildContext(definition), inputSelectionCallbacks, modelOptions, supportsReasoningEffortForModel)
        {
            Text = UiLocalization.Text("AutomationEditForm.Title.Edit", "Edit automation");
            ApplyDefinition(definition);
        }

        public AutomationCreationRequest Request { get; private set; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BindCombos()
        {
            foreach (var kind in GetAllowedKinds())
            {
                _kindComboBox.Items.Add(new AutomationKindItem(
                    kind,
                    AutomationDisplayText.Kind(kind)));
            }

            SelectKind(_context.Kind);

            _scheduleKindComboBox.Items.Add(new AutomationScheduleKindItem(
                AutomationScheduleKind.ManualOnly,
                AutomationDisplayText.ScheduleKind(AutomationScheduleKind.ManualOnly)));
            _scheduleKindComboBox.Items.Add(new AutomationScheduleKindItem(
                AutomationScheduleKind.EveryMinutes,
                AutomationDisplayText.ScheduleKind(AutomationScheduleKind.EveryMinutes)));
            _scheduleKindComboBox.Items.Add(new AutomationScheduleKindItem(
                AutomationScheduleKind.EveryHours,
                AutomationDisplayText.ScheduleKind(AutomationScheduleKind.EveryHours)));
            _scheduleKindComboBox.Items.Add(new AutomationScheduleKindItem(
                AutomationScheduleKind.Daily,
                AutomationDisplayText.ScheduleKind(AutomationScheduleKind.Daily)));
            _scheduleKindComboBox.Items.Add(new AutomationScheduleKindItem(
                AutomationScheduleKind.Weekly,
                AutomationDisplayText.ScheduleKind(AutomationScheduleKind.Weekly)));
            SelectScheduleKind(AutomationScheduleKind.EveryHours);
            BindDayOfWeekChoices();
            _scheduleKindComboBox.SelectedIndexChanged += (sender, e) => UpdateScheduleControls();
            UpdateScheduleControls();
            BindRunSettings();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("AutomationEditForm.Label.Name", "Name(&N)"), _nameTextBox), 0, 0);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("AutomationEditForm.Label.Kind", "Type(&T)"), _kindComboBox), 0, 1);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("AutomationEditForm.Label.ScheduleKind", "Schedule(&S)"), _scheduleKindComboBox), 0, 2);
            root.Controls.Add(CreateScheduleDetailsPanel(), 0, 3);
            root.Controls.Add(CreateRunSettingsPanel(), 0, 4);
            root.Controls.Add(CreateLabeledControl(UiLocalization.Text("AutomationEditForm.Label.Prompt", "Prompt(&P)"), _promptTextBox), 0, 5);
            root.Controls.Add(CreateInputItemsPanel(), 0, 6);
            root.Controls.Add(_autoArchiveCheckBox, 0, 7);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                TabStop = false
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_okButton);
            root.Controls.Add(buttons, 0, 8);

            Controls.Add(root);
        }

        private Control CreateRunSettingsPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.Model", "Model(&L)")), 0, 0);
            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.ReasoningEffort", "Reasoning(&Y)")), 1, 0);
            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.ApprovalMode", "Approval(&V)")), 2, 0);
            panel.Controls.Add(_modelComboBox, 0, 1);
            panel.Controls.Add(_reasoningEffortComboBox, 1, 1);
            panel.Controls.Add(_approvalModeComboBox, 2, 1);
            return panel;
        }

        private Control CreateInputItemsPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = UiLocalization.Text("AutomationEditForm.Label.Insert", "Insert"),
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                TabStop = false
            };
            buttons.Controls.Add(_insertPluginButton);
            buttons.Controls.Add(_insertSkillButton);
            buttons.Controls.Add(_removeInputButton);

            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(buttons, 0, 1);
            panel.Controls.Add(_inputItemsListView, 0, 2);
            return panel;
        }

        private Control CreateScheduleDetailsPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.Interval", "Interval(&I)")), 0, 0);
            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.Time", "Time(&M)")), 1, 0);
            panel.Controls.Add(CreateScheduleLabel(UiLocalization.Text("AutomationEditForm.Label.DayOfWeek", "Day(&W)")), 2, 0);

            var intervalPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                TabStop = false
            };
            intervalPanel.Controls.Add(_intervalInput);
            intervalPanel.Controls.Add(_intervalUnitLabel);

            panel.Controls.Add(intervalPanel, 0, 1);
            panel.Controls.Add(_timePicker, 1, 1);
            panel.Controls.Add(_dayOfWeekComboBox, 2, 1);
            return panel;
        }

        private static Label CreateScheduleLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var name = (_nameTextBox.Text ?? string.Empty).Trim();
            var prompt = (_promptTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(prompt))
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(
                    this,
                    UiLocalization.Text("AutomationEditForm.Message.Required", "Name and prompt are required."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Request = new AutomationCreationRequest
            {
                Name = name,
                Kind = GetSelectedKind(),
                Prompt = prompt,
                InputItems = CloneInputItems(_inputItems),
                Schedule = BuildSchedule(),
                Context = new AutomationContextSnapshot
                {
                    InteractionMode = _context.InteractionMode,
                    RuntimeProfileId = _context.RuntimeProfileId,
                    OfficeRuntimeScopeId = _context.OfficeRuntimeScopeId,
                    WorkspaceId = _context.WorkspaceId,
                    WorkspacePath = _context.WorkspacePath,
                    OfficeRootPath = _context.OfficeRootPath,
                    SessionId = _context.SessionId,
                    ThreadId = _context.ThreadId
                },
                ModelId = GetSelectedModelId(),
                ReasoningEffort = GetSelectedReasoningEffort(),
                ApprovalMode = GetSelectedApprovalMode(),
                AllowNoFindingsAutoArchive = _autoArchiveCheckBox.Checked
            };
        }

        private void SelectKind(AutomationKind kind)
        {
            for (var i = 0; i < _kindComboBox.Items.Count; i++)
            {
                var item = _kindComboBox.Items[i] as AutomationKindItem;
                if (item != null && item.Kind == kind)
                {
                    _kindComboBox.SelectedIndex = i;
                    return;
                }
            }

            _kindComboBox.SelectedIndex = _kindComboBox.Items.Count > 0 ? 0 : -1;
        }

        private void ApplyDefinition(AutomationDefinitionSnapshot definition)
        {
            if (definition == null)
            {
                return;
            }

            _nameTextBox.Text = definition.Name ?? string.Empty;
            _promptTextBox.Text = definition.Prompt ?? string.Empty;
            _inputItems.Clear();
            foreach (var item in CloneInputItems(definition.InputItems))
            {
                _inputItems.Add(item);
            }

            RefreshInputItemsList();
            _autoArchiveCheckBox.Checked = definition.AllowNoFindingsAutoArchive;
            SelectKind(definition.Kind);
            ApplySchedule(definition.Schedule);
            SelectModel(definition.ModelId);
            UpdateReasoningEffortChoices(definition.ReasoningEffort);
            SelectApprovalMode(definition.ApprovalMode ?? ApprovalMode.Auto);
        }

        private void ApplySchedule(AutomationScheduleSnapshot schedule)
        {
            schedule = schedule ?? new AutomationScheduleSnapshot();
            SelectScheduleKind(schedule.Kind);
            if (schedule.Interval > 0)
            {
                _intervalInput.Value = Math.Max(_intervalInput.Minimum, Math.Min(_intervalInput.Maximum, schedule.Interval));
            }

            if (schedule.TimeOfDayMinutes.HasValue)
            {
                var minutes = Math.Max(0, Math.Min(23 * 60 + 59, schedule.TimeOfDayMinutes.Value));
                _timePicker.Value = DateTime.Today.AddMinutes(minutes);
            }

            if (schedule.DayOfWeek.HasValue)
            {
                SelectDayOfWeek(schedule.DayOfWeek.Value);
            }

            UpdateScheduleControls();
        }

        private void BindRunSettings()
        {
            BindModelChoices();

            _approvalModeComboBox.Items.Clear();
            _approvalModeComboBox.Items.Add(new ApprovalModeItem(ApprovalMode.ReadOnly, ApprovalModeDisplayText.NameWithDescription(ApprovalMode.ReadOnly)));
            _approvalModeComboBox.Items.Add(new ApprovalModeItem(ApprovalMode.Auto, ApprovalModeDisplayText.NameWithDescription(ApprovalMode.Auto)));
            _approvalModeComboBox.Items.Add(new ApprovalModeItem(ApprovalMode.FullAccess, ApprovalModeDisplayText.NameWithDescription(ApprovalMode.FullAccess)));
            _approvalModeComboBox.Items.Add(new ApprovalModeItem(ApprovalMode.Custom, ApprovalModeDisplayText.NameWithDescription(ApprovalMode.Custom)));
            SelectApprovalMode(_context.ApprovalMode ?? ApprovalMode.Auto);
        }

        private void BindModelChoices()
        {
            _modelComboBox.Items.Clear();
            var selectedModelId = ResolveInitialModelId();
            var hasSelectedModel = false;

            foreach (var model in MainFormModelOptionComposer.BuildOrderedModelOptions(_modelOptions))
            {
                var item = new AutomationModelItem(
                    model.ModelId,
                    MainFormModelOptionComposer.BuildModelMenuLabel(model),
                    model.DefaultReasoningEffort,
                    MainFormModelOptionComposer.GetEffortsForModel(model));
                _modelComboBox.Items.Add(item);

                if (string.Equals(model.ModelId, selectedModelId, StringComparison.OrdinalIgnoreCase))
                {
                    hasSelectedModel = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedModelId) && !hasSelectedModel)
            {
                _modelComboBox.Items.Add(new AutomationModelItem(
                    selectedModelId,
                    MainFormModelOptionComposer.GetModelVersionLabel(selectedModelId),
                    ResolveInitialReasoningEffort(),
                    Array.Empty<AppServerModelEffortOption>()));
            }

            SelectModel(selectedModelId);
            UpdateReasoningEffortChoices(ResolveInitialReasoningEffort());
        }

        private void SelectModel(string modelId)
        {
            for (var i = 0; i < _modelComboBox.Items.Count; i++)
            {
                var item = _modelComboBox.Items[i] as AutomationModelItem;
                if (item != null && string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    _modelComboBox.SelectedIndex = i;
                    return;
                }
            }

            _modelComboBox.SelectedIndex = _modelComboBox.Items.Count > 0 ? 0 : -1;
        }

        private void UpdateReasoningEffortChoices(string preferredEffortId)
        {
            _reasoningEffortComboBox.Items.Clear();
            var model = _modelComboBox.SelectedItem as AutomationModelItem;
            if (model == null || !SupportsReasoningEffortForModel(model.ModelId))
            {
                _reasoningEffortComboBox.Enabled = false;
                _reasoningEffortComboBox.SelectedIndex = -1;
                return;
            }

            _reasoningEffortComboBox.Enabled = true;
            foreach (var effort in model.Efforts)
            {
                if (effort == null || string.IsNullOrWhiteSpace(effort.EffortId))
                {
                    continue;
                }

                _reasoningEffortComboBox.Items.Add(new ReasoningEffortItem(
                    effort.EffortId,
                    MainFormModelOptionComposer.GetReasoningEffortText(effort.EffortId)));
            }

            if (_reasoningEffortComboBox.Items.Count == 0)
            {
                _reasoningEffortComboBox.Enabled = false;
                _reasoningEffortComboBox.SelectedIndex = -1;
                return;
            }

            SelectReasoningEffort(TextValueHelper.FirstNonEmptyTrimmed(preferredEffortId, model.DefaultReasoningEffort, ResolveInitialReasoningEffort()));
        }

        private void SelectReasoningEffort(string effortId)
        {
            var normalized = TextValueHelper.TrimToNull(effortId);
            for (var i = 0; i < _reasoningEffortComboBox.Items.Count; i++)
            {
                var item = _reasoningEffortComboBox.Items[i] as ReasoningEffortItem;
                if (item != null && string.Equals(item.EffortId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _reasoningEffortComboBox.SelectedIndex = i;
                    return;
                }
            }

            _reasoningEffortComboBox.SelectedIndex = _reasoningEffortComboBox.Items.Count > 0 ? 0 : -1;
        }

        private void SelectApprovalMode(ApprovalMode approvalMode)
        {
            if (approvalMode == ApprovalMode.FullAccessAutoApprove)
            {
                approvalMode = ApprovalMode.FullAccess;
            }

            for (var i = 0; i < _approvalModeComboBox.Items.Count; i++)
            {
                var item = _approvalModeComboBox.Items[i] as ApprovalModeItem;
                if (item != null && item.Mode == approvalMode)
                {
                    _approvalModeComboBox.SelectedIndex = i;
                    return;
                }
            }

            _approvalModeComboBox.SelectedIndex = _approvalModeComboBox.Items.Count > 0 ? 0 : -1;
        }

        private void SelectScheduleKind(AutomationScheduleKind kind)
        {
            for (var i = 0; i < _scheduleKindComboBox.Items.Count; i++)
            {
                var item = _scheduleKindComboBox.Items[i] as AutomationScheduleKindItem;
                if (item != null && item.Kind == kind)
                {
                    _scheduleKindComboBox.SelectedIndex = i;
                    return;
                }
            }

            _scheduleKindComboBox.SelectedIndex = _scheduleKindComboBox.Items.Count > 0 ? 0 : -1;
        }

        private void BindDayOfWeekChoices()
        {
            _dayOfWeekComboBox.Items.Clear();
            AddDayOfWeekChoice(DayOfWeek.Monday);
            AddDayOfWeekChoice(DayOfWeek.Tuesday);
            AddDayOfWeekChoice(DayOfWeek.Wednesday);
            AddDayOfWeekChoice(DayOfWeek.Thursday);
            AddDayOfWeekChoice(DayOfWeek.Friday);
            AddDayOfWeekChoice(DayOfWeek.Saturday);
            AddDayOfWeekChoice(DayOfWeek.Sunday);
            SelectDayOfWeek(DayOfWeek.Monday);
        }

        private void AddDayOfWeekChoice(DayOfWeek dayOfWeek)
        {
            _dayOfWeekComboBox.Items.Add(new DayOfWeekItem(dayOfWeek, AutomationDisplayText.DayOfWeek(dayOfWeek)));
        }

        private void SelectDayOfWeek(DayOfWeek dayOfWeek)
        {
            for (var i = 0; i < _dayOfWeekComboBox.Items.Count; i++)
            {
                var item = _dayOfWeekComboBox.Items[i] as DayOfWeekItem;
                if (item != null && item.DayOfWeek == dayOfWeek)
                {
                    _dayOfWeekComboBox.SelectedIndex = i;
                    return;
                }
            }

            _dayOfWeekComboBox.SelectedIndex = _dayOfWeekComboBox.Items.Count > 0 ? 0 : -1;
        }

        private AutomationKind GetSelectedKind()
        {
            var item = _kindComboBox.SelectedItem as AutomationKindItem;
            return item != null ? item.Kind : AutomationKind.Standalone;
        }

        private IEnumerable<AutomationKind> GetAllowedKinds()
        {
            switch (_context.Kind)
            {
                case AutomationKind.Thread:
                    return new[] { AutomationKind.Thread, AutomationKind.Standalone };
                case AutomationKind.Project:
                    return new[] { AutomationKind.Project };
                case AutomationKind.Office:
                    return new[] { AutomationKind.Office };
                default:
                    return new[] { AutomationKind.Standalone };
            }
        }

        private AutomationScheduleKind GetSelectedScheduleKind()
        {
            var item = _scheduleKindComboBox.SelectedItem as AutomationScheduleKindItem;
            return item != null ? item.Kind : AutomationScheduleKind.ManualOnly;
        }

        private string GetSelectedReasoningEffort()
        {
            var model = _modelComboBox.SelectedItem as AutomationModelItem;
            if (model == null || !SupportsReasoningEffortForModel(model.ModelId))
            {
                return null;
            }

            var item = _reasoningEffortComboBox.SelectedItem as ReasoningEffortItem;
            return item != null ? item.EffortId : null;
        }

        private string GetSelectedModelId()
        {
            var item = _modelComboBox.SelectedItem as AutomationModelItem;
            return item != null ? TextValueHelper.TrimToNull(item.ModelId) : ResolveInitialModelId();
        }

        private bool SupportsReasoningEffortForModel(string modelId)
        {
            if (_supportsReasoningEffortForModel != null)
            {
                return _supportsReasoningEffortForModel(modelId);
            }

            return _modelOptions.Any(model =>
                model != null &&
                string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase) &&
                model.ReasoningEfforts.Count > 0);
        }

        private ApprovalMode GetSelectedApprovalMode()
        {
            var item = _approvalModeComboBox.SelectedItem as ApprovalModeItem;
            return item != null ? item.Mode : ApprovalMode.Auto;
        }

        private DayOfWeek GetSelectedDayOfWeek()
        {
            var item = _dayOfWeekComboBox.SelectedItem as DayOfWeekItem;
            return item != null ? item.DayOfWeek : DayOfWeek.Monday;
        }

        private void UpdateScheduleControls()
        {
            var kind = GetSelectedScheduleKind();
            var usesInterval = kind == AutomationScheduleKind.EveryMinutes || kind == AutomationScheduleKind.EveryHours;
            var usesTime = kind == AutomationScheduleKind.Daily || kind == AutomationScheduleKind.Weekly;
            var usesDay = kind == AutomationScheduleKind.Weekly;

            _intervalInput.Enabled = usesInterval;
            _intervalUnitLabel.Enabled = usesInterval;
            _timePicker.Enabled = usesTime;
            _dayOfWeekComboBox.Enabled = usesDay;

            if (kind == AutomationScheduleKind.EveryMinutes)
            {
                _intervalUnitLabel.Text = UiLocalization.Text("AutomationEditForm.Value.IntervalMinutes", "minutes");
            }
            else if (kind == AutomationScheduleKind.EveryHours)
            {
                _intervalUnitLabel.Text = UiLocalization.Text("AutomationEditForm.Value.IntervalHours", "hours");
            }
            else
            {
                _intervalUnitLabel.Text = string.Empty;
            }

            if (usesTime && _timePicker.Value.Hour == 0 && _timePicker.Value.Minute == 0)
            {
                _timePicker.Value = DateTime.Today.AddHours(8);
            }
        }

        private AutomationScheduleSnapshot BuildSchedule()
        {
            var kind = GetSelectedScheduleKind();
            var schedule = new AutomationScheduleSnapshot
            {
                Kind = kind,
                SkipMissedRuns = true
            };

            if (kind == AutomationScheduleKind.EveryMinutes || kind == AutomationScheduleKind.EveryHours)
            {
                schedule.Interval = Decimal.ToInt32(_intervalInput.Value);
            }
            else if (kind == AutomationScheduleKind.Daily)
            {
                schedule.TimeOfDayMinutes = GetSelectedTimeOfDayMinutes();
            }
            else if (kind == AutomationScheduleKind.Weekly)
            {
                schedule.DayOfWeek = GetSelectedDayOfWeek();
                schedule.TimeOfDayMinutes = GetSelectedTimeOfDayMinutes();
            }

            return schedule;
        }

        private int GetSelectedTimeOfDayMinutes()
        {
            return _timePicker.Value.Hour * 60 + _timePicker.Value.Minute;
        }

        private ListView CreateInputItemsListView()
        {
            var listView = new ListView
            {
                AccessibleName = UiLocalization.Text("AutomationEditForm.Accessible.InputItems", "Inserted plugins and skills"),
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("AutomationEditForm.Column.InputType", "Type"), 110);
            listView.Columns.Add(UiLocalization.Text("AutomationEditForm.Column.InputName", "Name"), 180);
            listView.Columns.Add(UiLocalization.Text("AutomationEditForm.Column.InputPath", "Path"), 420);
            return listView;
        }

        private async Task InsertInputItemAsync(Func<IWin32Window, Task<AutomationInputItemSnapshot>> selector)
        {
            if (selector == null)
            {
                return;
            }

            var item = await selector(this).ConfigureAwait(true);
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            if (_inputItems.Any(existing =>
                existing != null &&
                existing.Type == item.Type &&
                string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("AutomationEditForm.Message.InputAlreadyInserted", "This plugin or skill has already been inserted."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _inputItems.Add(CloneInputItem(item));
            RefreshInputItemsList();
        }

        private void RemoveInputButton_Click(object sender, EventArgs e)
        {
            if (_inputItemsListView.SelectedItems.Count == 0)
            {
                return;
            }

            var item = _inputItemsListView.SelectedItems[0].Tag as AutomationInputItemSnapshot;
            if (item == null)
            {
                return;
            }

            _inputItems.Remove(item);
            RefreshInputItemsList();
        }

        private void RefreshInputItemsList()
        {
            _inputItemsListView.Items.Clear();
            foreach (var item in _inputItems.Where(value => value != null))
            {
                _inputItemsListView.Items.Add(new ListViewItem(new[]
                {
                    AutomationDisplayText.InputItemKind(item.Type),
                    TextValueHelper.FirstNonEmptyTrimmed(item.DisplayName, item.Name, item.Path),
                    item.Path ?? string.Empty
                }) { Tag = item });
            }

            RefreshInputItemButtons();
        }

        private void RefreshInputItemButtons()
        {
            _insertPluginButton.Enabled = _inputSelectionCallbacks != null && _inputSelectionCallbacks.SelectPluginAsync != null;
            _insertSkillButton.Enabled = _inputSelectionCallbacks != null && _inputSelectionCallbacks.SelectSkillAsync != null;
            _removeInputButton.Enabled = _inputItemsListView.SelectedItems.Count > 0;
        }

        private static AutomationInputItemSnapshot CloneInputItem(AutomationInputItemSnapshot item)
        {
            if (item == null)
            {
                return null;
            }

            return new AutomationInputItemSnapshot
            {
                Type = item.Type,
                Name = TextValueHelper.TrimToNull(item.Name),
                DisplayName = TextValueHelper.TrimToNull(item.DisplayName),
                Path = TextValueHelper.TrimToNull(item.Path)
            };
        }

        private static List<AutomationInputItemSnapshot> CloneInputItems(IEnumerable<AutomationInputItemSnapshot> items)
        {
            var result = new List<AutomationInputItemSnapshot>();
            if (items == null)
            {
                return result;
            }

            foreach (var item in items)
            {
                var clone = CloneInputItem(item);
                if (clone != null)
                {
                    result.Add(clone);
                }
            }

            return result;
        }

        private string ResolveInitialModelId()
        {
            return TextValueHelper.TrimToNull(_context.ModelId);
        }

        private string ResolveInitialReasoningEffort()
        {
            return TextValueHelper.TrimToNull(_context.ReasoningEffort);
        }

        private static AutomationCreationContext BuildContext(AutomationDefinitionSnapshot definition)
        {
            if (definition == null)
            {
                return new AutomationCreationContext { Kind = AutomationKind.Standalone };
            }

            var context = definition.Context ?? new AutomationContextSnapshot();
            return new AutomationCreationContext
            {
                Kind = definition.Kind,
                InteractionMode = context.InteractionMode,
                RuntimeProfileId = context.RuntimeProfileId,
                OfficeRuntimeScopeId = context.OfficeRuntimeScopeId,
                WorkspaceId = context.WorkspaceId,
                WorkspacePath = context.WorkspacePath,
                OfficeRootPath = context.OfficeRootPath,
                SessionId = context.SessionId,
                ThreadId = context.ThreadId,
                ModelId = definition.ModelId,
                ReasoningEffort = definition.ReasoningEffort,
                ApprovalMode = definition.ApprovalMode
            };
        }

        private static Control CreateLabeledControl(string labelText, Control control)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Text = labelText,
                AutoSize = true,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(label, true);

            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(control, 0, 1);
            return panel;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, DialogResult result)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex,
                DialogResult = result,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(button, result == DialogResult.OK ? UiGlyph.Ok : UiGlyph.Cancel);
            return button;
        }

        private sealed class AutomationKindItem
        {
            public AutomationKindItem(AutomationKind kind, string text)
            {
                Kind = kind;
                Text = text ?? string.Empty;
            }

            public AutomationKind Kind { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class AutomationScheduleKindItem
        {
            public AutomationScheduleKindItem(AutomationScheduleKind kind, string text)
            {
                Kind = kind;
                Text = text ?? string.Empty;
            }

            public AutomationScheduleKind Kind { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class DayOfWeekItem
        {
            public DayOfWeekItem(DayOfWeek dayOfWeek, string text)
            {
                DayOfWeek = dayOfWeek;
                Text = text ?? string.Empty;
            }

            public DayOfWeek DayOfWeek { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class ReasoningEffortItem
        {
            public ReasoningEffortItem(string effortId, string text)
            {
                EffortId = string.IsNullOrWhiteSpace(effortId) ? null : effortId.Trim();
                Text = text ?? string.Empty;
            }

            public string EffortId { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class AutomationModelItem
        {
            public AutomationModelItem(
                string modelId,
                string text,
                string defaultReasoningEffort,
                IReadOnlyList<AppServerModelEffortOption> efforts)
            {
                ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim();
                Text = text ?? string.Empty;
                DefaultReasoningEffort = TextValueHelper.TrimToNull(defaultReasoningEffort);
                Efforts = efforts ?? new List<AppServerModelEffortOption>();
            }

            public string ModelId { get; }

            public string DefaultReasoningEffort { get; }

            public IReadOnlyList<AppServerModelEffortOption> Efforts { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class ApprovalModeItem
        {
            public ApprovalModeItem(ApprovalMode mode, string text)
            {
                Mode = mode;
                Text = text ?? string.Empty;
            }

            public ApprovalMode Mode { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
