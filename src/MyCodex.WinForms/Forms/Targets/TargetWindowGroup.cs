using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal interface IRuntimeTransitionWindow
    {
        bool IsBusyForRuntimeTransition { get; }
    }

    // A target has one UI thread. Capture its group when a form is constructed,
    // including modeless forms that deliberately have no WinForms Owner.
    internal sealed class TargetWindowGroup : IDisposable
    {
        [ThreadStatic] private static TargetWindowGroup _current;
        private static readonly ConditionalWeakTable<Form, TargetWindowGroup> Groups = new ConditionalWeakTable<Form, TargetWindowGroup>();
        private static readonly ConditionalWeakTable<Form, object> Prepared = new ConditionalWeakTable<Form, object>();
        private readonly Dictionary<Form, WindowTitle> _windows = new Dictionary<Form, WindowTitle>();
        private readonly Form _main;
        private readonly Func<string> _targetName;
        private readonly TargetWindowGroup _previous;
        private TargetNativeDialogTracker _nativeDialogs;
        private bool _disposed;

        internal TargetWindowGroup(Form main, Func<string> targetName)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));
            _targetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
            _previous = _current;
            _current = this;
            Register(main);
        }

        internal void TrackNativeDialogs()
        {
            if (_nativeDialogs == null) _nativeDialogs = new TargetNativeDialogTracker(this);
        }

        internal static void Prepare(Form form)
        {
            if (form == null || Prepared.TryGetValue(form, out _)) return;
            Prepared.Add(form, new object());
            var constructedGroup = _current;
            form.Load += (sender, args) =>
            {
                var group = Find(form) ?? constructedGroup ?? _current;
                group?.Register(form);
            };
        }

        internal static TargetWindowGroup Find(Form form)
        {
            for (var current = form; current != null; current = current.Owner)
            {
                if (Groups.TryGetValue(current, out var group)) return group;
            }
            return null;
        }

        internal void Register(Form form)
        {
            if (_disposed || form == null || form.IsDisposed || _windows.ContainsKey(form)) return;
            if (Groups.TryGetValue(form, out var existing) && !ReferenceEquals(existing, this))
                throw new InvalidOperationException("A window cannot change execution target.");
            Groups.Add(form, this);
            var title = new WindowTitle { BaseText = form.Text };
            _windows.Add(form, title);
            form.TextChanged += OnTextChanged;
            form.Disposed += OnDisposed;
            ApplyTitle(form, title);
        }

        internal IReadOnlyList<Form> GetForms(bool includeShared = false)
        {
            return _windows.Keys.Where(form => !ReferenceEquals(form, _main) && !form.IsDisposed &&
                !form.Disposing && (includeShared || !IsShared(form))).ToArray();
        }

        internal Form FindModal()
        {
            return GetForms(true).LastOrDefault(form => form.Modal && form.Visible && form.Enabled &&
                form.IsHandleCreated && TargetNativeDialogTracker.IsWindowEnabled(form.Handle));
        }

        internal bool HasNativeModal => _nativeDialogs != null && _nativeDialogs.HasModal;

        internal bool TryActivateModal()
        {
            if (_nativeDialogs != null && _nativeDialogs.TryActivateModal()) return true;
            var modal = FindModal();
            if (modal == null) return false;
            modal.Activate();
            modal.BringToFront();
            return true;
        }

        internal void RefreshTitles()
        {
            foreach (var pair in _windows.ToArray()) ApplyTitle(pair.Key, pair.Value);
            _nativeDialogs?.RefreshTitles();
        }

        internal string PrefixFor(Form form)
        {
            // The phone service editor supplies its compact title with a target suffix.
            if (form is RemoteServiceSettingsForm settings && settings.UsesSharedServiceSettings) return null;
            return IsShared(form) ? UiLocalization.Text("Window.Targets.All", "All targets") : _targetName();
        }

        private static bool IsShared(Form form)
        {
            for (var current = form; current != null; current = current.Owner)
                if (current is TargetManagerForm || current is TargetExitAllForm) return true;
            return false;
        }

        private void OnTextChanged(object sender, EventArgs args)
        {
            var form = (Form)sender;
            var title = _windows[form];
            if (string.Equals(form.Text, title.AppliedText, StringComparison.Ordinal)) return;
            title.BaseText = form.Text;
            ApplyTitle(form, title);
        }

        private void ApplyTitle(Form form, WindowTitle title)
        {
            // The main composer preserves its established status/name/app/mode ordering.
            if (ReferenceEquals(form, _main)) return;
            var prefix = PrefixFor(form);
            // Existing file-browser/main composers already supply this same prefix.
            if (!string.IsNullOrEmpty(prefix) && title.BaseText.StartsWith(prefix.Trim() + " - ", StringComparison.Ordinal))
                title.BaseText = title.BaseText.Substring(prefix.Trim().Length + 3);
            title.AppliedText = MainFormWindowTitleComposer.AddTargetPrefix(title.BaseText, prefix);
            form.Text = title.AppliedText;
        }

        private void OnDisposed(object sender, EventArgs args)
        {
            var form = (Form)sender;
            form.TextChanged -= OnTextChanged;
            form.Disposed -= OnDisposed;
            _windows.Remove(form);
            Groups.Remove(form);
            if (ReferenceEquals(form, _main)) Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _nativeDialogs?.Dispose();
            _nativeDialogs = null;
            foreach (var form in _windows.Keys.ToArray())
            {
                form.TextChanged -= OnTextChanged;
                form.Disposed -= OnDisposed;
                Groups.Remove(form);
            }
            _windows.Clear();
            if (ReferenceEquals(_current, this)) _current = _previous != null && !_previous._disposed ? _previous : null;
        }

        private sealed class WindowTitle
        {
            internal string BaseText;
            internal string AppliedText;
        }
    }
}
