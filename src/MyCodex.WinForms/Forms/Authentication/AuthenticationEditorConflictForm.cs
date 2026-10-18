using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Lets the user choose which side wins when settings changed outside the config editor.
    public sealed class AuthenticationEditorConflictForm : Form
    {
        internal const string KeepMine = "keepMine";
        internal const string KeepTheirs = "keepTheirs";
        private readonly ListBox _keys;

        public AuthenticationEditorConflictForm(IEnumerable<string> keys)
        {
            Text = AccessibleName = UiLocalization.Text("AuthenticationConflict.Title", "Configuration changed while editing");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 320);
            Size = new Size(620, 380);
            MinimizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);
            var message = new Label
            {
                Text = UiLocalization.Text("AuthenticationConflict.Message", "While the editor was open, another program, such as MyCodex settings or Codex, changed these settings that you also edited. Overwrite them with your changes, or keep the other program's values? Your other changes are saved either way."),
                AutoSize = true, Dock = DockStyle.Fill, TabStop = false
            };
            _keys = new ListBox
            {
                Dock = DockStyle.Fill, TabIndex = 0, IntegralHeight = false,
                AccessibleName = UiLocalization.Text("AuthenticationConflict.Keys", "Conflicting settings")
            };
            UiTheme.ApplyListBox(_keys);
            foreach (var key in keys ?? new string[0]) { _keys.Items.Add(key); }
            if (_keys.Items.Count > 0) { _keys.SelectedIndex = 0; }
            var mine = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationConflict.KeepMine", "&Overwrite with my changes"), 0);
            var theirs = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationConflict.KeepTheirs", "&Keep their changes"), 1);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Cancel", "&Cancel"), 2);
            mine.Click += (sender, args) => Choose(KeepMine);
            theirs.Click += (sender, args) => Choose(KeepTheirs);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = 1, TabStop = false };
            actions.Controls.AddRange(new Control[] { mine, theirs, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(message, 0, 0);
            root.Controls.Add(_keys, 0, 1);
            root.Controls.Add(actions, 0, 2);
            Controls.Add(root);
            Shown += (sender, args) => _keys.Focus();
        }

        // keepMine, keepTheirs, or null when the user cancels.
        public string Policy { get; private set; }

        private void Choose(string policy)
        {
            Policy = policy;
            DialogResult = DialogResult.OK;
        }
    }
}
