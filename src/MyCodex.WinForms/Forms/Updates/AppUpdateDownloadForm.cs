using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Updates;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppUpdateDownloadForm : Form
    {
        private readonly AppUpdateService _service;
        private readonly AppUpdateCheckResult _checkResult;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _actionButton;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _isCompleted;

        public AppUpdateDownloadForm(AppUpdateService service, AppUpdateCheckResult checkResult)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _checkResult = checkResult ?? throw new ArgumentNullException(nameof(checkResult));
            _cancellationTokenSource = new CancellationTokenSource();

            Text = UiLocalization.Text("AppUpdateDownloadForm.Title", "Downloading Update");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 170);
            UiTheme.ApplyForm(this);

            _statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("AppUpdateDownloadForm.Status.Starting", "Preparing download..."),
                TextAlign = ContentAlignment.MiddleLeft
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _progressBar = new ProgressBar
            {
                AccessibleName = UiLocalization.Text("AppUpdateDownloadForm.Accessible.Progress", "Download progress"),
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                TabStop = false
            };

            _actionButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                TabIndex = 0
            };
            UiTheme.ApplyDialogButton(_actionButton, UiGlyph.Cancel);
            _actionButton.Click += ActionButton_Click;

            var buttonPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonPanel.Controls.Add(_actionButton);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_statusLabel, 0, 0);
            layout.Controls.Add(_progressBar, 0, 1);
            layout.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(layout);

            CancelButton = _actionButton;
            Shown += AppUpdateDownloadForm_Shown;
            FormClosing += AppUpdateDownloadForm_FormClosing;
        }

        public AppUpdateDownloadResult DownloadResult { get; private set; }

        private async void AppUpdateDownloadForm_Shown(object sender, EventArgs e)
        {
            await DownloadAsync();
        }

        private async Task DownloadAsync()
        {
            var progress = new Progress<UpdateDownloadProgress>(UpdateProgress);
            try
            {
                DownloadResult = await _service.DownloadAsync(
                    _checkResult,
                    progress,
                    _cancellationTokenSource.Token);
                _isCompleted = true;
                _progressBar.Value = 100;
                _statusLabel.Text = UiLocalization.Text(
                    "AppUpdateDownloadForm.Status.Completed",
                    "Download completed. Press OK to start the installer.");
                _actionButton.Text = UiLocalization.Text("Common.OK", "&OK");
                _actionButton.AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK");
                _actionButton.DialogResult = DialogResult.OK;
                AcceptButton = _actionButton;
                CancelButton = null;
                _actionButton.Focus();
            }
            catch (OperationCanceledException)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            catch (Exception ex)
            {
                _isCompleted = true;
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Abort;
                Close();
            }
        }

        private void UpdateProgress(UpdateDownloadProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            var percent = Math.Max(0, Math.Min(100, progress.Percentage));
            _progressBar.Value = percent;
            _statusLabel.Text = string.Format(
                UiLocalization.Text(
                    "AppUpdateDownloadForm.Status.Downloading.Template",
                    "Downloading update... {0}% ({1} / {2})"),
                percent,
                FormatBytes(progress.BytesReceived),
                progress.TotalBytesToReceive > 0 ? FormatBytes(progress.TotalBytesToReceive) : "?");
        }

        private void ActionButton_Click(object sender, EventArgs e)
        {
            if (_isCompleted)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _cancellationTokenSource.Cancel();
            _actionButton.Enabled = false;
            _statusLabel.Text = UiLocalization.Text("AppUpdateDownloadForm.Status.Cancelling", "Cancelling download...");
        }

        private void AppUpdateDownloadForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_isCompleted && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource.Dispose();
            }

            base.Dispose(disposing);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                bytes = 0;
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? string.Format("{0:0} {1}", value, units[unitIndex])
                : string.Format("{0:0.0} {1}", value, units[unitIndex]);
        }
    }
}
