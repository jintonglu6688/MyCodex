using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationProviderEditForm : Form
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly AuthenticationProvider _original;
        private readonly AuthenticationProviderPreset _template;
        private readonly string _providerId;
        private readonly bool _chatGpt;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ThemedTextBox _nameTextBox;
        private readonly ComboBox _kindComboBox;
        private readonly ThemedTextBox _urlTextBox;
        private readonly ThemedTextBox _keyTextBox;
        private readonly CheckBox _clearKeyCheckBox;
        private readonly Button _saveButton;
        private readonly ThemedTextBox _statusLabel;
        private readonly Panel _content;
        private readonly TableLayoutPanel _sections;
        private readonly TableLayoutPanel _fields;
        private readonly AuthenticationModelsControl _models;
        private AuthenticationAdvancedControl _advanced;
        private AuthenticationContextControl _context;
        private readonly Button _configButton;
        private readonly bool _supportsContextSettings;
        private readonly bool _supportsFullConfigEditor;
        private AuthenticationConfigDocument _configDraft;
        // Full config editor draft: global edits are written on save by three-way merge.
        private string _editorBase;
        private string _editorText;
        private List<AuthenticationInactiveSetting> _editorInactive;
        private bool _editingConfig;
        private readonly AuthenticationLoginControl _login;
        private bool _saving;
        private bool _fetching;
        private bool _closing;
        private bool _disposed;
        private readonly Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> _save;
        private readonly AuthenticationModelMetadataService _modelMetadata;
        public AuthenticationSaveResult SaveResult { get; private set; }
        public bool SaveOutcomeUnknown { get; private set; }

        public AuthenticationProviderEditForm(IAuthenticationCenterClient client, AuthenticationProvider provider = null, IScreenReaderService screenReader = null,
            AuthenticationProviderPreset template = null, bool reauthenticate = false,
            Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> save = null, bool supportsContextSettings = false, bool supportsConfigEditing = false,
            AuthenticationModelMetadataService modelMetadata = null, bool supportsFullConfigEditor = false)
            : this(client, provider, screenReader, template, reauthenticate, Clipboard.SetText, ExternalUrlLauncher.Open, save, supportsContextSettings, supportsConfigEditing, modelMetadata, supportsFullConfigEditor)
        {
        }

        internal AuthenticationProviderEditForm(IAuthenticationCenterClient client, AuthenticationProvider provider, IScreenReaderService screenReader,
            AuthenticationProviderPreset template, bool reauthenticate, Action<string> copyCode, Action<string> openUrl,
            Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> save = null, bool supportsContextSettings = false, bool supportsConfigEditing = false,
            AuthenticationModelMetadataService modelMetadata = null, bool supportsFullConfigEditor = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _save = save;
            _modelMetadata = modelMetadata;
            _supportsContextSettings = supportsContextSettings;
            _supportsFullConfigEditor = supportsFullConfigEditor;
            ChooseConflictPolicy = ShowConflictDialog;
            _original = provider;
            _template = template;
            _providerId = provider?.Id ?? Guid.NewGuid().ToString("N");
            var kind = provider?.Kind ?? template?.Kind ?? "official_api";
            _chatGpt = kind == "chatgpt";
            Text = UiLocalization.Text("AuthenticationProviderEdit.Title", "Provider configuration");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 600);
            Size = new Size(900, 720);
            UiTheme.ApplyForm(this);
            _fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, TabStop = false };
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _nameTextBox = TextBox(UiLocalization.Text("AuthenticationProviderEdit.Label.Name", "&Name"), 0, provider?.Name ?? template?.Name);
            _kindComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, TabIndex = 3 };
            UiTheme.ApplyComboBox(_kindComboBox);
            foreach (var protocol in new[] { "responses", "chat_completions", "anthropic" }) { _kindComboBox.Items.Add(new ProviderKindChoice(protocol)); }
            _kindComboBox.SelectedIndex = kind == "chat_completions" ? 1 : kind == "anthropic" ? 2 : 0;
            _kindComboBox.Enabled = !_chatGpt && kind != "official_api";
            AddField(UiLocalization.Text("AuthenticationProviderEdit.Label.UpstreamFormat", "Upstream format (&Y)"), _kindComboBox, 1);
            _urlTextBox = TextBox(UiLocalization.Text("AuthenticationProviderEdit.Label.Url", "API &base URL"), 2, provider?.BaseUrl ?? template?.BaseUrl ?? (kind == "official_api" ? "https://api.openai.com/v1" : null));
            _keyTextBox = TextBox(UiLocalization.Text("AuthenticationProviderEdit.Label.Key", "API &key"), 3, null);
            _keyTextBox.UseSystemPasswordChar = true;
            var clearCaption = UiLocalization.Text("AuthenticationProviderEdit.ClearKey", "C&lear stored API key");
            _clearKeyCheckBox = new CheckBox { Text = clearCaption, AccessibleName = AuthenticationCenterUiText.Name(clearCaption), AutoSize = true, TabIndex = 8 };
            UiTheme.ApplyCheckBox(_clearKeyCheckBox);
            _clearKeyCheckBox.CheckedChanged += (sender, args) => { _keyTextBox.Enabled = !_clearKeyCheckBox.Checked; if (_clearKeyCheckBox.Checked) { _keyTextBox.Clear(); } };
            _fields.Controls.Add(_clearKeyCheckBox, 1, 4);
            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabStop = false, TabIndex = 0,
                AccessibleName = UiLocalization.Text("AuthenticationProviderEdit.Sections", "Configuration sections") };
            _sections = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, TabStop = false };
            _sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _content.Controls.Add(_sections);
            var basic = Section(UiLocalization.Text("AuthenticationProviderEdit.Basic", "Basic settings"));
            basic.Controls.Add(_fields);
            if (!_chatGpt)
            {
                _models = new AuthenticationModelsControl() { Dock = DockStyle.Top, Height = 280,
                    Models = provider?.Models ?? template?.Models ?? new List<AuthenticationCatalogModel>(), DefaultModel = provider?.Model ?? template?.Model };
                Section(UiLocalization.Text("AuthenticationProviderEdit.Models", "Models")).Controls.Add(_models);
                _advanced = new AuthenticationAdvancedControl(provider?.Advanced ?? template?.Advanced)
                    { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScroll = false };
                Section(UiLocalization.Text("AuthenticationProviderEdit.Advanced", "Advanced settings")).Controls.Add(_advanced);
                _advanced.Kind = CurrentKind;
                _kindComboBox.SelectedIndexChanged += async (sender, args) => { _advanced.Kind = CurrentKind; await SwitchBaseUrlForKindAsync(); };
                _models.FetchRequested += async (sender, args) => await FetchModelsAsync();
                _models.ModelAdded += async (sender, modelId) => await CompleteAddedModelAsync(modelId);
            }
            if (_chatGpt)
            {
                for (var row = 1; row <= 3; row++)
                {
                    _fields.GetControlFromPosition(0, row).Visible = false;
                    _fields.GetControlFromPosition(1, row).Visible = false;
                    _fields.RowStyles[row].Height = 0;
                }
                _clearKeyCheckBox.Visible = false;
                _login = new AuthenticationLoginControl(client, null, provider?.AccountId, copyCode, openUrl) { Dock = DockStyle.Top, TabIndex = 9 };
                _fields.Controls.Add(_login, 0, 5);
                _fields.SetColumnSpan(_login, 2);
                _login.StateChanged += (sender, args) =>
                {
                    if (_disposed) { return; }
                    _statusLabel.Text = _login.StatusText;
                    UpdateActions();
                };
            }
            _context = new AuthenticationContextControl(provider?.Advanced ?? template?.Advanced, _chatGpt, supportsContextSettings)
                { Dock = DockStyle.Top };
            Section(UiLocalization.Text("AuthenticationProviderEdit.Context", "Context and compaction")).Controls.Add(_context);
            _configButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Config", "Edit provider TOML (&4)"), 11);
            _configButton.Visible = supportsConfigEditing;
            _configButton.Click += async (sender, args) => await EditConfigAsync();
            _fields.Controls.Add(_configButton, 1, 6);
            _statusLabel = new ThemedTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Height = 55, TabStop = false, TabIndex = 2,
                AccessibleName = UiLocalization.Text("AuthenticationProviderEdit.Status", "Configuration status") };
            UiTheme.ApplyTextBox(_statusLabel, true, false);
            _saveButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Save", "&Save"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Cancel", "&Cancel"), 1);
            cancel.DialogResult = DialogResult.Cancel;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 3 };
            actions.Controls.AddRange(new Control[] { _saveButton, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_content, 0, 0);
            root.Controls.Add(new Label { Text = _chatGpt
                ? UiLocalization.Text("AuthenticationProviderEdit.ChatGptHint", "Models and reasoning levels are managed by Codex. Choose them in session settings. Saving the current account applies changes immediately; previously saved custom model mappings are cleared when saving or enabling this account.")
                : UiLocalization.Text("AuthenticationProviderEdit.NativeHint", "The first provider is enabled when saved. Saving the current provider updates global settings immediately; other providers are only saved. Leave the key empty to keep it."), AutoSize = true, Dock = DockStyle.Fill, TabStop = false }, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            root.Controls.Add(actions, 0, 3);
            Controls.Add(root);
            AcceptButton = _saveButton;
            CancelButton = cancel;
            _saveButton.Click += async (sender, args) => await SaveAsync();
            Shown += async (sender, args) =>
            {
                _nameTextBox.Focus();
                if (_chatGpt)
                {
                    if (_original == null || reauthenticate) { await _login.StartLoginAsync(); }
                    else { await _login.RestoreAsync(); }
                }
                // Earlier versions saved a format change without moving the preset endpoint.
                else { await SwitchBaseUrlForKindAsync(); }
            };
            FormClosing += ClosingAsync;
            UpdateActions();
        }

        public AuthenticationProvider SavedProvider { get; private set; }
        private Dictionary<string, object> ReadAdvanced()
        {
            var result = _chatGpt
                ? new Dictionary<string, object>(_configDraft?.Advanced ?? _original?.Advanced ?? _template?.Advanced ?? new Dictionary<string, object>())
                : _advanced.Read();
            _context.Apply(result);
            return result;
        }
        private string CurrentKind { get { return _chatGpt ? "chatgpt" : (_original?.Kind ?? _template?.Kind) == "official_api" ? "official_api" : (_kindComboBox.SelectedItem as ProviderKindChoice)?.Kind ?? "responses"; } }
        private GroupBox Section(string caption)
        {
            var group = new GroupBox { Text = caption, AccessibleName = caption, Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 24, 10, 10),
                TabStop = false, TabIndex = _sections.RowCount };
            _sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sections.Controls.Add(group, 0, _sections.RowCount++);
            return group;
        }
        private ThemedTextBox TextBox(string caption, int row, string value)
        {
            var control = new ThemedTextBox { Text = value ?? string.Empty, Dock = DockStyle.Fill, TabIndex = row * 2 + 1 };
            UiTheme.ApplyTextBox(control, false, false);
            AddField(caption, control, row);
            return control;
        }
        private void AddField(string caption, Control control, int row)
        {
            control.AccessibleName = AuthenticationCenterUiText.Name(caption);
            _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            _fields.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, TabIndex = row * 2, TabStop = false, UseMnemonic = true }, 0, row);
            _fields.Controls.Add(control, 1, row);
        }
        internal async Task FetchModelsAsync()
        {
            if (_chatGpt || _fetching || _saving || _disposed) { return; }
            _fetching = true;
            _models.Fetching = true;
            UpdateActions();
            string baseUrl = null;
            string status = null;
            IReadOnlyList<AuthenticationProviderPreset> presets = null;
            ModelMetadataCatalog usedCatalog = null;
            try
            {
                var request = CreateFetchRequest();
                var serializer = new JavaScriptSerializer();
                var snapshot = serializer.Serialize(request);
                var fetched = await _client.FetchModelsAsync(request, _lifetime.Token);
                if (_disposed) { return; }
                if (!string.Equals(snapshot, serializer.Serialize(CreateFetchRequest()), StringComparison.Ordinal))
                {
                    Announce(UiLocalization.Text("AuthenticationProviderEdit.FetchChanged", "Settings changed while loading models. Retrieve models again for the current settings."));
                    return;
                }
                _models.MergeFetched(fetched);
                status = fetched.Count == 0 ? UiLocalization.Text("AuthenticationProviderEdit.NoModels", "No models were returned. Existing models were retained; you can add models manually.") : UiLocalization.Text("AuthenticationProviderEdit.Fetched", "Models loaded. Existing settings were preserved.");
                if (_modelMetadata != null)
                {
                    presets = await _modelMetadata.ReadPresetsAsync(_lifetime.Token);
                    if (_disposed) { return; }
                    if (string.Equals(_urlTextBox.Text.Trim(), request.BaseUrl, StringComparison.Ordinal))
                    {
                        baseUrl = request.BaseUrl;
                        usedCatalog = _modelMetadata.CachedCatalog;
                        var filled = FillModelMetadata(baseUrl, presets, usedCatalog);
                        if (filled != null) { status += " " + filled; }
                    }
                }
                Announce(status);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) { Announce(AuthenticationCenterUiText.Error(error)); } }
            finally { _fetching = false; if (!_disposed) { _models.Fetching = false; UpdateActions(); } }
            if (baseUrl != null) { await CompleteModelMetadataAsync(baseUrl, presets, usedCatalog, status); }
        }

        // Manual additions complete only the added row; other blank fields may be intentional.
        internal async Task CompleteAddedModelAsync(string modelId)
        {
            if (_modelMetadata == null || _disposed || _saving) { return; }
            var baseUrl = _urlTextBox.Text.Trim();
            Func<AuthenticationCatalogModel, bool> added = model => string.Equals(model.Model, modelId, StringComparison.Ordinal);
            try
            {
                var presets = await _modelMetadata.ReadPresetsAsync(_lifetime.Token);
                if (_disposed || _saving || SaveOutcomeUnknown || !string.Equals(_urlTextBox.Text.Trim(), baseUrl, StringComparison.Ordinal)) { return; }
                var usedCatalog = _modelMetadata.CachedCatalog;
                var filled = FillModelMetadata(baseUrl, presets, usedCatalog, added);
                if (filled != null && !_fetching && !_editingConfig) { Announce(filled); }
                await CompleteModelMetadataAsync(baseUrl, presets, usedCatalog, filled, added);
            }
            catch (OperationCanceledException) { }
        }

        // A preset endpoint follows the selected format; a URL the user typed stays unchanged.
        internal async Task SwitchBaseUrlForKindAsync()
        {
            var kind = CurrentKind;
            var baseUrl = _urlTextBox.Text;
            var target = AuthenticationProtocolBaseUrlPlanner.Resolve(baseUrl, kind, _template, null);
            if (target == null && _modelMetadata != null)
            {
                try
                {
                    var presets = await _modelMetadata.ReadPresetsAsync(_lifetime.Token);
                    if (_disposed || _saving || SaveOutcomeUnknown || CurrentKind != kind ||
                        !string.Equals(_urlTextBox.Text, baseUrl, StringComparison.Ordinal)) { return; }
                    target = AuthenticationProtocolBaseUrlPlanner.Resolve(baseUrl, kind, _template, presets);
                }
                catch (OperationCanceledException) { return; }
            }
            if (target == null) { return; }
            _urlTextBox.Text = target;
            Announce(string.Format(UiLocalization.Text("AuthenticationProviderEdit.BaseUrlSwitched", "API base URL changed to {0} for the selected upstream format."), target));
        }

        // Public catalog data may arrive later; it only fills fields still blank.
        private async Task CompleteModelMetadataAsync(string baseUrl, IReadOnlyList<AuthenticationProviderPreset> presets, ModelMetadataCatalog usedCatalog,
            string priorStatus, Func<AuthenticationCatalogModel, bool> include = null)
        {
            if (_disposed) { return; }
            try
            {
                var catalog = await _modelMetadata.LoadCatalogAsync(_lifetime.Token);
                if (catalog == null || ReferenceEquals(catalog, usedCatalog) || _disposed || _saving || SaveOutcomeUnknown ||
                    !string.Equals(_urlTextBox.Text.Trim(), baseUrl, StringComparison.Ordinal)) { return; }
                var filled = FillModelMetadata(baseUrl, presets, catalog, include);
                if (filled != null && !_fetching && !_editingConfig)
                {
                    Announce(string.Equals(_statusLabel.Text, priorStatus, StringComparison.Ordinal) ? priorStatus + " " + filled : filled);
                }
            }
            catch (OperationCanceledException) { }
        }

        private string FillModelMetadata(string baseUrl, IReadOnlyList<AuthenticationProviderPreset> presets, ModelMetadataCatalog catalog,
            Func<AuthenticationCatalogModel, bool> include = null)
        {
            var resolver = new ModelMetadataResolver(baseUrl, presets, catalog);
            var sources = ModelMetadataSources.None;
            var count = _models.FillMissing(model =>
            {
                if (include != null && !include(model)) { return false; }
                var used = resolver.FillMissing(model);
                sources |= used;
                return used != ModelMetadataSources.None;
            });
            if (count == 0) { return null; }
            var source = sources == (ModelMetadataSources.Preset | ModelMetadataSources.ModelsDev)
                ? UiLocalization.Text("AuthenticationProviderEdit.MetadataSource.Both", "presets and models.dev")
                : sources == ModelMetadataSources.Preset ? UiLocalization.Text("AuthenticationProviderEdit.MetadataSource.Preset", "presets") : "models.dev";
            return string.Format(UiLocalization.Text("AuthenticationProviderEdit.MetadataFilled", "Filled blank context lengths or reasoning levels for {0} model(s) from {1}. Review them before saving."), count, source);
        }
        private AuthenticationModelFetchRequest CreateFetchRequest()
        {
            return new AuthenticationModelFetchRequest
            {
                ProviderId = !_clearKeyCheckBox.Checked && _original != null && _original.Kind == CurrentKind && string.Equals(_original.BaseUrl?.TrimEnd('/'), _urlTextBox.Text.Trim().TrimEnd('/'), StringComparison.Ordinal) ? _original.Id : null,
                Kind = CurrentKind, BaseUrl = _chatGpt ? string.Empty : _urlTextBox.Text.Trim(),
                ApiKey = _chatGpt || _clearKeyCheckBox.Checked || _keyTextBox.Text.Length == 0 ? null : _keyTextBox.Text,
                AccountId = _login?.AccountId, Advanced = _chatGpt ? new Dictionary<string, object>() : _advanced.Read()
            };
        }
        private AuthenticationProviderEdit CreateEdit(Dictionary<string, object> advanced)
        {
            return new AuthenticationProviderEdit
            {
                Id = _providerId, Name = _nameTextBox.Text.Trim(), Kind = CurrentKind,
                BaseUrl = _chatGpt ? string.Empty : _urlTextBox.Text.Trim(), Model = _chatGpt ? _configDraft?.Model ?? _original?.Model ?? string.Empty : _models.DefaultModel,
                ExpectedVersion = _original?.Version, PresetId = _original?.PresetId ?? _template?.Id ?? "custom",
                AccountId = _login?.AccountId, Models = _chatGpt ? new List<AuthenticationCatalogModel>() : _models.Models.ToList(),
                Advanced = advanced, ConfigToml = _configDraft?.Text,
                ApiKey = _chatGpt || _keyTextBox.Text.Length == 0 ? null : _keyTextBox.Text,
                ClearApiKey = !_chatGpt && _clearKeyCheckBox.Checked,
                EditorBase = _editorBase, EditorText = _editorText
            };
        }

        private async Task EditConfigAsync()
        {
            if (_editingConfig || _saving || _fetching || _disposed || _login?.IsLoginActive == true) return;
            AuthenticationProviderEdit edit;
            try { edit = CreateEdit(ReadAdvanced()); }
            catch (AuthenticationCenterException error) { Announce(AuthenticationCenterUiText.Error(error)); return; }
            _editingConfig = true;
            UpdateActions();
            try
            {
                if (_supportsFullConfigEditor) { await EditFullConfigAsync(edit); return; }
                var document = await _client.PreviewProviderConfigAsync(edit, null, _lifetime.Token);
                if (_disposed) return;
                using (var editor = new AuthenticationConfigEditForm(
                    UiLocalization.Text("AuthenticationConfig.ProviderTitle", "Edit provider configuration"),
                    UiLocalization.Text("AuthenticationConfig.ProviderHint", "Provider TOML draft with this provider's own fields, such as model, address and credentials. Global settings stay in Codex config.toml. Save here updates the form; use Save in the provider window to persist it. This text can contain credentials."),
                    document.Text, async (text, token) =>
                    {
                        var updated = await _client.PreviewProviderConfigAsync(edit, text, token);
                        ApplyConfigDraft(updated);
                    }))
                {
                    editor.ShowDialog(this);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) Announce(AuthenticationCenterUiText.Error(error)); }
            finally
            {
                _editingConfig = false;
                if (!_disposed) { UpdateActions(); _configButton.Focus(); }
            }
        }

        private async Task EditFullConfigAsync(AuthenticationProviderEdit edit)
        {
            // Reopening keeps pending global edits; otherwise show the current projection.
            var baseText = _editorBase;
            var text = _editorText;
            var inactive = _editorInactive;
            if (baseText == null)
            {
                var view = await _client.ViewProviderConfigAsync(edit, _lifetime.Token);
                if (_disposed) return;
                baseText = text = view.Text;
                inactive = view.Inactive;
            }
            using (var editor = new AuthenticationConfigEditForm(
                UiLocalization.Text("AuthenticationConfig.ProviderTitle", "Edit provider configuration"),
                UiLocalization.Text("AuthenticationConfig.FullHint", "The complete Codex config.toml as it will be after switching to this provider. Provider fields, such as model, address and credentials, are saved with this provider; other changes are written to config.toml when you save the provider window. Settings you leave unchanged keep their current values. This text can contain credentials."),
                text, InactiveText(inactive), (edited, token) => ApplyFullConfigDraftAsync(edit, baseText, edited, inactive, token)))
            {
                editor.ShowDialog(this);
            }
        }

        // Splits the edited text into form fields and remembers it for the outer save.
        internal async Task ApplyFullConfigDraftAsync(AuthenticationProviderEdit edit, string baseText, string edited,
            List<AuthenticationInactiveSetting> inactive, CancellationToken token)
        {
            edit.EditorBase = baseText;
            var updated = await _client.PreviewProviderConfigAsync(edit, edited, token);
            ApplyConfigDraft(updated);
            _editorBase = baseText;
            _editorText = edited;
            _editorInactive = inactive;
            if (updated.GlobalChanges.Count > 0)
            {
                Announce(string.Format(UiLocalization.Text("AuthenticationProviderEdit.GlobalChanges", "Saving this provider will also change these settings in config.toml: {0}"),
                    string.Join(", ", updated.GlobalChanges)));
            }
        }

        private static string InactiveText(IReadOnlyCollection<AuthenticationInactiveSetting> inactive)
        {
            if (inactive == null || inactive.Count == 0) return null;
            return UiLocalization.Text("AuthenticationConfig.InactiveHeader", "Saved with this provider but not applied when switching; move any you need into the configuration above:") +
                Environment.NewLine + string.Join(Environment.NewLine, inactive.Select(item => item.Path + " = " + item.Value));
        }

        internal Func<IReadOnlyList<string>, string> ChooseConflictPolicy { get; set; }

        private string ShowConflictDialog(IReadOnlyList<string> keys)
        {
            using (var dialog = new AuthenticationEditorConflictForm(keys))
            {
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Policy : null;
            }
        }

        internal void ApplyConfigDraft(AuthenticationConfigDocument document)
        {
            _configDraft = document;
            if (!_chatGpt)
            {
                _urlTextBox.Text = document.BaseUrl;
                _models.DefaultModel = document.Model;
                var parent = _advanced.Parent;
                parent.Controls.Remove(_advanced);
                _advanced.Dispose();
                _advanced = new AuthenticationAdvancedControl(document.Advanced)
                    { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoScroll = false, Kind = CurrentKind };
                parent.Controls.Add(_advanced);
            }
            var contextParent = _context.Parent;
            contextParent.Controls.Remove(_context);
            _context.Dispose();
            _context = new AuthenticationContextControl(document.Advanced, _chatGpt, _supportsContextSettings) { Dock = DockStyle.Top };
            contextParent.Controls.Add(_context);
        }

        internal async Task SaveAsync()
        {
            if (_saving || _fetching || _editingConfig || _disposed || SaveOutcomeUnknown || _login?.IsLoginActive == true) { return; }
            if (string.IsNullOrWhiteSpace(_nameTextBox.Text) || (!_chatGpt && string.IsNullOrWhiteSpace(_models.DefaultModel)) || (_chatGpt && string.IsNullOrWhiteSpace(_login.AccountId)))
            {
                Announce(UiLocalization.Text("AuthenticationProviderEdit.Required", "Enter a name and choose a default model. For ChatGPT, complete sign-in first."));
                if (string.IsNullOrWhiteSpace(_nameTextBox.Text)) { _nameTextBox.Focus(); }
                else if (!_chatGpt) { _models.SelectNextControl(null, true, true, true, false); }
                return;
            }
            Dictionary<string, object> advanced;
            try { advanced = ReadAdvanced(); }
            catch (AuthenticationCenterException error) { Announce(AuthenticationCenterUiText.Error(error)); return; }
            _saving = true;
            UpdateActions();
            Announce(UiLocalization.Text("AuthenticationProviderEdit.Status.Saving", "Saving configuration…"));
            try
            {
                var edit = CreateEdit(advanced);
                if (_save == null) throw new AuthenticationCenterException("runtime_busy");
                while (true)
                {
                    try
                    {
                        SaveResult = await _save(edit, _lifetime.Token);
                        break;
                    }
                    catch (AuthenticationCenterException conflict) when (conflict.Code == "editor_conflict" && edit.EditorConflictPolicy == null && !_disposed)
                    {
                        // Nothing was written; the user decides which side keeps the conflicting settings.
                        var policy = ChooseConflictPolicy(conflict.Keys);
                        if (policy == null)
                        {
                            Announce(UiLocalization.Text("AuthenticationConflict.Canceled", "Not saved. Review the conflicting settings, then save again."));
                            return;
                        }
                        edit.EditorConflictPolicy = policy;
                    }
                }
                SavedProvider = SaveResult.Provider;
                if (_disposed) { return; }
                _keyTextBox.Clear();
                Announce(UiLocalization.Text("AuthenticationCenter.Status.SavedNative", "Configuration saved."));
                _closing = true;
                DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                SaveOutcomeUnknown = error is AuthenticationCenterException failure && failure.Code == "operation_unknown";
                if (!_disposed) { Announce(AuthenticationCenterUiText.Error(error)); }
            }
            finally { _saving = false; if (!_disposed) { UpdateActions(); } }
        }
        private async void ClosingAsync(object sender, FormClosingEventArgs args)
        {
            if (_closing) { return; }
            if (_saving || _editingConfig) { args.Cancel = true; return; }
            if (_login?.IsLoginActive != true) { return; }
            args.Cancel = true;
            await _login.CancelLoginAsync();
            if (_disposed) { return; }
            if (_login.IsLoginActive) { Announce(UiLocalization.Text("AuthenticationProviderEdit.WaitLogin", "Sign-in is finishing. Please wait before closing.")); return; }
            _closing = true;
            Close();
        }
        private void UpdateActions()
        {
            if (_saveButton == null || _disposed) { return; }
            _saveButton.Enabled = !_saving && !_fetching && !_editingConfig && !SaveOutcomeUnknown && (_login == null || (!_login.IsLoginActive && !string.IsNullOrEmpty(_login.AccountId)));
            _configButton.Enabled = !_saving && !_fetching && !_editingConfig && !SaveOutcomeUnknown && _login?.IsLoginActive != true;
            _content.Enabled = !_saving && !_editingConfig;
        }
        private void Announce(string text) { _statusLabel.Text = text; }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed) { _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); _keyTextBox?.Clear(); }
            base.Dispose(disposing);
        }
        private sealed class ProviderKindChoice
        {
            internal ProviderKindChoice(string kind) { Kind = kind; }
            internal string Kind { get; private set; }
            public override string ToString() { return AuthenticationCenterUiText.FormatChoice(Kind); }
        }
    }
}
