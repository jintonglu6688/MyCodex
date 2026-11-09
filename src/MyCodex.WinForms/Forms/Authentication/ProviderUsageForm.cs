using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // User Center for a provider other than a ChatGPT account: the usage its saved query reports.
    public sealed class ProviderUsageForm : Form
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly IScreenReaderService _screenReaderService;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ListBox _detailsListBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _settingsButton;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private string _providerId;
        private bool _busy;
        private bool _disposed;

        public ProviderUsageForm(IAuthenticationCenterClient client, IScreenReaderService screenReaderService = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _screenReaderService = screenReaderService;
            Text = UiLocalization.Text("UserCenterForm.Title", "User Center");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 420);
            Size = new Size(720, 520);
            KeyPreview = true;
            UiTheme.ApplyForm(this);
            var title = new Label
            {
                Text = UiLocalization.Text("ProviderUsage.Header", "Provider usage"),
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            _detailsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("ProviderUsage.Accessible.Details", "Usage details"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                ItemHeight = 28,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_detailsListBox);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            _refreshButton = AuthenticationCenterUiText.Button(UiLocalization.Text("UserCenterForm.Button.Refresh", "&Refresh"), 1);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            _settingsButton = AuthenticationCenterUiText.Button(UiLocalization.Text("ProviderUsage.Button.Settings", "Usage &settings"), 2);
            var close = AuthenticationCenterUiText.Button(UiLocalization.Text("UserCenterForm.Button.Close", "&Close"), 3);
            close.DialogResult = DialogResult.Cancel;
            UiTheme.ApplyButton(close, UiGlyph.Cancel);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, TabStop = false };
            buttons.Controls.AddRange(new Control[] { _refreshButton, _settingsButton, close });
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(0, 12, 0, 0), TabStop = false };
            footer.Controls.Add(_statusLabel);
            footer.Controls.Add(buttons);
            var main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UiTheme.OuterPadding), TabStop = false };
            main.Controls.Add(_detailsListBox);
            main.Controls.Add(footer);
            main.Controls.Add(title);
            Controls.Add(main);
            CancelButton = close;
            _refreshButton.Click += async (sender, args) => await RefreshAsync(true);
            _settingsButton.Click += async (sender, args) => await OpenSettingsAsync();
            // Automatic refreshes update the list quietly; only requested ones are spoken.
            _timer.Tick += async (sender, args) => await RefreshAsync(false);
            KeyDown += async (sender, args) =>
            {
                if (args.KeyCode == Keys.F5)
                {
                    args.Handled = args.SuppressKeyPress = true;
                    await RefreshAsync(true);
                }
            };
            Shown += async (sender, args) =>
            {
                await RefreshAsync(false);
                if (!IsDisposed) _detailsListBox.Focus();
            };
            UpdateButtons();
        }

        internal async Task RefreshAsync(bool announce)
        {
            if (_busy || IsDisposed) return;
            _busy = true;
            UpdateButtons();
            _statusLabel.Text = UiLocalization.Text("ProviderUsage.Status.Refreshing", "Querying usage…");
            var lines = new List<string>();
            string status;
            var interval = 0;
            try
            {
                var snapshot = await _client.ReadAsync(_lifetime.Token);
                var currentId = string.IsNullOrEmpty(snapshot.LiveState) || snapshot.LiveState == "current" ? snapshot.CurrentProviderId : null;
                var provider = snapshot.Providers?.FirstOrDefault(item => item.Id == currentId);
                _providerId = null;
                if (!snapshot.SupportsProviderUsage)
                {
                    status = UiLocalization.Text("ProviderUsage.Status.BackendUnsupported", "The authentication backend of this target cannot query usage. Update it from the authentication center.");
                }
                else if (provider == null || provider.Kind == "chatgpt")
                {
                    status = UiLocalization.Text("ProviderUsage.Status.NoProvider", "The current configuration is not a saved provider of the authentication center.");
                }
                else
                {
                    _providerId = provider.Id;
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Provider", "Provider: {0}"), provider.Name));
                    var settings = await _client.ReadUsageAsync(provider.Id, _lifetime.Token);
                    if (!settings.Configured || !settings.Enabled)
                    {
                        status = UiLocalization.Text("ProviderUsage.Status.NotConfigured", "Usage queries are not turned on for this provider. Choose Usage settings to set them up.");
                    }
                    else
                    {
                        lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Method", "Query method: {0}"),
                            AuthenticationUsagePresentation.TemplateName(settings.TemplateType)));
                        var result = await _client.QueryUsageAsync(provider.Id, _lifetime.Token);
                        lines.AddRange(AuthenticationUsagePresentation.Lines(result));
                        interval = settings.AutoQueryInterval;
                        status = result.Success
                            ? string.Format(UiLocalization.Text("ProviderUsage.Status.Updated", "Usage updated at {0}."), DateTime.Now.ToString("T", CultureInfo.CurrentCulture))
                            : UiLocalization.Text("ProviderUsage.Status.Failed", "The usage query failed. The details are in the list.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                status = AuthenticationCenterUiText.Error(error);
            }
            finally
            {
                _busy = false;
            }
            if (IsDisposed) return;
            // The list is the focus target, so it always says why it has no usage.
            if (lines.Count < 2) lines.Add(status);
            ShowLines(lines);
            _statusLabel.Text = status;
            _timer.Stop();
            if (interval > 0)
            {
                _timer.Interval = interval * 60000;
                _timer.Start();
            }
            UpdateButtons();
            if (announce) _screenReaderService?.Speak(status, true, false, true);
        }

        private void ShowLines(List<string> lines)
        {
            var selected = _detailsListBox.SelectedIndex;
            _detailsListBox.BeginUpdate();
            try
            {
                _detailsListBox.Items.Clear();
                _detailsListBox.Items.AddRange(lines.Cast<object>().ToArray());
                _detailsListBox.SelectedIndex = Math.Max(0, Math.Min(selected, lines.Count - 1));
            }
            finally
            {
                _detailsListBox.EndUpdate();
            }
        }

        private async Task OpenSettingsAsync()
        {
            if (_busy || _providerId == null) return;
            bool saved;
            try
            {
                saved = await AuthenticationUsageForm.EditAsync(this, _client, _providerId, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                _statusLabel.Text = AuthenticationCenterUiText.Error(error);
                AuthenticationCenterUiText.ShowError(this, _statusLabel.Text);
                return;
            }
            if (IsDisposed) return;
            if (saved) await RefreshAsync(false);
            if (!IsDisposed) _detailsListBox.Focus();
        }

        private void UpdateButtons()
        {
            _refreshButton.Enabled = !_busy;
            _settingsButton.Enabled = !_busy && _providerId != null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _timer.Dispose();
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
