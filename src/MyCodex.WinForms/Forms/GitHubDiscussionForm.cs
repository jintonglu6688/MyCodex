using System;
using System.Collections.Generic;
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
    internal sealed class GitHubDiscussionForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _loadCancellation != null;

        private readonly IGitHubCliService _gitHubCliService;
        private readonly GitHubRepositoryEntry _repository;
        private readonly int _number;
        private readonly bool _pullRequest;
        private readonly IScreenReaderService _screenReaderService;
        private readonly ListBox _entryListBox;
        private readonly TextBox _contentTextBox;
        private readonly Label _statusLabel;
        private readonly Button _commentButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;
        private CancellationTokenSource _loadCancellation;

        public GitHubDiscussionForm(
            IGitHubCliService gitHubCliService,
            GitHubRepositoryEntry repository,
            int number,
            bool pullRequest,
            IScreenReaderService screenReaderService)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _number = number;
            _pullRequest = pullRequest;
            _screenReaderService = screenReaderService;

            Text = string.Format(
                UiLocalization.Text("GitHubDiscussionForm.Title.Template", "{0} #{1} Discussion"),
                pullRequest
                    ? UiLocalization.Text("GitHubDiscussionForm.Kind.PullRequest", "Pull request")
                    : UiLocalization.Text("GitHubDiscussionForm.Kind.Issue", "Issue"),
                number);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 560);
            Size = new Size(1020, 680);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _entryListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubDiscussionForm.Accessible.Entries", "Discussion entries"),
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_entryListBox);

            _contentTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubDiscussionForm.Accessible.Content", "Content"),
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
                UiLocalization.Text("GitHubDiscussionForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("GitHubDiscussionForm.Accessible.Refresh", "Refresh"),
                3,
                112);
            _commentButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubDiscussionForm.Button.Comment", "Comment(&C)"),
                UiLocalization.Text("GitHubDiscussionForm.Accessible.Comment", "Comment"),
                2,
                112);
            _closeButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubDiscussionForm.Button.Close", "Close(&L)"),
                UiLocalization.Text("GitHubDiscussionForm.Accessible.Close", "Close"),
                4,
                112);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_commentButton, UiGlyph.Command);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _entryListBox.SelectedIndexChanged += (sender, e) => UpdateSelectedEntryContent();
            _refreshButton.Click += async (sender, e) => await LoadDiscussionAsync();
            _commentButton.Click += async (sender, e) => await AddCommentAsync();
            _closeButton.Click += (sender, e) => Close();
            KeyDown += GitHubDiscussionForm_KeyDown;
            FormClosing += GitHubDiscussionForm_FormClosing;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadDiscussionAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Ready", "Discussion is not loaded."), false);
        }

        private async void GitHubDiscussionForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && _loadCancellation == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await LoadDiscussionAsync();
            }
        }

        private void GitHubDiscussionForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
            }
        }

        private async Task LoadDiscussionAsync()
        {
            if (_loadCancellation != null)
            {
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Loading", "Loading discussion..."), false);
            try
            {
                var discussion = _pullRequest
                    ? await _gitHubCliService.GetPullRequestDiscussionAsync(_repository, _number, _loadCancellation.Token).ConfigureAwait(true)
                    : await _gitHubCliService.GetIssueDiscussionAsync(_repository, _number, _loadCancellation.Token).ConfigureAwait(true);
                PopulateDiscussion(discussion);
                SetStatus(string.Format(
                    UiLocalization.Text("GitHubDiscussionForm.Status.Loaded.Template", "Loaded {0} discussion entries."),
                    _entryListBox.Items.Count), false);
                FocusEntries();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Canceled", "Discussion loading canceled."), false);
            }
            catch (Exception ex)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, ex.ToString());
                SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Failed", "Failed to load discussion."), true);
            }
            finally
            {
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
            }
        }

        private void PopulateDiscussion(GitHubDiscussionEntry discussion)
        {
            _entryListBox.BeginUpdate();
            try
            {
                _entryListBox.Items.Clear();
                if (discussion != null)
                {
                    _entryListBox.Items.Add(new DiscussionListItem(
                        UiLocalization.Text("GitHubDiscussionForm.Entry.Main", "Original post"),
                        BuildMainDiscussionContent(discussion)));

                    foreach (var comment in discussion.Comments ?? new List<GitHubDiscussionCommentEntry>())
                    {
                        _entryListBox.Items.Add(new DiscussionListItem(
                            BuildCommentSummary(comment),
                            BuildCommentContent(comment)));
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
                    UiLocalization.Text("GitHubDiscussionForm.Empty", "No discussion entries found."));
            }
        }

        private void UpdateSelectedEntryContent()
        {
            var item = _entryListBox.SelectedItem as DiscussionListItem;
            TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, item == null ? string.Empty : item.Content);
        }

        private async Task AddCommentAsync()
        {
            if (_loadCancellation != null)
            {
                FocusEntries();
                return;
            }

            using (var form = new GitHubCommentForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    FocusEntries();
                    return;
                }

                _loadCancellation = new CancellationTokenSource();
                SetBusy(true);
                SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Commenting", "Adding comment..."), true);
                try
                {
                    if (_pullRequest)
                    {
                        await _gitHubCliService.AddPullRequestCommentAsync(_repository, _number, form.Body, _loadCancellation.Token).ConfigureAwait(true);
                    }
                    else
                    {
                        await _gitHubCliService.AddIssueCommentAsync(_repository, _number, form.Body, _loadCancellation.Token).ConfigureAwait(true);
                    }

                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                    SetBusy(false);
                    SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Commented", "Comment added."), true);
                    await LoadDiscussionAsync().ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.Canceled", "Discussion loading canceled."), false);
                }
                catch (Exception ex)
                {
                    TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, ex.ToString());
                    SetStatus(UiLocalization.Text("GitHubDiscussionForm.Status.CommentFailed", "Failed to add comment."), true);
                }
                finally
                {
                    if (_loadCancellation != null)
                    {
                        _loadCancellation.Dispose();
                        _loadCancellation = null;
                    }

                    SetBusy(false);
                    FocusEntries();
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
            _commentButton.Enabled = !busy;
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
                    UiLocalization.Text("GitHubDiscussionForm.Heading.Template", "{0} #{1}"),
                    _pullRequest
                        ? UiLocalization.Text("GitHubDiscussionForm.Kind.PullRequest", "Pull request")
                        : UiLocalization.Text("GitHubDiscussionForm.Kind.Issue", "Issue"),
                    _number),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_commentButton);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_entryListBox, 0, 1);
            root.Controls.Add(_contentTextBox, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
        }

        private static string BuildMainDiscussionContent(GitHubDiscussionEntry discussion)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Number.Template", "Number: {0}"), discussion.Number));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Title.Template", "Title: {0}"), DisplayValue(discussion.Title)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.State.Template", "State: {0}"), DisplayValue(discussion.State)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Author.Template", "Author: {0}"), DisplayValue(discussion.Author)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.CreatedAt.Template", "Created: {0}"), FormatDate(discussion.CreatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.UpdatedAt.Template", "Updated: {0}"), FormatDate(discussion.UpdatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Url.Template", "URL: {0}"), DisplayValue(discussion.Url)));
            builder.AppendLine();
            builder.AppendLine(DisplayValue(discussion.Body));
            return builder.ToString().TrimEnd();
        }

        private static string BuildCommentSummary(GitHubDiscussionCommentEntry comment)
        {
            return string.Format(
                UiLocalization.Text("GitHubDiscussionForm.Entry.Comment.Template", "Comment by {0}, {1}"),
                DisplayValue(comment == null ? null : comment.Author),
                FormatDate(comment == null ? null : comment.CreatedAt));
        }

        private static string BuildCommentContent(GitHubDiscussionCommentEntry comment)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Author.Template", "Author: {0}"), DisplayValue(comment == null ? null : comment.Author)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.CreatedAt.Template", "Created: {0}"), FormatDate(comment == null ? null : comment.CreatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.UpdatedAt.Template", "Updated: {0}"), FormatDate(comment == null ? null : comment.UpdatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubDiscussionForm.Details.Url.Template", "URL: {0}"), DisplayValue(comment == null ? null : comment.Url)));
            builder.AppendLine();
            builder.AppendLine(DisplayValue(comment == null ? null : comment.Body));
            return builder.ToString().TrimEnd();
        }

        private static string FormatDate(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : UiLocalization.Text("GitHubDiscussionForm.Value.NotAvailable", "Not available");
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("GitHubDiscussionForm.Value.NotAvailable", "Not available")
                : value.Trim();
        }

        private sealed class DiscussionListItem
        {
            public DiscussionListItem(string display, string content)
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
