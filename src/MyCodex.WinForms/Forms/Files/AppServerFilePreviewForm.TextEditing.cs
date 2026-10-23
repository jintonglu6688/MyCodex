using System;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class AppServerFilePreviewForm
    {
        internal bool SuppressFindReplaceDialogForTests
        {
            get { return _suppressFindReplaceDialogForTests; }
            set { _suppressFindReplaceDialogForTests = value; }
        }

        internal int FindReplaceDialogOpenCountForTests
        {
            get { return _findReplaceDialogOpenCountForTests; }
        }

        internal bool LastFindReplaceDialogReplaceModeForTests
        {
            get { return _lastFindReplaceDialogReplaceModeForTests; }
        }

        internal bool SuppressGoToLineDialogForTests
        {
            get { return _suppressGoToLineDialogForTests; }
            set { _suppressGoToLineDialogForTests = value; }
        }

        internal int GoToLineDialogOpenCountForTests
        {
            get { return _goToLineDialogOpenCountForTests; }
        }

        internal string FindTextForTests
        {
            get { return _lastFindText; }
            set { _lastFindText = value ?? string.Empty; }
        }

        internal string ReplaceTextForTests
        {
            get { return _lastReplaceText; }
            set { _lastReplaceText = value ?? string.Empty; }
        }

        internal string SelectedTextForTests
        {
            get { return _editor.SelectedText; }
        }

        internal int SelectionStartForTests
        {
            get { return _editor.SelectionStart; }
        }

        internal int GoToLineNumberForTests
        {
            get { return _lineNumberForTests; }
            set { _lineNumberForTests = Math.Max(1, value); }
        }

        internal int CurrentLineForTests
        {
            get { return _editor.CurrentLineNumber; }
        }

        internal void FindNextForTests()
        {
            FindText(_lastFindText, true);
        }

        internal void ReplaceCurrentForTests()
        {
            ReplaceCurrent(_lastFindText, _lastReplaceText);
        }

        internal void ReplaceAllForTests()
        {
            ReplaceAll(_lastFindText, _lastReplaceText);
        }

        internal void GoToLineForTests()
        {
            GoToLine(_lineNumberForTests);
        }

        internal void OpenFindReplaceDialog(bool replaceMode)
        {
            _findReplaceDialogOpenCountForTests++;
            _lastFindReplaceDialogReplaceModeForTests = replaceMode;
            if (_suppressFindReplaceDialogForTests)
            {
                return;
            }

            using (var dialog = new AppServerFilePreviewFindReplaceDialog(this, replaceMode))
            {
                dialog.ShowDialog(this);
            }

            _editor.FocusEditor();
        }

        internal void OpenGoToLineDialog()
        {
            _goToLineDialogOpenCountForTests++;
            if (_suppressGoToLineDialogForTests)
            {
                return;
            }

            using (var dialog = new AppServerFilePreviewGoToLineDialog(this))
            {
                dialog.ShowDialog(this);
            }

            _editor.FocusEditor();
        }

        internal bool FindFromDialog(string query, bool forward)
        {
            _lastFindText = query ?? string.Empty;
            return FindText(_lastFindText, forward);
        }

        internal void ReplaceCurrentFromDialog(string query, string replacement)
        {
            _lastFindText = query ?? string.Empty;
            _lastReplaceText = replacement ?? string.Empty;
            ReplaceCurrent(_lastFindText, _lastReplaceText);
        }

        internal void ReplaceAllFromDialog(string query, string replacement)
        {
            _lastFindText = query ?? string.Empty;
            _lastReplaceText = replacement ?? string.Empty;
            ReplaceAll(_lastFindText, _lastReplaceText);
        }

        internal int GetLineCountForDialog()
        {
            return _editor.LineCount;
        }

        internal void GoToLineFromDialog(int lineNumber)
        {
            _lineNumberForTests = Math.Max(1, lineNumber);
            GoToLine(_lineNumberForTests);
        }

        private void FindFromShortcut(bool forward)
        {
            if (string.IsNullOrEmpty(_lastFindText))
            {
                OpenFindReplaceDialog(false);
                return;
            }

            FindText(_lastFindText, forward);
        }

        private bool FindText(string query, bool forward)
        {
            if (string.IsNullOrEmpty(query))
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.FindRequired", "Enter search text.");
                return false;
            }

            if (!_editor.FindText(query, forward))
            {
                _statusLabel.Text = string.Format(
                    UiLocalization.Text("AppServerFilePreviewForm.Status.NotFound.Template", "Not found: {0}"),
                    query);
                _editor.FocusEditor();
                return false;
            }

            _statusLabel.Text = string.Format(
                UiLocalization.Text("AppServerFilePreviewForm.Status.Found.Template", "Found: {0}"),
                query);
            return true;
        }

        private void ReplaceCurrent(string query, string replacement)
        {
            if (!_canEdit)
            {
                _statusLabel.Text = UnavailableMessage();
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.FindRequired", "Enter search text.");
                return;
            }

            if (!_editor.ReplaceCurrent(query, replacement))
            {
                _statusLabel.Text = string.Format(
                    UiLocalization.Text("AppServerFilePreviewForm.Status.NotFound.Template", "Not found: {0}"),
                    query);
                _editor.FocusEditor();
                return;
            }

            MarkContentDirtyAfterProgrammaticEdit();
            _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.Replaced", "Replaced current match.");
            RefreshPreviewState();
        }

        private void ReplaceAll(string query, string replacement)
        {
            if (!_canEdit)
            {
                _statusLabel.Text = UnavailableMessage();
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFilePreviewForm.Status.FindRequired", "Enter search text.");
                return;
            }

            var count = _editor.ReplaceAll(query, replacement);
            if (count > 0)
            {
                MarkContentDirtyAfterProgrammaticEdit();
            }

            _statusLabel.Text = string.Format(
                UiLocalization.Text("AppServerFilePreviewForm.Status.ReplaceAll.Template", "Replaced {0} matches."),
                count);
            RefreshPreviewState();
            _editor.FocusEditor();
        }

        private void GoToLine(int requestedLineNumber)
        {
            var lineNumber = Math.Max(1, Math.Min(GetLineCountForDialog(), requestedLineNumber));
            _editor.GoToLine(lineNumber);
            _statusLabel.Text = string.Format(
                UiLocalization.Text("AppServerFilePreviewForm.Status.Line.Template", "Line {0}."),
                lineNumber);
        }

        private void MarkContentDirtyAfterProgrammaticEdit()
        {
            if (_canEdit)
            {
                _isDirty = true;
            }
        }
    }
}
