using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class RuntimeSwitchWaitForm : Form
    {
        private readonly Func<Task> _operation;
        private readonly TextBox _messageTextBox;
        private readonly ProgressBar _progressBar;

        public RuntimeSwitchWaitForm(string title, string message, Func<Task> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            _operation = operation;

            Text = string.IsNullOrWhiteSpace(title) ? "Please Wait" : title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 150);
            UiTheme.ApplyForm(this);

            _messageTextBox = new TextBox
            {
                AccessibleName = Text,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.None,
                TabIndex = 0,
                Text = message ?? string.Empty
            };
            UiTheme.ApplyTextBox(_messageTextBox, false, true);

            _progressBar = new ProgressBar
            {
                AccessibleName = Text,
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25,
                TabStop = false
            };

            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.Controls.Add(_messageTextBox, 0, 0);
            layout.Controls.Add(_progressBar, 0, 1);
            Controls.Add(layout);

            Shown += RuntimeSwitchWaitForm_Shown;
        }

        public Exception OperationException { get; private set; }

        private async void RuntimeSwitchWaitForm_Shown(object sender, EventArgs e)
        {
            _messageTextBox.Focus();
            await Task.Yield();

            try
            {
                await _operation().ConfigureAwait(true);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                OperationException = ex;
                DialogResult = DialogResult.Abort;
            }
            finally
            {
                Close();
            }
        }
    }
}
