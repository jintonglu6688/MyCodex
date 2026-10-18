using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class InsertPluginForm : Form
    {
        private readonly ListView _pluginListView;
        private readonly Label _statusLabel;
        private readonly Button _insertButton;
        private readonly Button _closeButton;

        public InsertPluginForm(IReadOnlyCollection<AppServerPluginSummary> plugins)
        {
            Text = UiLocalization.Text("InsertPluginForm.Title", "Insert Plugin");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(820, 420);
            UiTheme.ApplyForm(this);

            _pluginListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("InsertPluginForm.Accessible.Plugins", "Plugins"),
                TabIndex = 0
            };
            UiTheme.ApplyListView(_pluginListView);
            _pluginListView.Columns.Add(UiLocalization.Text("InsertPluginForm.Column.Plugin", "Plugin"), 260);
            _pluginListView.Columns.Add(UiLocalization.Text("InsertPluginForm.Column.Enabled", "Enabled"), 90);
            _pluginListView.Columns.Add(UiLocalization.Text("InsertPluginForm.Column.Marketplace", "Marketplace"), 180);
            _pluginListView.Columns.Add(UiLocalization.Text("InsertPluginForm.Column.Summary", "Summary"), 260);
            _pluginListView.ItemActivate += PluginListView_ItemActivate;
            _pluginListView.SelectedIndexChanged += PluginListView_SelectedIndexChanged;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };

            _insertButton = new Button
            {
                Text = UiLocalization.Text("InsertPluginForm.Button.Insert", "Insert(&I)"),
                AccessibleName = UiLocalization.Text("InsertPluginForm.Accessible.Insert", "Insert"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_insertButton, UiGlyph.Ok);
            _insertButton.Click += InsertButton_Click;

            _closeButton = new Button
            {
                Text = UiLocalization.Text("InsertPluginForm.Button.Close", "Close(&C)"),
                AccessibleName = UiLocalization.Text("InsertPluginForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 2
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += CloseButton_Click;

            BuildLayout();
            BindPlugins(plugins);

            AcceptButton = _insertButton;
            CancelButton = _closeButton;
            Shown += InsertPluginForm_Shown;
        }

        public AppServerPluginSummary SelectedPlugin { get; private set; }

        private void BuildLayout()
        {
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_insertButton);

            var footerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footerLayout.Controls.Add(_statusLabel, 0, 0);
            footerLayout.Controls.Add(buttonPanel, 1, 0);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_pluginListView, 0, 0);
            root.Controls.Add(footerLayout, 0, 1);

            Controls.Add(root);
        }

        private void BindPlugins(IReadOnlyCollection<AppServerPluginSummary> plugins)
        {
            _pluginListView.BeginUpdate();
            try
            {
                _pluginListView.Items.Clear();
                foreach (var plugin in (plugins ?? Array.Empty<AppServerPluginSummary>())
                    .Where(IsInsertablePlugin)
                    .OrderByDescending(item => item.Enabled)
                    .ThenBy(item => BuildDisplayName(item), StringComparer.CurrentCultureIgnoreCase))
                {
                    var item = new ListViewItem(BuildDisplayName(plugin));
                    item.SubItems.Add(plugin.Enabled
                        ? UiLocalization.Text("InsertPluginForm.Value.Yes", "Yes")
                        : UiLocalization.Text("InsertPluginForm.Value.No", "No"));
                    item.SubItems.Add(string.IsNullOrWhiteSpace(plugin.MarketplaceName)
                        ? UiLocalization.Text("InsertPluginForm.Value.UnknownMarketplace", "Unknown")
                        : plugin.MarketplaceName.Trim());
                    item.SubItems.Add(BuildSummary(plugin));
                    item.Tag = plugin;
                    _pluginListView.Items.Add(item);
                }
            }
            finally
            {
                _pluginListView.EndUpdate();
            }

            _statusLabel.Text = string.Format(
                UiLocalization.Text("InsertPluginForm.Status.TotalPlugins", "Total {0} plugins"),
                _pluginListView.Items.Count);
            _insertButton.Enabled = _pluginListView.Items.Count > 0;
        }

        private void InsertPluginForm_Shown(object sender, EventArgs e)
        {
            if (_pluginListView.Items.Count > 0)
            {
                _pluginListView.Items[0].Selected = true;
                _pluginListView.Items[0].Focused = true;
                _pluginListView.EnsureVisible(0);
                _pluginListView.Focus();
            }
            else
            {
                _closeButton.Focus();
            }
        }

        private void PluginListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            _insertButton.Enabled = GetSelectedPlugin() != null;
        }

        private void PluginListView_ItemActivate(object sender, EventArgs e)
        {
            ConfirmSelection();
        }

        private void InsertButton_Click(object sender, EventArgs e)
        {
            ConfirmSelection();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ConfirmSelection()
        {
            var plugin = GetSelectedPlugin();
            if (plugin == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("InsertPluginForm.Message.SelectPlugin", "Please select a plugin to insert."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _pluginListView.Focus();
                return;
            }

            SelectedPlugin = plugin;
            DialogResult = DialogResult.OK;
            Close();
        }

        private AppServerPluginSummary GetSelectedPlugin()
        {
            if (_pluginListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _pluginListView.SelectedItems[0].Tag as AppServerPluginSummary;
        }

        private static bool IsInsertablePlugin(AppServerPluginSummary plugin)
        {
            return plugin != null &&
                   plugin.Installed &&
                   !string.IsNullOrWhiteSpace(plugin.SourcePath);
        }

        private static string BuildDisplayName(AppServerPluginSummary plugin)
        {
            return ComposeAttachmentState.GetPluginInsertionDisplayName(plugin);
        }

        private static string BuildSummary(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return string.Empty;
            }

            var summary = !string.IsNullOrWhiteSpace(plugin.ShortDescription)
                ? plugin.ShortDescription
                : plugin.LongDescription;
            return (summary ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
