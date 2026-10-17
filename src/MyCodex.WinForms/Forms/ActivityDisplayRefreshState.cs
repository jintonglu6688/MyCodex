namespace MyCodex.WinForms.Forms
{
    internal sealed class ActivityDisplayRefreshState
    {
        private bool _refreshPending;
        private string _lastRenderedText;

        internal bool RequestRefresh()
        {
            if (_refreshPending)
            {
                return false;
            }

            _refreshPending = true;
            return true;
        }

        internal bool ConsumeRefreshRequest()
        {
            if (!_refreshPending)
            {
                return false;
            }

            _refreshPending = false;
            return true;
        }

        internal bool HasPendingRefresh
        {
            get { return _refreshPending; }
        }

        internal bool ShouldRender(string text)
        {
            return !string.Equals(_lastRenderedText, text ?? string.Empty, System.StringComparison.Ordinal);
        }

        internal void MarkRendered(string text)
        {
            _lastRenderedText = text ?? string.Empty;
        }

        internal void ResetRenderedText()
        {
            _lastRenderedText = null;
        }
    }
}
