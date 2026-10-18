using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitRemoteEditForm : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly TextBox _fetchUrlTextBox;
        private readonly TextBox _pushUrlTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly bool _isEditMode;

        public GitRemoteEditForm(GitRemoteEntry entry)
        {
            _isEditMode = entry != null;

            Text = _isEditMode
                ? UiLocalization.Text("GitRemoteEditForm.Title.Edit", "Edit Remote")
                : UiLocalization.Text("GitRemoteEditForm.Title.Add", "Add Remote");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 230);
            UiTheme.ApplyForm(this);

            _nameTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitRemoteEditForm.Accessible.Name", "Name"), 0);
            _fetchUrlTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitRemoteEditForm.Accessible.FetchUrl", "Fetch URL"), 1);
            _pushUrlTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitRemoteEditForm.Accessible.PushUrl", "Push URL"), 2);
            _okButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRemoteEditForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("GitRemoteEditForm.Accessible.Save", "Save"),
                3,
                112);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitRemoteEditForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("GitRemoteEditForm.Accessible.Cancel", "Cancel"),
                4,
                112);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            if (entry != null)
            {
                _nameTextBox.Text = entry.Name ?? string.Empty;
                _nameTextBox.ReadOnly = true;
                _fetchUrlTextBox.Text = entry.FetchUrl ?? string.Empty;
                _pushUrlTextBox.Text = string.Equals(entry.FetchUrl, entry.PushUrl, StringComparison.Ordinal)
                    ? string.Empty
                    : entry.PushUrl ?? string.Empty;
            }

            _okButton.Click += (sender, e) => AcceptValue();
            _cancelButton.Click += (sender, e) => DialogResult = DialogResult.Cancel;
            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += (sender, e) => FocusInitialControl();
        }

        public string RemoteName
        {
            get { return (_nameTextBox.Text ?? string.Empty).Trim(); }
        }

        public string FetchUrl
        {
            get { return (_fetchUrlTextBox.Text ?? string.Empty).Trim(); }
        }

        public string PushUrl
        {
            get
            {
                var pushUrl = (_pushUrlTextBox.Text ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(pushUrl) ? FetchUrl : pushUrl;
            }
        }

        private void AcceptValue()
        {
            if (string.IsNullOrWhiteSpace(RemoteName))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitRemoteEditForm.Message.NameRequired", "Enter the remote name first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _nameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(FetchUrl))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitRemoteEditForm.Message.FetchUrlRequired", "Enter the fetch URL first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _fetchUrlTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitRemoteEditForm.Label.Name", "Name(&N)")), 0, 0);
            root.Controls.Add(_nameTextBox, 0, 1);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitRemoteEditForm.Label.FetchUrl", "Fetch URL(&F)")), 0, 2);
            root.Controls.Add(_fetchUrlTextBox, 0, 3);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("GitRemoteEditForm.Label.PushUrl", "Push URL(&P)")), 0, 4);
            root.Controls.Add(_pushUrlTextBox, 0, 5);

            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 6);
            Controls.Add(root);
        }

        private void FocusInitialControl()
        {
            var target = _isEditMode ? _fetchUrlTextBox : _nameTextBox;
            target.SelectAll();
            target.Focus();
        }
    }
}
