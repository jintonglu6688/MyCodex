using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    internal sealed class AuthenticationQueryParametersControl : UserControl
    {
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, TabIndex = 0
        };
        private readonly Button _add;

        internal AuthenticationQueryParametersControl(IDictionary<string, object> values)
        {
            Height = 185;
            TabStop = true;
            _list.AccessibleName = AuthenticationCenterUiText.Name(UiLocalization.Text("AuthenticationAdvanced.Query", "&Query parameters"));
            _list.Columns.Add(UiLocalization.Text("AuthenticationAdvanced.Key", "Key"), 160);
            _list.Columns.Add(UiLocalization.Text("AuthenticationAdvanced.Value", "Value"), 240);
            UiTheme.ApplyListView(_list);
            if (values != null)
            {
                foreach (var pair in values) AddItem(pair.Key, pair.Value);
            }
            var edit = new MenuItem(UiLocalization.Text("AuthenticationModels.Edit", "&Edit"), (sender, args) => Edit(false));
            var delete = new MenuItem(UiLocalization.Text("AuthenticationModels.Delete", "&Delete"), (sender, args) => DeleteSelected());
            _list.ContextMenu = new ContextMenu(new[] { edit, delete });
            _list.ContextMenu.Popup += (sender, args) => edit.Enabled = delete.Enabled = Selected != null;
            _list.DoubleClick += (sender, args) => Edit(false);
            _list.KeyDown += (sender, args) =>
            {
                if (args.Modifiers != Keys.None) return;
                if (args.KeyCode == Keys.Enter || args.KeyCode == Keys.Insert || args.KeyCode == Keys.Delete)
                {
                    args.Handled = args.SuppressKeyPress = true;
                    if (args.KeyCode == Keys.Delete) DeleteSelected();
                    else Edit(args.KeyCode == Keys.Insert);
                }
            };
            _add = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationQuery.Add", "Add parameter (&Z)"), 0);
            _add.Click += (sender, args) => Edit(true);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 1 };
            actions.Controls.Add(_add);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, TabStop = false };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_list, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout);
        }

        private ListViewItem Selected => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0];

        internal Dictionary<string, object> Read()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (ListViewItem item in _list.Items) result.Add(item.Text, item.Tag);
            return result;
        }

        private ListViewItem AddItem(string name, object value)
        {
            var item = new ListViewItem(new[] { name, Convert.ToString(value, CultureInfo.InvariantCulture) }) { Tag = value };
            _list.Items.Add(item);
            return item;
        }

        private void Edit(bool add)
        {
            var selected = add ? null : Selected;
            if (!add && selected == null) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ListViewItem item in _list.Items)
                if (item != selected) names.Add(item.Text);
            using (var dialog = new AuthenticationQueryParameterEditForm(selected?.Text, selected?.SubItems[1].Text, names))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (selected == null) selected = AddItem(dialog.ParameterName, dialog.ParameterValue);
                    else
                    {
                        selected.Text = dialog.ParameterName;
                        selected.SubItems[1].Text = dialog.ParameterValue;
                        selected.Tag = dialog.ParameterValue;
                    }
                }
            }
            if (selected != null) SelectItem(selected);
            else _add.Focus();
        }

        private void DeleteSelected()
        {
            var selected = Selected;
            if (selected == null) return;
            var index = selected.Index;
            _list.Items.Remove(selected);
            if (_list.Items.Count > 0) SelectItem(_list.Items[Math.Min(index, _list.Items.Count - 1)]);
            else _add.Focus();
        }

        private void SelectItem(ListViewItem item)
        {
            foreach (ListViewItem existing in _list.SelectedItems) existing.Selected = false;
            item.Selected = item.Focused = true;
            item.EnsureVisible();
            _list.Focus();
        }
    }
}
