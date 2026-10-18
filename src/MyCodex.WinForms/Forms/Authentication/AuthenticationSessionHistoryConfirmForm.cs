using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Confirms a session history change and explains what moves with it.
    public sealed class AuthenticationSessionHistoryConfirmForm : Form
    {
        public AuthenticationSessionHistoryConfirmForm(string title, string message)
        {
            Text = AccessibleName = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 340);
            Size = new Size(640, 420);
            MinimizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);
            // A focused read-only box lets screen readers read the whole explanation.
            var details = new TextBox
            {
                Text = message, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill, TabIndex = 0, AccessibleName = title
            };
            UiTheme.ApplyTextBox(details, false, true);
            var ok = AuthenticationCenterUiText.Button(UiLocalization.Text("Common.OK", "&OK"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("Common.Cancel", "Cancel(&A)"), 1);
            ok.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok;
            CancelButton = cancel;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = 1, TabStop = false };
            actions.Controls.AddRange(new Control[] { ok, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(details, 0, 0);
            root.Controls.Add(actions, 0, 1);
            Controls.Add(root);
            Shown += (sender, args) => details.Focus();
        }
    }
}
