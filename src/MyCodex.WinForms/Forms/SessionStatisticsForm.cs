using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class SessionStatisticsForm : Form
    {
        private readonly ChatSession _session;
        private readonly SessionStatisticsDisplayContext _displayContext;
        private readonly ListView _detailsListView;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;

        public SessionStatisticsForm(ChatSession session)
            : this(session, null)
        {
        }

        internal SessionStatisticsForm(ChatSession session, SessionStatisticsDisplayContext displayContext)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _displayContext = displayContext;

            Text = UiLocalization.Text("SessionStatisticsForm.Title", "Conversation Statistics");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 620);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _detailsListView = CreateDetailsListView();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            _refreshButton = new Button
            {
                Text = UiLocalization.Text("SessionStatisticsForm.Button.Refresh", "&Refresh"),
                AccessibleName = UiLocalization.Text("SessionStatisticsForm.Accessible.Refresh", "Refresh"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            _closeButton = new Button
            {
                Text = UiLocalization.Text("SessionStatisticsForm.Button.Close", "&Close"),
                AccessibleName = UiLocalization.Text("SessionStatisticsForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                UseMnemonic = true,
                TabIndex = 2
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            _refreshButton.Click += OnRefreshButtonClick;
            CancelButton = _closeButton;

            BuildLayout();
            RefreshDetailsList(false);
            Shown += OnShown;
        }

        private void BuildLayout()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 208,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_closeButton);
            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_detailsListView);
            mainPanel.Controls.Add(footerPanel);

            Controls.Add(mainPanel);
        }

        private static ListView CreateDetailsListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("SessionStatisticsForm.Accessible.Details", "Statistics details"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                TabIndex = 0
            };
            listView.View = View.List;
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private void OnShown(object sender, EventArgs e)
        {
            this.BeginInvokeSafe(FocusDetailsList);
        }

        private void OnRefreshButtonClick(object sender, EventArgs e)
        {
            RefreshDetailsList(true);
        }

        private void RefreshDetailsList(bool refreshed)
        {
            var selectedIndex = _detailsListView.SelectedIndices.Count > 0
                ? _detailsListView.SelectedIndices[0]
                : -1;

            _detailsListView.BeginUpdate();
            _detailsListView.Items.Clear();
            foreach (var line in SessionStatisticsDetailBuilder.BuildDetailLines(_session, _displayContext))
            {
                _detailsListView.Items.Add(new ListViewItem(line));
            }

            if (_detailsListView.Items.Count > 0)
            {
                var nextIndex = selectedIndex >= 0 && selectedIndex < _detailsListView.Items.Count
                    ? selectedIndex
                    : 0;
                _detailsListView.Items[nextIndex].Selected = true;
                _detailsListView.Items[nextIndex].Focused = true;
            }

            _detailsListView.EndUpdate();
            _statusLabel.Text = refreshed
                ? UiLocalization.Text("SessionStatisticsForm.Status.Refreshed", "Statistics refreshed.")
                : UiLocalization.Text("SessionStatisticsForm.Status.Ready", "Conversation-only statistics are shown here.");
        }

        private void FocusDetailsList()
        {
            if (_detailsListView.Items.Count > 0 && _detailsListView.SelectedIndices.Count == 0)
            {
                _detailsListView.Items[0].Selected = true;
                _detailsListView.Items[0].Focused = true;
            }

            ActiveControl = _detailsListView;
            _detailsListView.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                RefreshDetailsList(true);
                return true;
            }

            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
