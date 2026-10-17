using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class CommandPresetForm : Form
    {
        private readonly ListView _commandListView;
        private readonly Button _insertButton;
        private readonly Button _runButton;
        private readonly Button _addButton;
        private readonly Button _editButton;
        private readonly Button _deleteButton;
        private readonly Button _closeButton;
        private readonly List<CommandDefinition> _commandDefinitions;
        private readonly CommandHostPlatform _currentHostPlatform;
        private readonly bool _canRunCommand;

        public CommandPresetForm(IEnumerable<CommandDefinition> commandDefinitions, AppServerHostPlatform currentHostPlatform, bool canRunCommand = true)
        {
            Text = UiLocalization.Text("CommandPresetForm.Title", "Common Commands");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 420);
            Size = new Size(860, 500);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _commandDefinitions = CloneDefinitions(commandDefinitions);
            _currentHostPlatform = ToCommandHostPlatform(currentHostPlatform);
            _canRunCommand = canRunCommand;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var tipLabel = new Label
            {
                Dock = DockStyle.Fill,
                Height = 28,
                Text = UiLocalization.Text("CommandPresetForm.Tip", "Select a command, then insert it into the terminal input or run it immediately."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _commandListView = CreateCommandListView();
            _commandListView.TabIndex = 0;
            _commandListView.SelectedIndexChanged += CommandListView_SelectedIndexChanged;
            _commandListView.DoubleClick += CommandListView_DoubleClick;
            _commandListView.KeyDown += CommandListView_KeyDown;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            _closeButton = CreateButton("CommandPresetForm.Button.Close", "Close(&C)", "CommandPresetForm.Accessible.Close", "Close", 6);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            _deleteButton = CreateButton("CommandPresetForm.Button.Delete", "Delete(&D)", "CommandPresetForm.Accessible.Delete", "Delete", 5);
            UiTheme.ApplyButton(_deleteButton, UiGlyph.Cancel);
            _deleteButton.Click += DeleteButton_Click;

            _editButton = CreateButton("CommandPresetForm.Button.Edit", "Edit(&E)", "CommandPresetForm.Accessible.Edit", "Edit", 4);
            UiTheme.ApplyButton(_editButton, UiGlyph.Info);
            _editButton.Click += EditButton_Click;

            _addButton = CreateButton("CommandPresetForm.Button.Add", "Add(&A)", "CommandPresetForm.Accessible.Add", "Add", 3);
            UiTheme.ApplyButton(_addButton, UiGlyph.NewSession);
            _addButton.Click += AddButton_Click;

            _runButton = CreateButton("CommandPresetForm.Button.Run", "Run(&R)", "CommandPresetForm.Accessible.Run", "Run", 2);
            UiTheme.ApplyButton(_runButton, UiGlyph.Send);
            _runButton.Click += RunButton_Click;

            _insertButton = CreateButton("CommandPresetForm.Button.Insert", "Insert(&I)", "CommandPresetForm.Accessible.Insert", "Insert", 1);
            UiTheme.ApplyButton(_insertButton, UiGlyph.Command);
            _insertButton.Click += InsertButton_Click;

            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_deleteButton);
            buttonPanel.Controls.Add(_editButton);
            buttonPanel.Controls.Add(_addButton);
            buttonPanel.Controls.Add(_runButton);
            buttonPanel.Controls.Add(_insertButton);

            root.Controls.Add(tipLabel, 0, 0);
            root.Controls.Add(_commandListView, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(root);

            CancelButton = _closeButton;
            RebuildCommandList();
        }

        public IReadOnlyList<CommandDefinition> CommandDefinitions
        {
            get { return _commandDefinitions.ToArray(); }
        }

        public string SelectedCommandText { get; private set; }

        public CommandPresetAction SelectedAction { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_commandListView.Items.Count > 0 && _commandListView.SelectedItems.Count == 0)
            {
                _commandListView.Items[0].Selected = true;
                _commandListView.Items[0].Focused = true;
            }

            _commandListView.Focus();
            RefreshActionButtons();
        }

        private void CommandListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            InsertSelectedCommand();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void CommandListView_DoubleClick(object sender, EventArgs e)
        {
            InsertSelectedCommand();
        }

        private void CommandListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshActionButtons();
        }

        private void InsertButton_Click(object sender, EventArgs e)
        {
            InsertSelectedCommand();
        }

        private void RunButton_Click(object sender, EventArgs e)
        {
            var definition = GetSelectedCommandDefinition();
            if (definition == null)
            {
                return;
            }

            SelectedCommandText = (definition.CommandText ?? string.Empty).Trim();
            SelectedAction = CommandPresetAction.Run;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            using (var form = new CommandDefinitionEditForm(
                UiLocalization.Text("CommandPresetForm.DialogTitle.AddCommand", "Add Command"),
                new CommandDefinition { HostPlatform = _currentHostPlatform },
                _currentHostPlatform))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.EditedDefinition == null)
                {
                    return;
                }

                _commandDefinitions.Add(form.EditedDefinition);
                RebuildCommandList();
                SelectCommandDefinition(form.EditedDefinition);
            }
        }

        private void EditButton_Click(object sender, EventArgs e)
        {
            var selectedDefinition = GetSelectedCommandDefinition();
            if (selectedDefinition == null)
            {
                return;
            }

            using (var form = new CommandDefinitionEditForm(
                UiLocalization.Text("CommandPresetForm.DialogTitle.EditCommand", "Edit Command"),
                selectedDefinition,
                _currentHostPlatform))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.EditedDefinition == null)
                {
                    return;
                }

                var index = _commandDefinitions.IndexOf(selectedDefinition);
                if (index < 0)
                {
                    return;
                }

                _commandDefinitions[index] = form.EditedDefinition;
                RebuildCommandList();
                SelectCommandDefinition(_commandDefinitions[index]);
            }
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            var selectedDefinition = GetSelectedCommandDefinition();
            if (selectedDefinition == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                UiLocalization.Text("CommandPresetForm.Message.ConfirmDelete", "Are you sure you want to delete the selected command?"),
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            _commandDefinitions.Remove(selectedDefinition);
            RebuildCommandList();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            SelectedAction = CommandPresetAction.None;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void InsertSelectedCommand()
        {
            var definition = GetSelectedCommandDefinition();
            if (definition == null)
            {
                return;
            }

            SelectedCommandText = (definition.CommandText ?? string.Empty).Trim();
            SelectedAction = CommandPresetAction.Insert;
            DialogResult = DialogResult.OK;
            Close();
        }

        private CommandDefinition GetSelectedCommandDefinition()
        {
            return _commandListView.SelectedItems.Count > 0
                ? _commandListView.SelectedItems[0].Tag as CommandDefinition
                : null;
        }

        private void RebuildCommandList()
        {
            _commandListView.BeginUpdate();
            _commandListView.Items.Clear();

            foreach (var definition in _commandDefinitions)
            {
                if (definition == null ||
                    string.IsNullOrWhiteSpace(definition.Name) ||
                    !IsDefinitionVisibleForCurrentHost(definition))
                {
                    continue;
                }

                var item = new ListViewItem(new[]
                {
                    definition.Name.Trim(),
                    GetHostPlatformDisplayText(definition.HostPlatform),
                    (definition.CommandText ?? string.Empty).Trim()
                })
                {
                    Tag = definition
                };
                _commandListView.Items.Add(item);
            }

            _commandListView.EndUpdate();
            RefreshActionButtons();
        }

        private void RefreshActionButtons()
        {
            var hasSelection = GetSelectedCommandDefinition() != null;
            _insertButton.Enabled = hasSelection;
            _runButton.Enabled = hasSelection && _canRunCommand;
            _editButton.Enabled = hasSelection;
            _deleteButton.Enabled = hasSelection;
        }

        private void SelectCommandDefinition(CommandDefinition definition)
        {
            foreach (ListViewItem item in _commandListView.Items)
            {
                item.Selected = ReferenceEquals(item.Tag, definition);
                if (item.Selected)
                {
                    item.Focused = true;
                    item.EnsureVisible();
                }
            }

            RefreshActionButtons();
        }

        private bool IsDefinitionVisibleForCurrentHost(CommandDefinition definition)
        {
            var hostPlatform = NormalizeHostPlatform(definition != null ? definition.HostPlatform : CommandHostPlatform.Windows);
            return hostPlatform == CommandHostPlatform.Any ||
                   hostPlatform == _currentHostPlatform;
        }

        private static Button CreateButton(string textKey, string textFallback, string accessibleKey, string accessibleFallback, int tabIndex)
        {
            return new Button
            {
                Text = UiLocalization.Text(textKey, textFallback),
                AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback),
                Width = 92,
                Height = 32,
                Margin = new Padding(8, 0, 0, 0),
                TabIndex = tabIndex,
                UseMnemonic = true
            };
        }

        private static ListView CreateCommandListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                AccessibleName = UiLocalization.Text("CommandPresetForm.Accessible.PresetCommands", "Common Commands")
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("CommandPresetForm.Column.Name", "Name"), 180, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("CommandPresetForm.Column.Target", "Target"), 120, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("CommandPresetForm.Column.Command", "Command"), 440, HorizontalAlignment.Left);
            return listView;
        }

        private static List<CommandDefinition> CloneDefinitions(IEnumerable<CommandDefinition> definitions)
        {
            return definitions == null
                ? new List<CommandDefinition>()
                : definitions
                    .Where(item => item != null)
                    .Select(item => new CommandDefinition
                    {
                        Name = item.Name,
                        CommandText = item.CommandText,
                        HostPlatform = NormalizeHostPlatform(item.HostPlatform)
                    })
                    .ToList();
        }

        private static CommandHostPlatform ToCommandHostPlatform(AppServerHostPlatform hostPlatform)
        {
            switch (hostPlatform)
            {
                case AppServerHostPlatform.Wsl:
                    return CommandHostPlatform.Wsl;
                case AppServerHostPlatform.RemoteMac:
                    return CommandHostPlatform.RemoteMac;
                default:
                    return CommandHostPlatform.Windows;
            }
        }

        private static CommandHostPlatform NormalizeHostPlatform(CommandHostPlatform hostPlatform)
        {
            return hostPlatform == CommandHostPlatform.Wsl ||
                   hostPlatform == CommandHostPlatform.RemoteMac ||
                   hostPlatform == CommandHostPlatform.Any
                ? hostPlatform
                : CommandHostPlatform.Windows;
        }

        private static string GetHostPlatformDisplayText(CommandHostPlatform hostPlatform)
        {
            switch (NormalizeHostPlatform(hostPlatform))
            {
                case CommandHostPlatform.Wsl:
                    return UiLocalization.Text("CommandDefinitionEditForm.Target.Wsl", "WSL");
                case CommandHostPlatform.RemoteMac:
                    return UiLocalization.Text("CommandDefinitionEditForm.Target.RemoteMac", "Remote Unix");
                case CommandHostPlatform.Any:
                    return UiLocalization.Text("CommandDefinitionEditForm.Target.Any", "All runtimes");
                default:
                    return UiLocalization.Text("CommandDefinitionEditForm.Target.Windows", "Windows");
            }
        }
    }

    internal enum CommandPresetAction
    {
        None = 0,
        Insert = 1,
        Run = 2
    }
}
