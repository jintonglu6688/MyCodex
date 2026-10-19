using System;
using System.Collections.Generic;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    // Keyboard-operable name/value list: Enter or double-click edits, Insert adds, Delete removes.
    // Typed lists also keep a JSON value type for each entry.
    internal sealed class AuthenticationKeyValueListControl : UserControl
    {
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, TabIndex = 0
        };
        private readonly Button _add;
        private readonly AuthenticationKeyValueTexts _texts;

        internal AuthenticationKeyValueListControl(IDictionary<string, object> values, AuthenticationKeyValueTexts texts)
        {
            _texts = texts ?? throw new ArgumentNullException(nameof(texts));
            Height = 185;
            TabStop = true;
            _list.AccessibleName = AuthenticationCenterUiText.Name(texts.List);
            _list.Columns.Add(UiLocalization.Text("AuthenticationAdvanced.Key", "Key"), 160);
            _list.Columns.Add(UiLocalization.Text("AuthenticationAdvanced.Value", "Value"), texts.Typed ? 180 : 240);
            if (texts.Typed) _list.Columns.Add(UiLocalization.Text("AuthenticationAdvanced.Type", "Type"), 90);
            UiTheme.ApplyListView(_list);
            if (values != null)
            {
                foreach (var pair in values) AddItem(pair.Key, pair.Value, texts.Typed ? AuthenticationKeyValueEditForm.TypeOf(pair.Value) : null);
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
            _add = AuthenticationCenterUiText.Button(texts.Add, 0);
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

        private ListViewItem AddItem(string name, object value, string type)
        {
            var item = new ListViewItem(name);
            item.SubItems.Add(string.Empty);
            if (_texts.Typed) item.SubItems.Add(string.Empty);
            SetItem(item, name, value, type);
            _list.Items.Add(item);
            return item;
        }

        private void SetItem(ListViewItem item, string name, object value, string type)
        {
            item.Text = name;
            item.Tag = value;
            item.SubItems[1].Text = AuthenticationKeyValueEditForm.Display(value, type);
            if (_texts.Typed) item.SubItems[2].Text = type;
        }

        private void Edit(bool add)
        {
            var selected = add ? null : Selected;
            if (!add && selected == null) return;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ListViewItem item in _list.Items)
                if (item != selected) names.Add(item.Text);
            var type = _texts.Typed ? selected?.SubItems[2].Text ?? AuthenticationKeyValueEditForm.StringType : null;
            using (var dialog = new AuthenticationKeyValueEditForm(_texts, selected?.Text, selected?.Tag, type, names))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (selected == null) selected = AddItem(dialog.ParameterName, dialog.ParameterValue, dialog.ValueType);
                    else SetItem(selected, dialog.ParameterName, dialog.ParameterValue, dialog.ValueType);
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

    internal sealed class AuthenticationKeyValueTexts
    {
        internal string List { get; set; }
        internal string Add { get; set; }
        internal string Title { get; set; }
        internal string Name { get; set; }
        internal string Value { get; set; }
        internal string InvalidName { get; set; }
        internal bool Typed { get; set; }

        internal static AuthenticationKeyValueTexts Query()
        {
            return new AuthenticationKeyValueTexts
            {
                List = UiLocalization.Text("AuthenticationAdvanced.Query", "&Query parameters"),
                Add = UiLocalization.Text("AuthenticationQuery.Add", "Add parameter (&Z)"),
                Title = UiLocalization.Text("AuthenticationQuery.Title", "Query parameter"),
                Name = UiLocalization.Text("AuthenticationQuery.Name", "Parameter &name"),
                Value = UiLocalization.Text("AuthenticationQuery.Value", "Parameter &value"),
                InvalidName = UiLocalization.Text("AuthenticationQuery.InvalidName", "Enter a nonempty, unique parameter name.")
            };
        }

        internal static AuthenticationKeyValueTexts Headers()
        {
            return TypedList(UiLocalization.Text("AuthenticationAdvanced.Headers", "Request headers(&V)"),
                UiLocalization.Text("AuthenticationAdvanced.AddHeader", "Add header (&G)"),
                UiLocalization.Text("AuthenticationAdvanced.HeaderTitle", "Request header"));
        }

        internal static AuthenticationKeyValueTexts Body()
        {
            return TypedList(UiLocalization.Text("AuthenticationAdvanced.Body", "Request body overrides(&W)"),
                UiLocalization.Text("AuthenticationAdvanced.AddBody", "Add override (&5)"),
                UiLocalization.Text("AuthenticationAdvanced.BodyTitle", "Request body override"));
        }

        private static AuthenticationKeyValueTexts TypedList(string list, string add, string title)
        {
            return new AuthenticationKeyValueTexts
            {
                List = list, Add = add, Title = title, Typed = true,
                Name = UiLocalization.Text("AuthenticationKeyValue.Name", "&Name"),
                Value = UiLocalization.Text("AuthenticationKeyValue.Value", "&Value"),
                InvalidName = UiLocalization.Text("AuthenticationKeyValue.InvalidName", "Enter a nonempty, unique name.")
            };
        }
    }
}
