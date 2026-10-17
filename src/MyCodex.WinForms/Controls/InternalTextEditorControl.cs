using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;
using ScintillaNET;

namespace MyCodex.WinForms.Controls
{
    internal sealed class InternalTextEditorControl : UserControl
    {
        private readonly Scintilla _editor;
        private bool _isProgrammaticUpdate;

        public InternalTextEditorControl(string accessibleName)
        {
            AccessibleName = accessibleName ?? string.Empty;
            Dock = DockStyle.Fill;
            TabStop = true;

            _editor = new Scintilla
            {
                AccessibleName = accessibleName ?? string.Empty,
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateCodeFont(UiTheme.TitleFontSize, FontStyle.Regular),
                TabIndex = 0
            };
            _editor.TextChanged += Editor_TextChanged;
            Controls.Add(_editor);
        }

        public event EventHandler ContentChangedByUser;

        public string ContentText
        {
            get { return _editor.Text ?? string.Empty; }
        }

        public bool ReadOnly
        {
            get { return _editor.ReadOnly; }
            set { _editor.ReadOnly = value; }
        }

        public string SelectedText
        {
            get { return _editor.SelectedText ?? string.Empty; }
        }

        public int SelectionStart
        {
            get { return Math.Min(_editor.SelectionStart, _editor.SelectionEnd); }
        }

        public int TextLength
        {
            get { return _editor.TextLength; }
        }

        public int CurrentLineNumber
        {
            get { return _editor.LineFromPosition(_editor.CurrentPosition) + 1; }
        }

        public int LineCount
        {
            get { return Math.Max(1, _editor.Lines.Count); }
        }

        internal string EditorControlTypeNameForTests
        {
            get { return GetType().Name; }
        }

        internal string InnerEditorControlTypeNameForTests
        {
            get { return _editor.GetType().Name; }
        }

        internal string InnerAccessibleNameForTests
        {
            get { return _editor.AccessibleName; }
        }

        internal bool InnerTabStopForTests
        {
            get { return _editor.TabStop; }
        }

        internal int InnerTabIndexForTests
        {
            get { return _editor.TabIndex; }
        }

        internal string FocusedAccessibleNameForTests
        {
            get
            {
                return _editor.Focused || ContainsFocus
                    ? _editor.AccessibleName
                    : string.Empty;
            }
        }

        public void SetContentText(string text)
        {
            _isProgrammaticUpdate = true;
            var wasReadOnly = _editor.ReadOnly;
            try
            {
                if (wasReadOnly)
                {
                    _editor.ReadOnly = false;
                }

                _editor.Text = text ?? string.Empty;
            }
            finally
            {
                _editor.ReadOnly = wasReadOnly;
                _isProgrammaticUpdate = false;
            }
        }

        public bool FindText(string query, bool forward)
        {
            if (string.IsNullOrEmpty(query))
            {
                return false;
            }

            _editor.SearchFlags = SearchFlags.None;
            var start = forward
                ? Math.Max(_editor.SelectionStart, _editor.SelectionEnd)
                : Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
            var found = SearchInRange(query, start, forward ? _editor.TextLength : 0);
            if (found < 0)
            {
                found = SearchInRange(query, forward ? 0 : _editor.TextLength, start);
            }

            if (found < 0)
            {
                _editor.Focus();
                return false;
            }

            SelectTarget(found, _editor.TargetEnd);
            return true;
        }

        public bool ReplaceCurrent(string query, string replacement)
        {
            if (ReadOnly || string.IsNullOrEmpty(query))
            {
                return false;
            }

            if (!string.Equals(SelectedText, query, StringComparison.Ordinal) && !FindText(query, true))
            {
                return false;
            }

            var start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
            var end = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
            var replacementText = replacement ?? string.Empty;
            _editor.SetTargetRange(start, end);
            _editor.ReplaceTarget(replacementText);
            SelectTarget(start, start + replacementText.Length);
            return true;
        }

        public int ReplaceAll(string query, string replacement)
        {
            if (ReadOnly || string.IsNullOrEmpty(query))
            {
                return 0;
            }

            var replacementText = replacement ?? string.Empty;
            var replacements = 0;
            var searchStart = 0;
            _editor.SearchFlags = SearchFlags.None;
            while (searchStart <= _editor.TextLength)
            {
                _editor.SetTargetRange(searchStart, _editor.TextLength);
                var found = _editor.SearchInTarget(query);
                if (found < 0)
                {
                    break;
                }

                _editor.ReplaceTarget(replacementText);
                replacements++;
                searchStart = found + replacementText.Length;
            }

            _editor.Focus();
            return replacements;
        }

        public void GoToLine(int requestedLineNumber)
        {
            var lineNumber = Math.Max(1, Math.Min(LineCount, requestedLineNumber));
            var line = _editor.Lines[lineNumber - 1];
            _editor.GotoPosition(line.Position);
            _editor.ScrollCaret();
            _editor.Focus();
        }

        public void FocusEditor()
        {
            _editor.Focus();
        }

        protected override void OnEnter(EventArgs e)
        {
            base.OnEnter(e);
            FocusEditor();
        }

        private int SearchInRange(string query, int start, int end)
        {
            _editor.SetTargetRange(start, end);
            return _editor.SearchInTarget(query);
        }

        private void SelectTarget(int start, int end)
        {
            _editor.SelectionStart = start;
            _editor.SelectionEnd = end;
            _editor.ScrollCaret();
            _editor.Focus();
        }

        private void Editor_TextChanged(object sender, EventArgs e)
        {
            if (!_isProgrammaticUpdate && !ReadOnly)
            {
                var handler = ContentChangedByUser;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }
    }
}
