using System.Threading.Tasks;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class AppServerFilePreviewForm
    {
        private bool _targetExitPrepared;
        private bool _targetExitPreparing;
        private string _targetExitContent;

        internal bool HasUnsavedTargetExitContent => _isDirty;
        internal bool CanCommitTargetExit => IsClosingOrDisposed || (_targetExitPrepared && _targetExitContent == _editor.ContentText);

        internal async Task<bool> PrepareForTargetExitAsync()
        {
            if (IsClosingOrDisposed) return true;
            if (_isLoading || _closePromptInProgress || _targetExitPreparing) return false;
            _targetExitPreparing = true;
            RefreshPreviewState();
            try
            {
                if (_isDirty)
                {
                    var choice = _confirmCloseChanges();
                    if (!_targetExitPreparing || choice == AppServerFilePreviewCloseChoice.Cancel) return false;
                    if (choice == AppServerFilePreviewCloseChoice.Save && !await SaveAsync(true)) return false;
                    // Discard is consent for commit only. Cancelling another target must retain this draft.
                }
                if (IsClosingOrDisposed) return true;
                if (!_targetExitPreparing) return false;
                _targetExitPrepared = true;
                _targetExitContent = _editor.ContentText;
                return true;
            }
            finally
            {
                _targetExitPreparing = false;
                RefreshPreviewState();
            }
        }

        internal void CancelTargetExit()
        {
            if (IsClosingOrDisposed || (!_targetExitPrepared && !_targetExitPreparing)) return;
            _targetExitPreparing = false;
            _targetExitPrepared = false;
            _targetExitContent = null;
            _editor.ReadOnly = !_canEdit;
            RefreshPreviewState();
        }

        internal void CommitTargetExit()
        {
            if (IsClosingOrDisposed || !CanCommitTargetExit) return;
            _closeWithoutDirtyPrompt = true;
            Close();
        }
    }
}
