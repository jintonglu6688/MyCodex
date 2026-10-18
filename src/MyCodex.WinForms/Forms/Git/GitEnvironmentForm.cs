using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitEnvironmentForm : Form
    {
        private readonly IGitEnvironmentService _environmentService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly ListBox _statusListBox;
        private readonly TextBox _detailsTextBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _setNameButton;
        private readonly Button _setEmailButton;
        private readonly Button _copySummaryButton;
        private readonly Button _copyDetailsButton;
        private readonly Button _closeButton;
        private CancellationTokenSource _refreshCancellation;
        private GitEnvironmentSnapshot _snapshot;

        public GitEnvironmentForm(IGitEnvironmentService environmentService, IScreenReaderService screenReaderService)
        {
            _environmentService = environmentService ?? throw new ArgumentNullException(nameof(environmentService));
            _screenReaderService = screenReaderService;

            Text = UiLocalization.Text("GitEnvironmentForm.Title", "Git Environment");
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(760, 500);
            Size = new Size(900, 620);
            UiTheme.ApplyForm(this);

            _statusListBox = CreateStatusListBox();
            _detailsTextBox = CreateDetailsTextBox();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitEnvironmentForm.Status.Ready", "Press Refresh to check Git environment."),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _refreshButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitEnvironmentForm.Accessible.Refresh", "Refresh"), 0, 112);
            _setNameButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.SetName", "Set Name(&N)"), UiLocalization.Text("GitEnvironmentForm.Accessible.SetName", "Set name"), 1, 120);
            _setEmailButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.SetEmail", "Set Email(&E)"), UiLocalization.Text("GitEnvironmentForm.Accessible.SetEmail", "Set email"), 2, 120);
            _copySummaryButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.CopySummary", "Copy Summary(&S)"), UiLocalization.Text("GitEnvironmentForm.Accessible.CopySummary", "Copy summary"), 3, 140);
            _copyDetailsButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.CopyDetails", "Copy Details(&D)"), UiLocalization.Text("GitEnvironmentForm.Accessible.CopyDetails", "Copy details"), 4, 132);
            _closeButton = CreateButton(UiLocalization.Text("GitEnvironmentForm.Button.Close", "Close(&C)"), UiLocalization.Text("GitEnvironmentForm.Accessible.Close", "Close"), 5, 112);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_setNameButton, UiGlyph.User);
            UiTheme.ApplyButton(_setEmailButton, UiGlyph.Account);
            UiTheme.ApplyButton(_copySummaryButton, UiGlyph.Info);
            UiTheme.ApplyButton(_copyDetailsButton, UiGlyph.CommandOutput);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _refreshButton.Click += async (sender, e) => await RefreshStatusAsync();
            _setNameButton.Click += async (sender, e) => await SetUserNameAsync();
            _setEmailButton.Click += async (sender, e) => await SetUserEmailAsync();
            _copySummaryButton.Click += (sender, e) => CopySummary();
            _copyDetailsButton.Click += (sender, e) => CopyDetails();
            _closeButton.Click += (sender, e) => Close();
            CancelButton = _closeButton;
            FormClosing += GitEnvironmentForm_FormClosing;
            KeyDown += GitEnvironmentForm_KeyDown;

            BuildLayout();
            Shown += async (sender, e) =>
            {
                Activate();
                FocusStatusList();
                await RefreshStatusAsync();
            };
        }

        private async void GitEnvironmentForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && _refreshCancellation == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                FormInteractionConventions.SpeakRefresh(_screenReaderService);
                await RefreshStatusAsync();
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitEnvironmentForm.Heading", "Git Environment"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_statusListBox, 0, 1);
            root.Controls.Add(_detailsTextBox, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(CreateFooter(), 0, 4);
            Controls.Add(root);
        }

        private Control CreateFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_copyDetailsButton);
            footer.Controls.Add(_copySummaryButton);
            footer.Controls.Add(_setEmailButton);
            footer.Controls.Add(_setNameButton);
            footer.Controls.Add(_refreshButton);
            return footer;
        }

        private async Task RefreshStatusAsync()
        {
            if (_refreshCancellation != null)
            {
                return;
            }

            _refreshCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.Loading", "Checking Git environment..."), false);
            try
            {
                _snapshot = await _environmentService.ReadStatusAsync(_refreshCancellation.Token);
                PopulateSnapshot(_snapshot);
                SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.Loaded", "Git environment loaded."), false);
                FocusStatusList();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.Canceled", "Git environment check canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.Failed", "Git environment check failed."), true);
                _detailsTextBox.Text = ex.Message;
            }
            finally
            {
                _refreshCancellation.Dispose();
                _refreshCancellation = null;
                SetBusy(false);
            }
        }

        private async Task SetUserNameAsync()
        {
            using (var form = new GitConfigValueForm(
                       UiLocalization.Text("GitConfigValueForm.Title.UserName", "Set Git User Name"),
                       UiLocalization.Text("GitConfigValueForm.Label.UserName", "User name"),
                       UiLocalization.Text("GitConfigValueForm.Accessible.UserName", "User name"),
                       _snapshot != null ? _snapshot.GlobalUserName : string.Empty))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                await _environmentService.SetGlobalUserNameAsync(form.Value, CancellationToken.None);
                SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.NameSaved", "Git user name saved."), true);
                await RefreshStatusAsync();
            }
        }

        private async Task SetUserEmailAsync()
        {
            using (var form = new GitConfigValueForm(
                       UiLocalization.Text("GitConfigValueForm.Title.UserEmail", "Set Git User Email"),
                       UiLocalization.Text("GitConfigValueForm.Label.UserEmail", "User email"),
                       UiLocalization.Text("GitConfigValueForm.Accessible.UserEmail", "User email"),
                       _snapshot != null ? _snapshot.GlobalUserEmail : string.Empty))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                await _environmentService.SetGlobalUserEmailAsync(form.Value, CancellationToken.None);
                SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.EmailSaved", "Git user email saved."), true);
                await RefreshStatusAsync();
            }
        }

        private void PopulateSnapshot(GitEnvironmentSnapshot snapshot)
        {
            _statusListBox.BeginUpdate();
            _statusListBox.Items.Clear();
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.Runtime", "Runtime"), snapshot.Runtime);
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.GitVersion", "Git version"), DisplayValue(snapshot.GitVersion));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.GitPath", "Git path"), DisplayValue(snapshot.GitPath));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.UserName", "User name"), DisplayValue(snapshot.GlobalUserName));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.UserEmail", "User email"), DisplayValue(snapshot.GlobalUserEmail));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.CredentialHelper", "Credential helper"), DisplayValue(snapshot.CredentialHelper));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.Gcm", "Git Credential Manager"), DisplayValue(snapshot.GitCredentialManager));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.SshKeys", "SSH keys"), DisplayValue(snapshot.SshKeys));
            AddItem(UiLocalization.Text("GitEnvironmentForm.Item.GitHubSsh", "GitHub SSH"), DisplayValue(snapshot.GitHubSsh));
            _statusListBox.EndUpdate();
            if (_statusListBox.Items.Count > 0)
            {
                _statusListBox.SelectedIndex = 0;
            }

            _detailsTextBox.Text = snapshot.DetailedOutput ?? string.Empty;
        }

        private void AddItem(string name, string value)
        {
            _statusListBox.Items.Add(string.Format(
                UiLocalization.Text("GitEnvironmentForm.Item.Template", "{0}: {1}"),
                name ?? string.Empty,
                value ?? string.Empty));
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("GitEnvironmentForm.Value.NotDetected", "Not detected")
                : value;
        }

        private void CopySummary()
        {
            var text = BuildSummaryText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Clipboard.SetText(text);
            SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.CopiedSummary", "Copied Git environment summary."), true);
        }

        private void CopyDetails()
        {
            if (string.IsNullOrWhiteSpace(_detailsTextBox.Text))
            {
                return;
            }

            Clipboard.SetText(_detailsTextBox.Text);
            SetStatus(UiLocalization.Text("GitEnvironmentForm.Status.CopiedDetails", "Copied Git environment details."), true);
        }

        private string BuildSummaryText()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _statusListBox.Items.Count; i++)
            {
                builder.AppendLine(_statusListBox.Items[i].ToString());
            }

            return builder.ToString();
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
            _setNameButton.Enabled = !busy;
            _setEmailButton.Enabled = !busy;
            _copySummaryButton.Enabled = !busy;
            _copyDetailsButton.Enabled = !busy;
            _closeButton.Enabled = true;
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text;
            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(text, true, false, true);
            }
        }

        private void GitEnvironmentForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_refreshCancellation == null)
            {
                return;
            }

            e.Cancel = true;
            _refreshCancellation.Cancel();
        }

        private static ListBox CreateStatusListBox()
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitEnvironmentForm.Accessible.Status", "Status"),
                TabIndex = 6
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private void FocusStatusList()
        {
            if (_statusListBox.Items.Count == 0)
            {
                return;
            }

            if (_statusListBox.SelectedIndex < 0)
            {
                _statusListBox.SelectedIndex = 0;
            }

            _statusListBox.Focus();
        }

        private static TextBox CreateDetailsTextBox()
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitEnvironmentForm.Accessible.Details", "Details"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 7
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, int width)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = width,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 4, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
