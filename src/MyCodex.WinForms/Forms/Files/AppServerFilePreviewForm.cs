using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal enum AppServerFilePreviewCloseChoice
    {
        Save,
        Discard,
        Cancel
    }

    internal sealed partial class AppServerFilePreviewForm : Form, IAppServerFileWatchTarget
    {
        private readonly string _path;
        private Func<byte[], Task> _saveAsync;
        private Func<Task<string>> _loadTextAsync;
        private Func<bool> _confirmDiscardChanges;
        private Func<AppServerFilePreviewCloseChoice> _confirmCloseChanges;
        private InternalTextEditorControl _editor;
        private Label _statusLabel;
        private Button _reloadButton;
        private Button _saveButton;
        private Button _closeButton;
        private bool _canEdit;
        private bool _isDirty;
        private bool _hasExternalChange;
        private bool _isLoading;
        private bool _isUpdatingContent;

        internal bool IsBusyForRuntimeTransition
        {
            get { return _isLoading || _isDirty; }
        }
        private bool _closeWithoutDirtyPrompt;
        private bool _closePromptInProgress;
        private string _lastFindText;
        private string _lastReplaceText;
        private int _lineNumberForTests;
        private bool _suppressFindReplaceDialogForTests;
        private int _findReplaceDialogOpenCountForTests;
        private bool _lastFindReplaceDialogReplaceModeForTests;
        private bool _suppressGoToLineDialogForTests;
        private int _goToLineDialogOpenCountForTests;

        public AppServerFilePreviewForm(string path, string dataBase64)
            : this(path, dataBase64, null)
        {
        }

        public AppServerFilePreviewForm(string path, string dataBase64, Func<byte[], Task> saveAsync)
        {
            _path = path ?? string.Empty;
            _saveAsync = saveAsync;
            var decodedText = DecodePreviewText(dataBase64, out _canEdit);
            InitializePreviewUi(_path, decodedText, _canEdit);
        }

        public AppServerFilePreviewForm(
            string path,
            Func<Task<string>> loadTextAsync,
            Func<byte[], Task> saveAsync,
            Func<bool> confirmDiscardChanges = null,
            Func<AppServerFilePreviewCloseChoice> confirmCloseChanges = null)
        {
            _path = path ?? string.Empty;
            _loadTextAsync = loadTextAsync ?? throw new ArgumentNullException(nameof(loadTextAsync));
            _saveAsync = saveAsync;
            _confirmDiscardChanges = confirmDiscardChanges ?? ConfirmDiscardChanges;
            _confirmCloseChanges = confirmCloseChanges ?? ConfirmCloseChanges;
            _canEdit = true;
            InitializePreviewUi(_path, string.Empty, true);
        }

        public string WatchPath
        {
            get { return _path; }
        }

        private bool IsClosingOrDisposed
        {
            get { return IsDisposed || Disposing; }
        }

        internal string ContentTextForTests
        {
            get { return _editor.ContentText; }
        }

        internal bool ContentReadOnlyForTests
        {
            get { return _editor.ReadOnly; }
        }

        internal string ContentAccessibleNameForTests
        {
            get { return _editor.AccessibleName; }
        }

        internal string EditorControlTypeNameForTests
        {
            get { return _editor.EditorControlTypeNameForTests; }
        }

        internal string InnerEditorControlTypeNameForTests
        {
            get { return _editor.InnerEditorControlTypeNameForTests; }
        }

        internal string InnerEditorAccessibleNameForTests
        {
            get { return _editor.InnerAccessibleNameForTests; }
        }

        internal bool EditorTabStopForTests
        {
            get { return _editor.TabStop; }
        }

        internal bool InnerEditorTabStopForTests
        {
            get { return _editor.InnerTabStopForTests; }
        }

        internal int InnerEditorTabIndexForTests
        {
            get { return _editor.InnerTabIndexForTests; }
        }

        internal string FocusedAccessibleNameForTests
        {
            get { return _editor.FocusedAccessibleNameForTests; }
        }

        internal bool IsDirtyForTests
        {
            get { return _isDirty; }
        }

        internal bool HasExternalChangeForTests
        {
            get { return _hasExternalChange; }
        }

        internal bool ReloadEnabledForTests
        {
            get { return _reloadButton.Enabled; }
        }

        internal static AppServerFilePreviewForm CreateForTests(string path, string dataBase64)
        {
            return new AppServerFilePreviewForm(path, dataBase64);
        }

        internal static AppServerFilePreviewForm CreateForTests(string path, string dataBase64, Func<byte[], Task> saveAsync)
        {
            return new AppServerFilePreviewForm(path, dataBase64, saveAsync);
        }

        internal static AppServerFilePreviewForm CreateLiveForTests(
            string path,
            Func<Task<string>> loadTextAsync,
            Func<byte[], Task> saveAsync,
            Func<bool> confirmDiscardChanges = null,
            Func<AppServerFilePreviewCloseChoice> confirmCloseChanges = null)
        {
            return new AppServerFilePreviewForm(path, loadTextAsync, saveAsync, confirmDiscardChanges, confirmCloseChanges);
        }

        internal void SetContentTextForTests(string text)
        {
            _editor.SetContentText(text);
            if (!_editor.ReadOnly)
            {
                _isDirty = true;
                RefreshPreviewState();
            }
        }

        internal Task SaveForTestsAsync()
        {
            return SaveAsync();
        }

        internal Task LoadForTestsAsync()
        {
            return LoadAsync();
        }

        internal Task ReloadForTestsAsync()
        {
            return ReloadAsync();
        }

        internal Task<bool> ConfirmCloseForTestsAsync()
        {
            return ConfirmCloseAsync();
        }

        public async Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change)
        {
            if (IsClosingOrDisposed || _targetExitPrepared)
            {
                return;
            }

            if (_isDirty)
            {
                _hasExternalChange = true;
                _statusLabel.Text = UiLocalization.Text(
                    "AppServerFilePreviewForm.Status.ExternalChangeDirty",
                    "File changed externally. Reload to discard your unsaved edits.");
                RefreshPreviewState();
                return;
            }

            await ReloadAsync(false);
        }

        public void HandleWatchUnavailable(string message)
        {
            if (IsClosingOrDisposed)
            {
                return;
            }

            _statusLabel.Text = string.IsNullOrWhiteSpace(message)
                ? UiLocalization.Text("AppServerFilePreviewForm.Status.LiveRefreshUnavailable", "Live refresh unavailable.")
                : message;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            if (keyData == Keys.F3)
            {
                FindFromShortcut(true);
                return true;
            }

            if (keyData == (Keys.Shift | Keys.F3))
            {
                FindFromShortcut(false);
                return true;
            }

            if (keyData == (Keys.Control | Keys.F))
            {
                OpenFindReplaceDialog(false);
                return true;
            }

            if (keyData == (Keys.Control | Keys.H))
            {
                OpenFindReplaceDialog(true);
                return true;
            }

            if (keyData == (Keys.Control | Keys.G))
            {
                OpenGoToLineDialog();
                return true;
            }

            if (keyData == (Keys.Control | Keys.S) && _saveButton.Enabled)
            {
                _ = SaveAsync();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closeWithoutDirtyPrompt || !_isDirty)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;
            if (_closePromptInProgress)
            {
                return;
            }

            _closePromptInProgress = true;
            try
            {
                var canClose = await ConfirmCloseAsync().ConfigureAwait(true);
                if (canClose && !IsClosingOrDisposed)
                {
                    _closeWithoutDirtyPrompt = true;
                    Close();
                }
            }
            finally
            {
                _closePromptInProgress = false;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _editor.FocusEditor();
        }

        internal Task LoadAsync()
        {
            return ReloadAsync(false);
        }

        private async Task<bool> SaveAsync(bool forTargetExit = false)
        {
            if (IsClosingOrDisposed || _isLoading || _targetExitPrepared || (_targetExitPreparing && !forTargetExit) || _saveAsync == null || !_canEdit)
            {
                return false;
            }

            if (_hasExternalChange && _isDirty)
            {
                _statusLabel.Text = UiLocalization.Text(
                    "AppServerFilePreviewForm.Status.SaveBlockedExternalChange",
                    "File changed externally. Reload before saving.");
                RefreshPreviewState();
                return false;
            }

            _isLoading = true;
            RefreshPreviewState();
            try
            {
                await _saveAsync(Encoding.UTF8.GetBytes(_editor.ContentText));
                if (IsClosingOrDisposed)
                {
                    return false;
                }

                _isDirty = false;
                _hasExternalChange = false;
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.Saved", "File saved.");
                return true;
            }
            catch (Exception ex)
            {
                if (IsClosingOrDisposed)
                {
                    return false;
                }

                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.SaveFailed", "Unable to save this file.");
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                if (!IsClosingOrDisposed)
                {
                    _isLoading = false;
                    RefreshPreviewState();
                }
            }
        }

        private async Task<bool> ConfirmCloseAsync()
        {
            if (IsClosingOrDisposed || !_isDirty)
            {
                return true;
            }

            var choice = _confirmCloseChanges();
            if (choice == AppServerFilePreviewCloseChoice.Cancel)
            {
                return false;
            }

            if (choice == AppServerFilePreviewCloseChoice.Discard)
            {
                _isDirty = false;
                _hasExternalChange = false;
                RefreshPreviewState();
                return true;
            }

            return await SaveAsync().ConfigureAwait(true);
        }

        private Task ReloadAsync()
        {
            return ReloadAsync(true);
        }

        private async Task ReloadAsync(bool requireDirtyConfirmation)
        {
            if (IsClosingOrDisposed || _loadTextAsync == null || _isLoading)
            {
                return;
            }

            if (_targetExitPreparing || _targetExitPrepared || (requireDirtyConfirmation && _isDirty && !_confirmDiscardChanges()))
            {
                return;
            }

            _isLoading = true;
            RefreshPreviewState();
            try
            {
                var text = await _loadTextAsync().ConfigureAwait(true);
                if (IsClosingOrDisposed)
                {
                    return;
                }

                SetContentText(text ?? string.Empty);
                _canEdit = true;
                _isDirty = false;
                _hasExternalChange = false;
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.Reloaded", "File reloaded.");
            }
            catch (Exception ex)
            {
                if (IsClosingOrDisposed)
                {
                    return;
                }

                // The editor no longer mirrors the file, so saving it could truncate or overwrite the file.
                _canEdit = false;
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.ReloadFailed", "Unable to reload file.");
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsClosingOrDisposed)
                {
                    _isLoading = false;
                    RefreshPreviewState();
                }
            }
        }

        private void SetContentText(string text)
        {
            if (IsClosingOrDisposed)
            {
                return;
            }

            _isUpdatingContent = true;
            try
            {
                _editor.SetContentText(text);
            }
            finally
            {
                _isUpdatingContent = false;
            }
        }

        private void RefreshPreviewState()
        {
            if (IsClosingOrDisposed || _reloadButton == null || _saveButton == null)
            {
                return;
            }

            _editor.ReadOnly = !_canEdit || _isLoading || _targetExitPreparing || _targetExitPrepared;
            _reloadButton.Enabled = !_targetExitPreparing && !_targetExitPrepared && !_isLoading && (_hasExternalChange || _isDirty) && _loadTextAsync != null;
            _saveButton.Enabled = !_targetExitPreparing && !_targetExitPrepared && !_isLoading && _canEdit && _saveAsync != null && (!_hasExternalChange || !_isDirty);
        }

        private bool ConfirmDiscardChanges()
        {
            return MessageBox.Show(
                this,
                UiLocalization.Text(
                    "AppServerFilePreviewForm.Confirm.ReloadDiscardChanges",
                    "Reload this file and discard your unsaved edits?"),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private AppServerFilePreviewCloseChoice ConfirmCloseChanges()
        {
            var result = MessageBox.Show(
                this,
                UiLocalization.Text(
                    "AppServerFilePreviewForm.Confirm.CloseDirty",
                    "Save changes before closing this preview?"),
                AppConstants.AppName,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button3);

            if (result == DialogResult.Yes)
            {
                return AppServerFilePreviewCloseChoice.Save;
            }

            return result == DialogResult.No
                ? AppServerFilePreviewCloseChoice.Discard
                : AppServerFilePreviewCloseChoice.Cancel;
        }

        private void InitializePreviewUi(string path, string initialText, bool canEdit)
        {
            _lastFindText = string.Empty;
            _lastReplaceText = string.Empty;
            _lineNumberForTests = 1;

            Text = string.Format(
                UiLocalization.Text("AppServerFilePreviewForm.Title.Template", "{0} - Text editor"),
                GetPreviewTitleDisplayName(path));
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(640, 420);
            ShowInTaskbar = true;
            UiTheme.ApplyForm(this);

            _editor = new InternalTextEditorControl(UiLocalization.Text("AppServerFilePreviewForm.Accessible.Content", "Content"))
            {
                TabIndex = 0
            };
            _editor.ReadOnly = !canEdit || _saveAsync == null;
            _editor.SetContentText(initialText);
            _editor.ContentChangedByUser += (sender, e) =>
            {
                if (_isUpdatingContent || _editor.ReadOnly)
                {
                    return;
                }

                _isDirty = true;
                RefreshPreviewState();
            };

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, false);

            _reloadButton = new Button
            {
                Text = UiLocalization.Text("AppServerFilePreviewForm.Button.Reload", "Reload(&R)"),
                AccessibleName = UiLocalization.Text("AppServerFilePreviewForm.Accessible.Reload", "Reload"),
                Width = 112,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1,
                Enabled = false
            };
            UiTheme.ApplyButton(_reloadButton, UiGlyph.Refresh);
            _reloadButton.Click += async (sender, e) => await ReloadAsync();

            _saveButton = new Button
            {
                Text = UiLocalization.Text("AppServerFilePreviewForm.Button.Save", "Save(&S)"),
                AccessibleName = UiLocalization.Text("AppServerFilePreviewForm.Accessible.Save", "Save"),
                Width = 112,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 2,
                Enabled = canEdit && _saveAsync != null
            };
            UiTheme.ApplyButton(_saveButton, UiGlyph.Ok);
            _saveButton.Click += async (sender, e) => await SaveAsync();

            _closeButton = new Button
            {
                Text = UiLocalization.Text("AppServerFilePreviewForm.Button.Close", "Close(&C)"),
                AccessibleName = UiLocalization.Text("AppServerFilePreviewForm.Accessible.Close", "Close"),
                Width = 112,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 3
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            _closeButton.Click += (sender, e) => Close();

            BuildLayout();
            RefreshPreviewState();
        }

        private static string DecodePreviewText(string dataBase64, out bool canEdit)
        {
            canEdit = false;
            if (string.IsNullOrWhiteSpace(dataBase64))
            {
                return UnavailableMessage();
            }

            try
            {
                var text = DecodeEditableText(Convert.FromBase64String(dataBase64));
                if (text.IndexOf('\0') >= 0)
                {
                    return UnavailableMessage();
                }

                canEdit = true;
                return text;
            }
            catch (FormatException)
            {
                return UnavailableMessage();
            }
            catch (ArgumentException)
            {
                return UnavailableMessage();
            }
        }

        // Strict decoding rejects non-UTF-8 files (such as GBK) instead of saving U+FFFD over them,
        // and keeps a BOM as U+FEFF so saving writes it back.
        internal static string DecodeEditableText(byte[] bytes)
        {
            return new UTF8Encoding(false, true).GetString(bytes ?? new byte[0]);
        }

        internal static AppServerFilePreviewForm CreateForLocalFile(string path)
        {
            return new AppServerFilePreviewForm(
                path,
                () => Task.FromResult(DecodeEditableText(File.ReadAllBytes(path))),
                data =>
                {
                    File.WriteAllBytes(path, data ?? new byte[0]);
                    return Task.CompletedTask;
                });
        }

        private static string UnavailableMessage()
        {
            return UiLocalization.Text("AppServerFilePreviewForm.Message.PreviewUnavailable", "Preview is unavailable for this file.");
        }

        private static string GetPreviewTitleDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return UiLocalization.Text("AppServerFilePreviewForm.Value.UnknownFile", "File");
            }

            var trimmed = path.Trim().TrimEnd('\\', '/');
            if (trimmed.Length == 0)
            {
                return UiLocalization.Text("AppServerFilePreviewForm.Value.UnknownFile", "File");
            }

            var separatorIndex = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return separatorIndex >= 0 && separatorIndex < trimmed.Length - 1
                ? trimmed.Substring(separatorIndex + 1)
                : trimmed;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                Width = 364,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor
            };
            buttonPanel.Controls.Add(_reloadButton);
            buttonPanel.Controls.Add(_saveButton);
            buttonPanel.Controls.Add(_closeButton);

            root.Controls.Add(_editor, 0, 0);
            root.Controls.Add(_statusLabel, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(root);
        }
    }
}
