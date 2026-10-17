using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Updates;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class BundledRuntimeUpdateForm : Form
    {
        private readonly Func<IProgress<string>, IProgress<UpdateDownloadProgress>, CancellationToken, Task<CodexRuntimeUpdateResult>> _install;
        private readonly CancellationToken _shutdown;
        private readonly CodexRuntimeUpdateInfo _info;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _retryButton;
        private readonly Button _cancelButton;
        private CancellationTokenSource _attempt;
        private bool _cancelRequested;
        private bool _allowClose;
        private bool _running;

        internal BundledRuntimeUpdateForm(CodexRuntimeUpdateService service, CodexRuntimeUpdateInfo info, CancellationToken shutdown)
            : this(info, (status, download, token) => service.InstallCheckedAsync(info, status, download, token), shutdown)
        {
        }

        internal BundledRuntimeUpdateForm(CodexRuntimeUpdateInfo info,
            Func<IProgress<string>, IProgress<UpdateDownloadProgress>, CancellationToken, Task<CodexRuntimeUpdateResult>> install,
            CancellationToken shutdown)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _install = install ?? throw new ArgumentNullException(nameof(install));
            _shutdown = shutdown;
            Text = UiLocalization.Text("BundledRuntimeUpdateForm.Title", "Updating Runtime");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(460, 220);
            Size = new Size(560, 260);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill, TabStop = false, UseMnemonic = false,
                Text = UiLocalization.Text("BundledRuntimeUpdateForm.Starting", "Preparing runtime update...")
            };
            _progressBar = new ProgressBar
            {
                AccessibleName = UiLocalization.Text("BundledRuntimeUpdateForm.Accessible.Status", "Update progress"),
                Dock = DockStyle.Fill, TabStop = false, Style = ProgressBarStyle.Marquee
            };
            _retryButton = new Button
            {
                Text = UiLocalization.Text("BundledRuntimeUpdateForm.Retry", "&Retry"),
                AccessibleName = UiLocalization.Text("BundledRuntimeUpdateForm.Accessible.Retry", "Retry"),
                TabIndex = 0, Enabled = false, AutoSize = true
            };
            _cancelButton = new Button
            {
                Text = UiLocalization.Text("BundledRuntimeUpdateForm.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("BundledRuntimeUpdateForm.Accessible.Cancel", "Cancel"),
                TabIndex = 1, AutoSize = true
            };
            UiTheme.ApplyDialogButton(_retryButton, UiGlyph.Download);
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);
            _retryButton.Click += async (sender, args) => await RunUpdateAsync();
            _cancelButton.Click += (sender, args) => RequestCancel();
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, TabStop = false
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_retryButton);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), TabStop = false
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_statusLabel, 0, 0);
            layout.Controls.Add(_progressBar, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            CancelButton = _cancelButton;
            Shown += async (sender, args) => { _cancelButton.Focus(); await RunUpdateAsync(); };
            FormClosing += (sender, args) =>
            {
                if (_allowClose) return;
                _cancelRequested = true;
                if (_running)
                {
                    args.Cancel = true;
                    _cancelButton.Enabled = false;
                    _attempt?.Cancel();
                }
                else
                {
                    _allowClose = true;
                    DialogResult = DialogResult.Cancel;
                }
            };
        }

        internal CodexRuntimeUpdateResult UpdateResult { get; private set; }

        internal async Task RunUpdateAsync()
        {
            if (_running) return;
            _running = true;
            _retryButton.Enabled = false;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _statusLabel.Text = UiLocalization.Text("BundledRuntimeUpdateForm.Starting", "Preparing runtime update...");
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(_shutdown))
            {
                _attempt = attempt;
                attempt.CancelAfter(TimeSpan.FromMinutes(10));
                try
                {
                    var status = new Progress<string>(text =>
                    {
                        if (IsDisposed || !_running || _cancelRequested || !ReferenceEquals(_attempt, attempt)) return;
                        _progressBar.Style = ProgressBarStyle.Marquee;
                        _statusLabel.Text = text;
                    });
                    var download = new Progress<UpdateDownloadProgress>(value =>
                    {
                        if (IsDisposed || !_running || _cancelRequested || value == null || !ReferenceEquals(_attempt, attempt)) return;
                        _progressBar.Style = ProgressBarStyle.Continuous;
                        _progressBar.Value = Math.Max(0, Math.Min(100, value.Percentage));
                        _statusLabel.Text = UiLocalization.Text("BundledRuntimeUpdateForm.Downloading", "Downloading runtime...");
                    });
                    var result = await _install(status, download, attempt.Token);
                    attempt.Token.ThrowIfCancellationRequested();
                    if (result == null || result.TargetTriple != _info.TargetTriple ||
                        UpdateVersionComparer.Compare(result.Version, _info.LatestVersion) < 0)
                        throw new InvalidOperationException(UiLocalization.Text("BundledRuntimeUpdateForm.Incomplete",
                            "The runtime update did not finish."));
                    UpdateResult = result;
                    Finish(DialogResult.OK);
                }
                catch (Exception error)
                {
                    if (_cancelRequested || _shutdown.IsCancellationRequested) Finish(DialogResult.Cancel);
                    else
                    {
                        _progressBar.Style = ProgressBarStyle.Continuous;
                        _progressBar.Value = 0;
                        _statusLabel.Text = string.Format(UiLocalization.Text("BundledRuntimeUpdateForm.Failed",
                            "Runtime update failed. Retry, or cancel to use the existing runtime.\r\n\r\n{0}"), error is OperationCanceledException
                                ? UiLocalization.Text("BundledRuntimeUpdateForm.Timeout", "The update timed out.") : error.Message);
                        _retryButton.Enabled = true;
                    }
                }
                finally { _running = false; _attempt = null; }
            }
        }

        private void RequestCancel()
        {
            _cancelRequested = true;
            if (_running)
            {
                _cancelButton.Enabled = false;
                _attempt?.Cancel();
            }
            else Finish(DialogResult.Cancel);
        }

        private void Finish(DialogResult result)
        {
            _allowClose = true;
            DialogResult = result;
            Close();
        }
    }
}
