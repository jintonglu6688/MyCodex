using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    public sealed class AuthenticationModelsControl : UserControl
    {
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, TabIndex = 0 };
        private readonly Button _fetch;
        private readonly List<AuthenticationCatalogModel> _models = new List<AuthenticationCatalogModel>();
        private string _defaultModel;

        public AuthenticationModelsControl(IScreenReaderService reader = null)
        {
            TabStop = true;
            _list.AccessibleName = UiLocalization.Text("AuthenticationModels.List", "Models");
            _list.Columns.Add(UiLocalization.Text("AuthenticationModels.Name", "Name"), 180);
            _list.Columns.Add(UiLocalization.Text("AuthenticationModels.Id", "Model ID"), 200);
            _list.Columns.Add(UiLocalization.Text("AuthenticationModels.Default", "Default"), 100);
            UiTheme.ApplyListView(_list);
            var edit = new MenuItem(UiLocalization.Text("AuthenticationModels.Edit", "&Edit"), (sender, args) => Edit(false));
            var delete = new MenuItem(UiLocalization.Text("AuthenticationModels.Delete", "&Delete"), (sender, args) => DeleteSelected());
            var setDefault = new MenuItem(UiLocalization.Text("AuthenticationModels.SetDefault", "Set as de&fault"), (sender, args) =>
            {
                if (Selected != null) { DefaultModel = Selected.Model; Changed(); }
            });
            _list.ContextMenu = new ContextMenu(new[] { edit, delete, setDefault });
            _list.ContextMenu.Popup += (sender, args) => edit.Enabled = delete.Enabled = setDefault.Enabled = Selected != null;
            _list.PreviewKeyDown += (sender, args) =>
            {
                if (args.KeyData == Keys.Enter) { args.IsInputKey = true; }
            };
            _list.KeyDown += (sender, args) =>
            {
                if (args.Modifiers != Keys.None) { return; }
                if (args.KeyCode == Keys.Enter || args.KeyCode == Keys.Delete)
                {
                    args.Handled = args.SuppressKeyPress = true;
                    if (args.KeyCode == Keys.Delete) { DeleteSelected(); }
                    else { Edit(false); }
                }
            };
            _list.DoubleClick += (sender, args) => Edit(false);
            _fetch = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationModels.Fetch", "Get models (&J)"), 0);
            var add = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationModels.Add", "&Add model"), 1);
            _fetch.Click += (sender, args) => FetchRequested?.Invoke(this, EventArgs.Empty);
            add.Click += (sender, args) => Edit(true);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 1 };
            actions.Controls.AddRange(new Control[] { _fetch, add });
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, TabStop = false };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_list, 0, 0);
            layout.Controls.Add(actions, 0, 1);
            Controls.Add(layout);
        }

        public event EventHandler FetchRequested;
        public event EventHandler ModelsChanged;
        // Raised with the model ID after the user adds a model manually.
        public event EventHandler<string> ModelAdded;
        public bool Fetching { get { return !_fetch.Enabled; } set { _fetch.Enabled = !value; } }
        public IList<AuthenticationCatalogModel> Models
        {
            get { return _models.Select(Clone).ToList(); }
            set { _models.Clear(); _models.AddRange((value ?? new List<AuthenticationCatalogModel>()).Select(Clone)); RefreshList(); }
        }
        public string DefaultModel { get { return _defaultModel; } set { _defaultModel = value; RefreshList(); } }
        private AuthenticationCatalogModel Selected { get { return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as AuthenticationCatalogModel; } }

        public void MergeFetched(IEnumerable<AuthenticationCatalogModel> fetched)
        {
            var ids = new HashSet<string>(_models.Select(model => model.Model), StringComparer.Ordinal);
            foreach (var model in fetched ?? Enumerable.Empty<AuthenticationCatalogModel>())
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Model) || !ids.Add(model.Model)) { continue; }
                var copy = Clone(model);
                copy.DisplayName = copy.Model;
                _models.Add(copy);
            }
            if (string.IsNullOrEmpty(_defaultModel) && _models.Count > 0) { _defaultModel = _models[0].Model; }
            Changed();
        }

        // Lets the caller complete blank fields in place; returns the number of changed models.
        public int FillMissing(Func<AuthenticationCatalogModel, bool> fill)
        {
            if (fill == null) { return 0; }
            var count = 0;
            foreach (var model in _models)
            {
                if (fill(model)) { count++; }
            }
            if (count > 0) { Changed(); }
            return count;
        }

        private void Edit(bool add)
        {
            var selected = add ? null : Selected;
            if (!add && selected == null) { return; }
            using (var form = new AuthenticationModelEditForm(selected))
            {
                if (form.ShowDialog(this) != DialogResult.OK) { return; }
                if (_models.Any(model => !ReferenceEquals(model, selected) && string.Equals(model.Model, form.Model.Model, StringComparison.Ordinal)))
                {
                    MessageBox.Show(this, UiLocalization.Text("AuthenticationModels.Duplicate", "A model with this ID already exists."), _list.AccessibleName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (selected == null) { AddModel(form.Model); }
                else
                {
                    if (string.Equals(_defaultModel, selected.Model, StringComparison.Ordinal)) { _defaultModel = form.Model.Model; }
                    _models[_models.IndexOf(selected)] = Clone(form.Model);
                    Changed();
                    SelectModel(form.Model.Model);
                }
                _list.Focus();
            }
        }

        // The caller has rejected duplicate IDs.
        internal void AddModel(AuthenticationCatalogModel model)
        {
            _models.Add(Clone(model));
            Changed();
            SelectModel(model.Model);
            ModelAdded?.Invoke(this, model.Model);
        }

        internal void DeleteSelected()
        {
            var selected = Selected;
            if (selected == null) { return; }
            var index = _models.IndexOf(selected);
            _models.Remove(selected);
            if (string.Equals(_defaultModel, selected.Model, StringComparison.Ordinal)) { _defaultModel = null; }
            Changed();
            if (_models.Count > 0) { SelectModel(_models[Math.Min(index, _models.Count - 1)].Model); }
            _list.Focus();
        }

        private void Changed() { RefreshList(); ModelsChanged?.Invoke(this, EventArgs.Empty); }
        private void RefreshList()
        {
            var selected = Selected?.Model;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var model in _models)
                {
                    var item = new ListViewItem(model.DisplayName ?? model.Model) { Tag = model };
                    item.SubItems.Add(model.Model);
                    item.SubItems.Add(string.Equals(model.Model, _defaultModel, StringComparison.Ordinal) ? UiLocalization.Text("AuthenticationModels.IsDefault", "Default") : string.Empty);
                    _list.Items.Add(item);
                }
                SelectModel(selected);
            }
            finally { _list.EndUpdate(); }
        }
        private void SelectModel(string id)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (string.Equals((item.Tag as AuthenticationCatalogModel)?.Model, id, StringComparison.Ordinal)) { item.Selected = item.Focused = true; item.EnsureVisible(); break; }
            }
        }
        private static AuthenticationCatalogModel Clone(AuthenticationCatalogModel model)
        {
            return new AuthenticationCatalogModel
            {
                Model = model.Model, DisplayName = model.DisplayName, ContextWindow = model.ContextWindow,
                ReasoningLevels = model.ReasoningLevels == null ? null : new List<string>(model.ReasoningLevels),
                DefaultReasoningLevel = model.DefaultReasoningLevel,
                InputModalities = model.InputModalities == null ? null : new List<string>(model.InputModalities),
                SupportsParallelToolCalls = model.SupportsParallelToolCalls, BaseInstructions = model.BaseInstructions,
                Metadata = CloneMetadata(model.Metadata)
            };
        }
        internal static Dictionary<string, object> CloneMetadata(Dictionary<string, object> metadata)
        {
            if (metadata == null) { return null; }
            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(metadata));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _list.ContextMenu?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
