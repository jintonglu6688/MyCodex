using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Chooses the default provider and the providers whose models join the Codex model list.
    public sealed class AuthenticationAggregationForm : Form
    {
        private readonly AuthenticationStackState _state;
        private readonly IReadOnlyDictionary<string, string> _names;
        private readonly ComboBox _defaultComboBox;
        private readonly CheckedListBox _membersList;
        private readonly Label _summaryLabel;
        private readonly Button _okButton;
        private readonly HashSet<string> _checked;
        private bool _filling;

        public AuthenticationAggregationForm(AuthenticationStackState state, IReadOnlyDictionary<string, string> names, string preferredDefaultId)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _names = names ?? new Dictionary<string, string>();
            _checked = new HashSet<string>(state.Members.Select(member => member.ProviderId), StringComparer.Ordinal);
            Text = AccessibleName = UiLocalization.Text("Aggregation.Dialog.Title", "Aggregate models");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 420);
            Size = new Size(620, 500);
            MinimizeBox = false;
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);
            var intro = new Label
            {
                Text = UiLocalization.Text("Aggregation.Dialog.Intro", "The models of the providers you add appear in the Codex model list, marked with the provider name. A request for one of them goes to that provider; other requests go to the default. There is no failover."),
                AutoSize = true, Dock = DockStyle.Fill, TabStop = false, MaximumSize = new Size(580, 0)
            };
            UiTheme.ApplyLabel(intro, true);
            var defaultCaption = UiLocalization.Text("Aggregation.Dialog.Default", "&Default provider:");
            var defaultLabel = new Label { Text = defaultCaption, AutoSize = true, TabStop = false, TabIndex = 0, UseMnemonic = true, Dock = DockStyle.Fill };
            UiTheme.ApplyLabel(defaultLabel, false);
            _defaultComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, TabIndex = 1,
                AccessibleName = AuthenticationCenterUiText.Name(defaultCaption).TrimEnd(':', '：')
            };
            UiTheme.ApplyComboBox(_defaultComboBox);
            var membersCaption = UiLocalization.Text("Aggregation.Dialog.Members", "Other &providers to add:");
            var membersLabel = new Label { Text = membersCaption, AutoSize = true, TabStop = false, TabIndex = 2, UseMnemonic = true, Dock = DockStyle.Fill };
            UiTheme.ApplyLabel(membersLabel, false);
            _membersList = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true, TabIndex = 3, IntegralHeight = false,
                AccessibleName = AuthenticationCenterUiText.Name(membersCaption).TrimEnd(':', '：')
            };
            UiTheme.ApplyListBox(_membersList);
            _summaryLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, TabStop = false };
            UiTheme.ApplyLabel(_summaryLabel, true);
            _okButton = AuthenticationCenterUiText.Button(UiLocalization.Text("Aggregation.Dialog.Start", "&Start aggregating"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("Common.Cancel", "Cancel(&A)"), 1);
            _okButton.DialogResult = DialogResult.OK;
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = _okButton;
            CancelButton = cancel;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabIndex = 4, TabStop = false };
            actions.Controls.AddRange(new Control[] { _okButton, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(intro, 0, 0);
            root.Controls.Add(defaultLabel, 0, 1);
            root.Controls.Add(_defaultComboBox, 0, 2);
            root.Controls.Add(membersLabel, 0, 3);
            root.Controls.Add(_membersList, 0, 4);
            root.Controls.Add(_summaryLabel, 0, 5);
            root.Controls.Add(actions, 0, 6);
            Controls.Add(root);
            foreach (var id in AuthenticationStackPlanner.DefaultCandidates(state))
                _defaultComboBox.Items.Add(new Choice(id, NameOf(id)));
            var preferred = _defaultComboBox.Items.Cast<Choice>().FirstOrDefault(choice => choice.Id == preferredDefaultId)
                ?? _defaultComboBox.Items.Cast<Choice>().FirstOrDefault(choice => choice.Id == state.DefaultProviderId);
            if (preferred != null) _defaultComboBox.SelectedItem = preferred;
            else if (_defaultComboBox.Items.Count > 0) _defaultComboBox.SelectedIndex = 0;
            _defaultComboBox.SelectedIndexChanged += (sender, args) => FillMembers();
            _membersList.ItemCheck += (sender, args) =>
            {
                var id = ((Choice)_membersList.Items[args.Index]).Id;
                if (args.NewValue == CheckState.Checked) _checked.Add(id);
                else _checked.Remove(id);
                // The checked count changes only after this event; filling the list updates the summary itself.
                if (!_filling && IsHandleCreated) BeginInvoke((Action)UpdateSummary);
            };
            FillMembers();
            Shown += (sender, args) => _defaultComboBox.Focus();
        }

        public string DefaultProviderId => (_defaultComboBox.SelectedItem as Choice)?.Id;

        public IReadOnlyList<string> MemberIds =>
            _membersList.CheckedItems.Cast<Choice>().Select(choice => choice.Id).ToList();

        private void FillMembers()
        {
            _filling = true;
            _membersList.BeginUpdate();
            try
            {
                _membersList.Items.Clear();
                foreach (var id in AuthenticationStackPlanner.MemberCandidates(_state, DefaultProviderId))
                    _membersList.Items.Add(new Choice(id, NameOf(id)), _checked.Contains(id));
            }
            finally
            {
                _membersList.EndUpdate();
                _filling = false;
            }
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (IsDisposed) return;
            _okButton.Enabled = DefaultProviderId != null;
            _summaryLabel.Text = DefaultProviderId == null
                ? UiLocalization.Text("Aggregation.Dialog.NoDefault", "No saved provider can be the default. A ChatGPT account needs unified session history first.")
                : string.Format(UiLocalization.Text("Aggregation.Dialog.Summary", "Default: {0}. Other providers added: {1}."), NameOf(DefaultProviderId), _membersList.CheckedItems.Count);
        }

        private string NameOf(string id)
        {
            return id != null && _names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : id;
        }

        private sealed class Choice
        {
            internal Choice(string id, string name)
            {
                Id = id;
                Name = name;
            }

            internal string Id { get; }
            internal string Name { get; }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
