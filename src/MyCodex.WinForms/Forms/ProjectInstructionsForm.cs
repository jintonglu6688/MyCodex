using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ProjectInstructionsForm : Form
    {
        private readonly bool _fileExists;
        private readonly bool _canSmartGenerate;
        private readonly bool _canSave;
        private readonly string _initialContent;
        private readonly string _basicTemplate;
        private readonly Func<bool, string, CancellationToken, Task<string>> _generateAsync;
        private readonly Func<string, Task> _saveAsync;
        private readonly TextBox _pathTextBox;
        private readonly ThemedTextBox _contentTextBox;
        private readonly TextBox _statusTextBox;
        private readonly Button _templateButton;
        private readonly Button _generateButton;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private CancellationTokenSource _generationCts;
        private bool _isGenerating;
        private bool _closeConfirmed;

        public ProjectInstructionsForm(
            ProjectInstructionsStatus status,
            bool canSmartGenerate,
            string basicTemplate,
            Func<bool, string, CancellationToken, Task<string>> generateAsync,
            Func<string, Task> saveAsync)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            _fileExists = status.Exists;
            _canSmartGenerate = canSmartGenerate;
            _canSave = status.CanSave;
            _initialContent = status.Exists ? status.ExistingContent ?? string.Empty : string.Empty;
            _basicTemplate = basicTemplate ?? string.Empty;
            _generateAsync = generateAsync;
            _saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));

            Text = UiLocalization.Text("ProjectInstructionsForm.Title", "Project Instructions");
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            Width = 860;
            Height = 680;
            UiTheme.ApplyForm(this);

            _pathTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("ProjectInstructionsForm.Accessible.Path", "Path"),
                ReadOnly = true,
                Text = status.TargetPath ?? string.Empty,
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_pathTextBox, false, true);

            _contentTextBox = new ThemedTextBox
            {
                AccessibleName = UiLocalization.Text("ProjectInstructionsForm.Accessible.Content", "Content"),
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsReturn = true,
                AcceptsTab = false,
                Text = _initialContent,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_contentTextBox, true, false);

            _statusTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("ProjectInstructionsForm.Accessible.Status", "Status"),
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false,
                Height = 62,
                Text = BuildInitialStatusText(status)
            };
            UiTheme.ApplyTextBox(_statusTextBox, false, true);

            _templateButton = CreateButton(
                UiLocalization.Text("ProjectInstructionsForm.Button.UseTemplate", "Use Template(&T)"),
                UiLocalization.Text("ProjectInstructionsForm.Accessible.UseTemplate", "Use template"),
                2,
                UiGlyph.FileChange);
            _templateButton.Click += OnTemplateButtonClick;

            _generateButton = CreateButton(string.Empty, UiLocalization.Text("ProjectInstructionsForm.Accessible.Generate", "Generate"), 3, UiGlyph.Codex);
            _generateButton.Click += async (sender, e) => await GenerateOrApplyTemplateAsync();

            _saveButton = CreateButton(
                _fileExists
                    ? UiLocalization.Text("ProjectInstructionsForm.Button.Save", "Save(&S)")
                    : UiLocalization.Text("ProjectInstructionsForm.Button.Create", "Create(&C)"),
                _fileExists
                    ? UiLocalization.Text("ProjectInstructionsForm.Accessible.Save", "Save")
                    : UiLocalization.Text("ProjectInstructionsForm.Accessible.Create", "Create"),
                4,
                UiGlyph.Ok);
            _saveButton.Click += async (sender, e) => await SaveAsync();

            _cancelButton = CreateButton(
                UiLocalization.Text("ProjectInstructionsForm.Button.Cancel", "Cancel(&N)"),
                UiLocalization.Text("ProjectInstructionsForm.Accessible.Cancel", "Cancel"),
                5,
                UiGlyph.Cancel);
            _cancelButton.Click += OnCancelButtonClick;
            CancelButton = _cancelButton;
            _contentTextBox.TextChanged += (sender, e) => UpdateActionState();

            Controls.Add(BuildLayout());
            UpdateActionState();
            Shown += ProjectInstructionsForm_Shown;
            FormClosing += ProjectInstructionsForm_FormClosing;
        }

        private Control BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12),
                BackColor = UiTheme.WindowBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var pathLabel = CreateLabel(UiLocalization.Text("ProjectInstructionsForm.Label.Path", "Path(&P)"), _pathTextBox);
            var contentLabel = CreateLabel(UiLocalization.Text("ProjectInstructionsForm.Label.Content", "Content(&O)"), _contentTextBox);

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.Controls.Add(pathLabel, 0, 0);
            pathPanel.Controls.Add(_pathTextBox, 1, 0);

            contentLabel.Dock = DockStyle.Top;
            contentLabel.Margin = new Padding(0, 0, 0, 4);
            _contentTextBox.Dock = DockStyle.Fill;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_saveButton);
            buttonPanel.Controls.Add(_generateButton);
            buttonPanel.Controls.Add(_templateButton);

            root.Controls.Add(pathPanel, 0, 0);
            root.Controls.Add(contentLabel, 0, 1);
            root.Controls.Add(_contentTextBox, 0, 2);
            root.Controls.Add(_statusTextBox, 0, 3);
            root.Controls.Add(buttonPanel, 0, 4);

            return root;
        }

        private void ProjectInstructionsForm_Shown(object sender, EventArgs e)
        {
            if (_contentTextBox.TextLength > 0)
            {
                _contentTextBox.Focus();
                TextBoxCaretHelper.MoveToStart(_contentTextBox);
                return;
            }

            _generateButton.Focus();
        }

        private async Task GenerateOrApplyTemplateAsync()
        {
            if (!_canSmartGenerate || _generateAsync == null)
            {
                SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.SmartUnavailable", "Smart generation is unavailable. Use the basic template instead."));
                return;
            }

            SetBusy(true);
            SetStatus(HasContent()
                ? UiLocalization.Text("ProjectInstructionsForm.Status.Updating", "Updating project instructions...")
                : UiLocalization.Text("ProjectInstructionsForm.Status.Generating", "Generating project instructions..."));
            _generationCts = new CancellationTokenSource();
            try
            {
                var generated = await _generateAsync(
                    HasContent(),
                    _contentTextBox.Text ?? string.Empty,
                    _generationCts.Token).ConfigureAwait(true);
                TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, ProjectInstructionsService.ExtractGeneratedContent(generated));
                SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.Generated", "Project instructions generated. Review and save when ready."));
                _contentTextBox.Focus();
                UpdateActionState();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.Canceled", "Project instructions generation canceled."));
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("ProjectInstructionsForm.Status.GenerateFailed.Template", "Generation failed: {0}"),
                    ex.Message));
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (_generationCts != null)
                {
                    _generationCts.Dispose();
                    _generationCts = null;
                }
                SetBusy(false);
            }
        }

        private async Task SaveAsync()
        {
            if (!_canSave)
            {
                SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.SaveUnsupported", "The current runtime cannot write project instructions from this dialog."));
                return;
            }

            var content = _contentTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ProjectInstructionsForm.Message.ContentRequired", "Enter project instructions before saving."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _contentTextBox.Focus();
                return;
            }

            SetBusy(true);
            try
            {
                await _saveAsync(content).ConfigureAwait(true);
                _closeConfirmed = true;
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("ProjectInstructionsForm.Status.SaveFailed.Template", "Save failed: {0}"),
                    ex.Message));
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateActionState()
        {
            _generateButton.Text = _fileExists
                ? UiLocalization.Text("ProjectInstructionsForm.Button.Update", "Smart Update(&G)")
                : UiLocalization.Text("ProjectInstructionsForm.Button.Generate", "Smart Generate(&G)");
            _generateButton.AccessibleName = _fileExists
                ? UiLocalization.Text("ProjectInstructionsForm.Accessible.Update", "Smart update")
                : UiLocalization.Text("ProjectInstructionsForm.Accessible.Generate", "Smart generate");
            _generateButton.Enabled = !_isGenerating && _canSmartGenerate;
            _templateButton.Visible = !_fileExists;
            _templateButton.Enabled = !_isGenerating;
            _saveButton.Enabled = !_isGenerating && _canSave;
        }

        private void SetBusy(bool busy)
        {
            _isGenerating = busy;
            _contentTextBox.ReadOnly = busy;
            _generateButton.Enabled = !busy && _canSmartGenerate;
            _templateButton.Enabled = !busy;
            _saveButton.Enabled = !busy && _canSave;
            _cancelButton.Enabled = true;
            UseWaitCursor = busy;
            Text = busy
                ? UiLocalization.Text("ProjectInstructionsForm.Title.Generating", "Project Instructions - Generating")
                : UiLocalization.Text("ProjectInstructionsForm.Title", "Project Instructions");
            _cancelButton.Text = busy
                ? UiLocalization.Text("ProjectInstructionsForm.Button.CancelGeneration", "Cancel Generation(&N)")
                : UiLocalization.Text("ProjectInstructionsForm.Button.Cancel", "Cancel(&N)");
            _cancelButton.AccessibleName = busy
                ? UiLocalization.Text("ProjectInstructionsForm.Accessible.CancelGeneration", "Cancel generation")
                : UiLocalization.Text("ProjectInstructionsForm.Accessible.Cancel", "Cancel");
            UpdateActionState();
        }

        private void SetStatus(string text)
        {
            TextBoxCaretHelper.SetTextAndMoveToStart(_statusTextBox, text ?? string.Empty);
        }

        private void OnCancelButtonClick(object sender, EventArgs e)
        {
            if (!_isGenerating)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }

            SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.Canceling", "Canceling project instructions generation..."));
            _generationCts?.Cancel();
        }

        private bool HasContent()
        {
            return !string.IsNullOrWhiteSpace(_contentTextBox.Text);
        }

        private void OnTemplateButtonClick(object sender, EventArgs e)
        {
            if (HasUnsavedChanges() &&
                !ConfirmReplaceContent(
                    UiLocalization.Text("ProjectInstructionsForm.Message.ConfirmTemplateReplace", "Replace the current content with the basic template?")))
            {
                return;
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_contentTextBox, _basicTemplate);
            SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.TemplateApplied", "Basic template applied. Review and save when ready."));
            _contentTextBox.Focus();
            UpdateActionState();
        }

        private void ProjectInstructionsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closeConfirmed)
            {
                return;
            }

            if (_isGenerating)
            {
                e.Cancel = true;
                if (_generationCts != null && !_generationCts.IsCancellationRequested)
                {
                    SetStatus(UiLocalization.Text("ProjectInstructionsForm.Status.Canceling", "Canceling project instructions generation..."));
                    _generationCts.Cancel();
                }
                return;
            }

            if (!HasUnsavedChanges())
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                UiLocalization.Text("ProjectInstructionsForm.Message.UnsavedChanges", "Current content has changed. Close without saving?"),
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _closeConfirmed = true;
        }

        private bool HasUnsavedChanges()
        {
            return !string.Equals(_contentTextBox.Text ?? string.Empty, _initialContent ?? string.Empty, StringComparison.Ordinal);
        }

        private bool ConfirmReplaceContent(string message)
        {
            return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private static string BuildInitialStatusText(ProjectInstructionsStatus status)
        {
            if (!string.IsNullOrWhiteSpace(status.UnsupportedReason))
            {
                return status.UnsupportedReason;
            }

            return status.Exists
                ? UiLocalization.Text("ProjectInstructionsForm.Status.ExistingLoaded", "Existing project instructions loaded. Edit, update, or save.")
                : UiLocalization.Text("ProjectInstructionsForm.Status.ReadyToGenerate", "No project instructions found. Generate smart content or apply the basic template.");
        }

        private static Label CreateLabel(string text, Control target)
        {
            return new Label
            {
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Height = 24,
                Dock = DockStyle.Fill,
                TabStop = false,
                UseMnemonic = true,
                BackColor = UiTheme.WindowBackColor,
                ForeColor = UiTheme.TextColor,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 120,
                Height = 32,
                TabIndex = tabIndex,
                UseVisualStyleBackColor = false
            };
            UiTheme.ApplyButton(button, glyph);
            return button;
        }
    }
}
