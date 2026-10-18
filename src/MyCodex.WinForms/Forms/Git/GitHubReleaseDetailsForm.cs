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
    internal sealed class GitHubReleaseDetailsForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _loadCancellation != null;

        private readonly IGitHubCliService _gitHubCliService;
        private readonly GitHubRepositoryEntry _repository;
        private readonly string _tagName;
        private readonly IScreenReaderService _screenReaderService;
        private readonly ListBox _entryListBox;
        private readonly TextBox _contentTextBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;
        private CancellationTokenSource _loadCancellation;

        public GitHubReleaseDetailsForm(
            IGitHubCliService gitHubCliService,
            GitHubRepositoryEntry repository,
            string tagName,
            IScreenReaderService screenReaderService)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _tagName = string.IsNullOrWhiteSpace(tagName) ? string.Empty : tagName.Trim();
            _screenReaderService = screenReaderService;

            Text = string.Format(
                UiLocalization.Text("GitHubReleaseDetailsForm.Title.Template", "{0} Release"),
                DisplayValue(_tagName));
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 560);
            Size = new Size(1020, 680);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _entryListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubReleaseDetailsForm.Accessible.Entries", "Release entries"),
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_entryListBox);

            _contentTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubReleaseDetailsForm.Accessible.Content", "Content"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_contentTextBox, true, true);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _refreshButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubReleaseDetailsForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("GitHubReleaseDetailsForm.Accessible.Refresh", "Refresh"),
                2,
                112);
            _closeButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubReleaseDetailsForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("GitHubReleaseDetailsForm.Accessible.Close", "Close"),
                3,
                112);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _entryListBox.SelectedIndexChanged += (sender, e) => UpdateSelectedEntryContent();
            _refreshButton.Click += async (sender, e) => await LoadReleaseAsync();
            _closeButton.Click += (sender, e) => Close();
            KeyDown += GitHubReleaseDetailsForm_KeyDown;
            FormClosing += GitHubReleaseDetailsForm_FormClosing;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadReleaseAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitHubReleaseDetailsForm.Status.Ready", "Release details are not loaded."), false);
        }

        private async void GitHubReleaseDetailsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && _loadCancellation == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await LoadReleaseAsync();
            }
        }

        private void GitHubReleaseDetailsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
            }
        }

        private async Task LoadReleaseAsync()
        {
            if (_loadCancellation != null)
            {
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubReleaseDetailsForm.Status.Loading", "Loading release details..."), false);
            try
            {
                var release = await _gitHubCliService.GetReleaseDetailsAsync(_repository, _tagName, _loadCancellation.Token).ConfigureAwait(true);
                PopulateRelease(release);
                SetStatus(string.Format(
                    UiLocalization.Text("GitHubReleaseDetailsForm.Status.Loaded.Template", "Loaded {0} release entries."),
                    _entryListBox.Items.Count), false);
                FocusEntries();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubReleaseDetailsForm.Status.Canceled", "Release loading canceled."), false);
            }
            catch (Exception ex)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, ex.ToString());
                SetStatus(UiLocalization.Text("GitHubReleaseDetailsForm.Status.Failed", "Failed to load release details."), true);
            }
            finally
            {
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
            }
        }

        private void PopulateRelease(GitHubReleaseDetailEntry release)
        {
            _entryListBox.BeginUpdate();
            try
            {
                _entryListBox.Items.Clear();
                if (release != null)
                {
                    _entryListBox.Items.Add(new ReleaseListItem(
                        UiLocalization.Text("GitHubReleaseDetailsForm.Entry.Notes", "Release notes"),
                        BuildReleaseNotesContent(release)));

                    foreach (var asset in release.Assets)
                    {
                        _entryListBox.Items.Add(new ReleaseListItem(
                            BuildAssetSummary(asset),
                            BuildAssetContent(asset)));
                    }
                }
            }
            finally
            {
                _entryListBox.EndUpdate();
            }

            if (_entryListBox.Items.Count > 0)
            {
                _entryListBox.SelectedIndex = 0;
            }
            else
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _contentTextBox,
                    UiLocalization.Text("GitHubReleaseDetailsForm.Empty", "No release details found."));
            }
        }

        private void UpdateSelectedEntryContent()
        {
            var item = _entryListBox.SelectedItem as ReleaseListItem;
            TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, item == null ? string.Empty : item.Content);
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text ?? string.Empty;
            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(_statusLabel.Text, true, false, true);
            }
        }

        private void FocusEntries()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                if (_entryListBox.Items.Count > 0 && _entryListBox.SelectedIndex < 0)
                {
                    _entryListBox.SelectedIndex = 0;
                }

                if (_entryListBox.CanFocus)
                {
                    _entryListBox.Focus();
                }
            });
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = string.Format(
                    UiLocalization.Text("GitHubReleaseDetailsForm.Heading.Template", "Release {0}"),
                    DisplayValue(_tagName)),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_refreshButton);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_entryListBox, 0, 1);
            root.Controls.Add(_contentTextBox, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
        }

        private static string BuildReleaseNotesContent(GitHubReleaseDetailEntry release)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Name.Template", "Name: {0}"), DisplayValue(release.Name)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Tag.Template", "Tag: {0}"), DisplayValue(release.TagName)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.State.Template", "State: {0}"), BuildReleaseState(release)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Author.Template", "Author: {0}"), DisplayValue(release.Author)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.PublishedAt.Template", "Published: {0}"), FormatDate(release.PublishedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.CreatedAt.Template", "Created: {0}"), FormatDate(release.CreatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Url.Template", "URL: {0}"), DisplayValue(release.Url)));
            builder.AppendLine();
            builder.AppendLine(DisplayValue(release.Body));
            return builder.ToString().TrimEnd();
        }

        private static string BuildAssetSummary(GitHubReleaseAssetEntry asset)
        {
            return string.Format(
                UiLocalization.Text("GitHubReleaseDetailsForm.Entry.Asset.Template", "Asset: {0}, {1}"),
                DisplayValue(asset == null ? null : asset.Name),
                FormatSize(asset == null ? null : asset.Size));
        }

        private static string BuildAssetContent(GitHubReleaseAssetEntry asset)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Name.Template", "Name: {0}"), DisplayValue(asset == null ? null : asset.Name)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Label.Template", "Label: {0}"), DisplayValue(asset == null ? null : asset.Label)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.ContentType.Template", "Content type: {0}"), DisplayValue(asset == null ? null : asset.ContentType)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Size.Template", "Size: {0}"), FormatSize(asset == null ? null : asset.Size)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.DownloadCount.Template", "Downloads: {0}"), asset == null ? 0 : asset.DownloadCount));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.State.Template", "State: {0}"), DisplayValue(asset == null ? null : asset.State)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.CreatedAt.Template", "Created: {0}"), FormatDate(asset == null ? null : asset.CreatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.UpdatedAt.Template", "Updated: {0}"), FormatDate(asset == null ? null : asset.UpdatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Details.Url.Template", "URL: {0}"), DisplayValue(asset == null ? null : asset.Url)));
            return builder.ToString().TrimEnd();
        }

        private static string BuildReleaseState(GitHubReleaseDetailEntry release)
        {
            if (release == null)
            {
                return string.Empty;
            }

            if (release.IsDraft)
            {
                return UiLocalization.Text("GitHubReleaseDetailsForm.Value.Draft", "Draft");
            }

            if (release.IsPrerelease)
            {
                return UiLocalization.Text("GitHubReleaseDetailsForm.Value.Prerelease", "Prerelease");
            }

            return UiLocalization.Text("GitHubReleaseDetailsForm.Value.Release", "Release");
        }

        private static string FormatDate(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : UiLocalization.Text("GitHubReleaseDetailsForm.Value.NotAvailable", "Not available");
        }

        private static string FormatSize(long? bytes)
        {
            if (!bytes.HasValue)
            {
                return UiLocalization.Text("GitHubReleaseDetailsForm.Value.NotAvailable", "Not available");
            }

            if (bytes.Value < 1024)
            {
                return string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Value.Bytes.Template", "{0} bytes"), bytes.Value);
            }

            var kb = bytes.Value / 1024D;
            if (kb < 1024D)
            {
                return string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Value.Kilobytes.Template", "{0:0.##} KB"), kb);
            }

            return string.Format(UiLocalization.Text("GitHubReleaseDetailsForm.Value.Megabytes.Template", "{0:0.##} MB"), kb / 1024D);
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("GitHubReleaseDetailsForm.Value.NotAvailable", "Not available")
                : value.Trim();
        }

        private sealed class ReleaseListItem
        {
            public ReleaseListItem(string display, string content)
            {
                Display = display ?? string.Empty;
                Content = content ?? string.Empty;
            }

            public string Display { get; private set; }

            public string Content { get; private set; }

            public override string ToString()
            {
                return Display;
            }
        }
    }
}
