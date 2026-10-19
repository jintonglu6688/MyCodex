using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationConfigEditForm : Form
    {
        private readonly ThemedTextBox _editor;
        private readonly ThemedTextBox _details;
        private readonly Label _status;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly Func<string, CancellationToken, Task> _save;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _saving;
        private bool _disposed;
        public bool SaveOutcomeUnknown { get; private set; }

        public AuthenticationConfigEditForm(string title, string hint, string text, Func<string, CancellationToken, Task> save)
            : this(title, hint, text, null, save)
        {
        }

        // details: optional read-only notes shown below the editor, such as settings that never apply.
        public AuthenticationConfigEditForm(string title, string hint, string text, string details, Func<string, CancellationToken, Task> save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            Text = AccessibleName = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(650, 450);
            Size = new Size(850, 650);
            UiTheme.ApplyForm(this);
            _editor = new ThemedTextBox
            {
                Text = text ?? string.Empty, Multiline = true, AcceptsReturn = true, AcceptsTab = false,
                WordWrap = false, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, TabIndex = 0,
                MaxLength = 1024 * 1024, AccessibleName = UiLocalization.Text("AuthenticationConfig.Editor", "TOML configuration")
            };
            UiTheme.ApplyTextBox(_editor, false, false);
            _status = new Label { AutoSize = true, Dock = DockStyle.Fill, TabStop = false };
            _saveButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Save", "&Save"), 0);
            _cancelButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Cancel", "&Cancel"), 1);
            _cancelButton.DialogResult = DialogResult.Cancel;
            CancelButton = _cancelButton;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = 1, TabStop = false };
            actions.Controls.AddRange(new Control[] { _saveButton, _cancelButton });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = hint, AutoSize = true, Dock = DockStyle.Fill, TabStop = false }, 0, 0);
            root.Controls.Add(_editor, 0, 1);
            if (!string.IsNullOrEmpty(details))
            {
                _details = new ThemedTextBox
                {
                    Text = details, ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill, Height = 90, TabStop = false,
                    AccessibleName = UiLocalization.Text("AuthenticationConfig.Inactive", "Settings that do not apply")
                };
                UiTheme.ApplyTextBox(_details, false, false);
                root.Controls.Add(_details, 0, 2);
            }
            root.Controls.Add(_status, 0, 3);
            root.Controls.Add(actions, 0, 4);
            Controls.Add(root);
            Shown += (sender, args) => { _editor.Focus(); _editor.Select(0, 0); };
            _saveButton.Click += async (sender, args) => await SaveAsync();
            FormClosing += (sender, args) => { if (_saving) args.Cancel = true; };
        }

        internal async Task SaveAsync()
        {
            if (_saving || _disposed || SaveOutcomeUnknown) return;
            _saving = true;
            _editor.ReadOnly = true;
            _saveButton.Enabled = _cancelButton.Enabled = false;
            _status.Text = UiLocalization.Text("AuthenticationProviderEdit.Status.Saving", "Saving configuration…");
            try
            {
                await _save(_editor.Text, _lifetime.Token);
                _saving = false;
                DialogResult = DialogResult.OK;
            }
            catch (Exception error)
            {
                SaveOutcomeUnknown = error is AuthenticationCenterException failure && failure.Code == "operation_unknown";
                _status.Text = AuthenticationCenterUiText.Error(error);
                if (!(error is OperationCanceledException)) AuthenticationCenterUiText.ShowError(this, _status.Text);
                _editor.Focus();
            }
            finally
            {
                _saving = false;
                if (!IsDisposed)
                {
                    _editor.ReadOnly = false;
                    _saveButton.Enabled = !SaveOutcomeUnknown;
                    _cancelButton.Enabled = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
