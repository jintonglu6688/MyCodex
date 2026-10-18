using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class PendingAutomationAssignmentForm : Form
    {
        private readonly TargetCatalogStore _catalog;
        private readonly ListView _tasks;
        private readonly ComboBox _target;
        private readonly TextBox _workspace;
        private readonly Button _save;
        private readonly Label _status;
        private readonly IReadOnlyList<TargetProfileSnapshot> _targets;

        public PendingAutomationAssignmentForm(TargetCatalogStore catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _targets = catalog.Load().Targets;
            Text = UiLocalization.Text("Automation.Assignment.Title", "Assign old automation targets");
            MinimumSize = new Size(680, 440);
            Size = new Size(780, 540);
            StartPosition = FormStartPosition.CenterParent;
            UiTheme.ApplyForm(this);
            _tasks = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = false, TabIndex = 0,
                AccessibleName = UiLocalization.Text("Automation.Assignment.Tasks", "Unassigned tasks")
            };
            _tasks.Columns.Add(UiLocalization.Text("Automation.Assignment.Task", "Task"), 280);
            _tasks.Columns.Add(UiLocalization.Text("Automation.Assignment.Directory", "Working directory"), 340);
            UiTheme.ApplyListView(_tasks);
            _target = new ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 2,
                AccessibleName = UiLocalization.Text("Automation.Assignment.Target", "Target"),
                DisplayMember = "Name"
            };
            foreach (var target in _targets)
            {
                _target.Items.Add(new TargetChoice(target));
            }
            _workspace = new ThemedTextBox
            {
                Dock = DockStyle.Fill, TabIndex = 4,
                AccessibleName = UiLocalization.Text("Automation.Assignment.Directory", "Working directory")
            };
            UiTheme.ApplyTextBox(_workspace, false, false);
            _save = new Button
            {
                Text = UiLocalization.Text("Automation.Assignment.Save", "Save assignment(&S)"),
                AccessibleName = UiLocalization.Text("Automation.Assignment.SaveAccessible", "Save assignment"),
                AutoSize = true, TabIndex = 0
            };
            UiTheme.ApplyDialogButton(_save, UiGlyph.Ok);
            var close = new Button
            {
                Text = UiLocalization.Text("Dialog.Button.Close", "Close(&C)"),
                AccessibleName = UiLocalization.Text("Dialog.Accessible.Close", "Close"), AutoSize = true,
                TabIndex = 1, DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(close, UiGlyph.Cancel);
            CancelButton = close;
            _status = new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, TabStop = false,
                Text = UiLocalization.Text("Automation.Assignment.Intro", "Choose a task and target, then confirm its working directory. Saved tasks remain disabled; original data is retained.")
            };
            UiTheme.ApplyLabel(_status, false);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding), TabStop = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (var row = 1; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_tasks, 0, 0);
            layout.SetColumnSpan(_tasks, 2);
            layout.Controls.Add(CreateLabel(UiLocalization.Text("Automation.Assignment.TargetLabel", "Target(&T)"), 1), 0, 1);
            layout.Controls.Add(_target, 1, 1);
            layout.Controls.Add(CreateLabel(UiLocalization.Text("Automation.Assignment.DirectoryLabel", "Working directory(&W)"), 3), 0, 2);
            layout.Controls.Add(_workspace, 1, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 5 };
            actions.Controls.Add(_save);
            actions.Controls.Add(close);
            layout.Controls.Add(actions, 0, 3);
            layout.SetColumnSpan(actions, 2);
            layout.Controls.Add(_status, 0, 4);
            layout.SetColumnSpan(_status, 2);
            Controls.Add(layout);
            _tasks.SelectedIndexChanged += (sender, e) => UpdateSelection();
            _target.SelectedIndexChanged += (sender, e) => UpdateSaveAvailability();
            _workspace.TextChanged += (sender, e) => UpdateSaveAvailability();
            _save.Click += (sender, e) => SaveSelected();
            ActiveControl = _tasks;
            LoadTasks();
        }

        private static Label CreateLabel(string text, int tabIndex)
        {
            var label = new Label { Text = text, AutoSize = true, TabStop = false, UseMnemonic = true, TabIndex = tabIndex };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private PendingAutomationAssignment Selected => _tasks.SelectedItems.Count == 0
            ? null : _tasks.SelectedItems[0].Tag as PendingAutomationAssignment;

        private void LoadTasks()
        {
            _tasks.Items.Clear();
            foreach (var pending in _catalog.ListPendingAutomations())
            {
                _tasks.Items.Add(new ListViewItem(new[]
                {
                    pending.Definition.Name ?? pending.Definition.Id,
                    pending.Definition.Context == null ? string.Empty : pending.Definition.Context.WorkspacePath ?? string.Empty
                }) { Tag = pending });
            }
            UpdateSelection();
            if (_tasks.Items.Count == 0)
            {
                _status.Text = UiLocalization.Text("Automation.Assignment.Empty", "No old tasks need a target assignment.");
            }
        }

        private void UpdateSelection()
        {
            var selected = Selected;
            _target.SelectedIndex = -1;
            _target.Enabled = selected != null && selected.ReservedTargetId == null;
            if (selected != null && selected.ReservedTargetId != null)
            {
                for (var index = 0; index < _target.Items.Count; index++)
                {
                    if (((TargetChoice)_target.Items[index]).Target.Id == selected.ReservedTargetId)
                        _target.SelectedIndex = index;
                }
            }
            _workspace.Text = selected == null || selected.Definition.Context == null
                ? string.Empty : selected.Definition.Context.WorkspacePath ?? string.Empty;
            UpdateSaveAvailability();
        }

        private void UpdateSaveAvailability()
        {
            _save.Enabled = Selected != null && _target.SelectedItem != null && !string.IsNullOrWhiteSpace(_workspace.Text);
        }

        private void SaveSelected()
        {
            var selected = Selected;
            var choice = _target.SelectedItem as TargetChoice;
            if (selected == null || choice == null) return;
            try
            {
                _catalog.AssignPendingAutomation(selected.Definition.Id, choice.Target.Id, _workspace.Text);
                LoadTasks();
                _status.Text = UiLocalization.Text("Automation.Assignment.Saved", "Target assigned. The task remains disabled; enable or run it from that target's automation list.");
            }
            catch (Exception ex)
            {
                // Reload reservations without discarding the user's editable directory.
                var directory = _workspace.Text;
                var id = selected.Definition.Id;
                try
                {
                    LoadTasks();
                    foreach (ListViewItem row in _tasks.Items)
                    {
                        if (((PendingAutomationAssignment)row.Tag).Definition.Id == id) row.Selected = true;
                    }
                    _workspace.Text = directory;
                }
                catch (Exception)
                {
                    _workspace.Text = directory;
                }
                _status.Text = string.Format(UiLocalization.Text("Automation.Assignment.Failed", "Assignment failed: {0}"), ex.Message);
            }
        }

        private sealed class TargetChoice
        {
            public TargetChoice(TargetProfileSnapshot target) { Target = target; }
            public TargetProfileSnapshot Target { get; }
            public string Name => TargetManagerForm.DisplayName(Target);
        }
    }
}
