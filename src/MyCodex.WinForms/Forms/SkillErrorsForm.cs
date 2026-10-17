using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class SkillErrorsForm : Form
    {
        private readonly SplitContainer _splitContainer;
        private readonly ListBox _errorListBox;
        private readonly TextBox _detailTextBox;
        private readonly Button _closeButton;

        public SkillErrorsForm(IReadOnlyCollection<AppServerSkillError> errors)
        {
            Text = UiLocalization.Text("SkillErrorsForm.Title", "Skill Errors");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            Size = new Size(900, 560);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _errorListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                AccessibleName = UiLocalization.Text("SkillErrorsForm.Accessible.Errors", "Errors"),
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_errorListBox);
            _detailTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AccessibleName = UiLocalization.Text("SkillErrorsForm.Accessible.Details", "Details"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_detailTextBox, true, true);
            _closeButton = new Button
            {
                Text = UiLocalization.Text("SkillErrorsForm.Button.Close", "Close(&C)"),
                AutoSize = true,
                AccessibleName = UiLocalization.Text("SkillErrorsForm.Accessible.Close", "Close"),
                TabIndex = 2
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _closeButton.Click += CloseButton_Click;
            _errorListBox.SelectedIndexChanged += ErrorListBox_SelectedIndexChanged;
            Shown += OnShown;
            Resize += OnResize;

            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.None,
                BackColor = UiTheme.BorderColor,
                TabStop = false
            };
            BuildLayout();
            BindErrors(errors);
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ErrorListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDetailText();
        }

        private void OnShown(object sender, EventArgs e)
        {
            ApplySplitLayout();
        }

        private void OnResize(object sender, EventArgs e)
        {
            ApplySplitLayout();
        }

        private void BuildLayout()
        {
            _splitContainer.Panel1.Controls.Add(_errorListBox);
            _splitContainer.Panel2.Controls.Add(_detailTextBox);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0),
                Margin = new Padding(0),
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonPanel.Controls.Add(_closeButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_splitContainer, 0, 0);
            root.Controls.Add(buttonPanel, 0, 1);

            Controls.Add(root);
            CancelButton = _closeButton;
        }

        private void ApplySplitLayout()
        {
            if (_splitContainer == null || _splitContainer.Height <= 0)
            {
                return;
            }

            try
            {
                var availableHeight = _splitContainer.Height;
                var minTopHeight = Math.Min(UiTheme.Scale(180), Math.Max(UiTheme.Scale(80), availableHeight / 3));
                var minBottomHeight = Math.Min(UiTheme.Scale(160), Math.Max(UiTheme.Scale(80), availableHeight / 3));
                _splitContainer.Panel1MinSize = minTopHeight;
                _splitContainer.Panel2MinSize = minBottomHeight;

                var desiredTopHeight = UiTheme.Scale(240);
                var maxTopHeight = Math.Max(minTopHeight, _splitContainer.Height - _splitContainer.SplitterWidth - _splitContainer.Panel2MinSize);
                _splitContainer.SplitterDistance = Math.Max(minTopHeight, Math.Min(desiredTopHeight, maxTopHeight));
            }
            catch
            {
                // Keep current split state during initialization/resizing edge cases.
            }
        }

        private void BindErrors(IReadOnlyCollection<AppServerSkillError> errors)
        {
            _errorListBox.BeginUpdate();
            try
            {
                _errorListBox.Items.Clear();
                foreach (var error in (errors ?? Array.Empty<AppServerSkillError>())
                    .Where(item => item != null)
                    .OrderBy(item => item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    _errorListBox.Items.Add(new SkillErrorListItem(error));
                }
            }
            finally
            {
                _errorListBox.EndUpdate();
            }

            if (_errorListBox.Items.Count > 0)
            {
                _errorListBox.SelectedIndex = 0;
            }
            else
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailTextBox, string.Empty);
            }
        }

        private void UpdateDetailText()
        {
            var item = _errorListBox.SelectedItem as SkillErrorListItem;
            if (item == null || item.Error == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailTextBox, string.Empty);
                return;
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_detailTextBox, string.Format(
                UiLocalization.Text("SkillErrorsForm.Details.Template", "Path: {0}{1}{1}Error: {2}"),
                string.IsNullOrWhiteSpace(item.Error.Path)
                    ? UiLocalization.Text("SkillErrorsForm.Value.Unknown", "(Unknown)")
                    : item.Error.Path,
                Environment.NewLine,
                string.IsNullOrWhiteSpace(item.Error.Message)
                    ? UiLocalization.Text("SkillErrorsForm.Value.NoDetail", "(No details)")
                    : item.Error.Message));
        }

        private sealed class SkillErrorListItem
        {
            public SkillErrorListItem(AppServerSkillError error)
            {
                Error = error;
            }

            public AppServerSkillError Error { get; private set; }

            public override string ToString()
            {
                if (Error == null)
                {
                    return UiLocalization.Text("SkillErrorsForm.Value.NoErrors", "(No errors)");
                }

                return string.Format(
                    "{0} - {1}",
                    string.IsNullOrWhiteSpace(Error.Path)
                        ? UiLocalization.Text("SkillErrorsForm.Value.UnknownPath", "(Unknown path)")
                        : Error.Path,
                    string.IsNullOrWhiteSpace(Error.Message)
                        ? UiLocalization.Text("SkillErrorsForm.Value.NoDetail", "(No details)")
                        : Error.Message);
            }
        }
    }
}
