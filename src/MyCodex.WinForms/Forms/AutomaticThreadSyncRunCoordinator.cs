namespace MyCodex.WinForms.Forms
{
    internal sealed class AutomaticThreadSyncRunCoordinator
    {
        private bool _hasPendingRequest;
        private bool _pendingReportNoNewThreads;
        private bool _pendingForceFullThreadScan;

        internal bool IsRunning { get; private set; }

        internal bool TryBegin(bool canRun, bool reportNoNewThreads, bool forceFullThreadScan)
        {
            if (IsRunning)
            {
                RequestPending(reportNoNewThreads, forceFullThreadScan);
                return false;
            }

            if (!canRun)
            {
                return false;
            }

            IsRunning = true;
            return true;
        }

        internal bool CompleteAndTryConsumePending(out bool reportNoNewThreads, out bool forceFullThreadScan)
        {
            IsRunning = false;
            if (!_hasPendingRequest)
            {
                reportNoNewThreads = false;
                forceFullThreadScan = false;
                return false;
            }

            reportNoNewThreads = _pendingReportNoNewThreads;
            forceFullThreadScan = _pendingForceFullThreadScan;
            _hasPendingRequest = false;
            _pendingReportNoNewThreads = false;
            _pendingForceFullThreadScan = false;
            return true;
        }

        private void RequestPending(bool reportNoNewThreads, bool forceFullThreadScan)
        {
            _hasPendingRequest = true;
            _pendingReportNoNewThreads = _pendingReportNoNewThreads || reportNoNewThreads;
            _pendingForceFullThreadScan = _pendingForceFullThreadScan || forceFullThreadScan;
        }
    }
}
