namespace MyCodex.WinForms.Forms
{
    internal sealed class AutomaticThreadSyncFollowUpTracker
    {
        private readonly int _maxAttempts;
        private readonly int _firstDelayMilliseconds;
        private readonly int _retryDelayMilliseconds;
        private int _remainingAttempts;

        internal AutomaticThreadSyncFollowUpTracker(
            int maxAttempts,
            int firstDelayMilliseconds,
            int retryDelayMilliseconds)
        {
            _maxAttempts = maxAttempts > 0 ? maxAttempts : 0;
            _firstDelayMilliseconds = firstDelayMilliseconds > 0 ? firstDelayMilliseconds : 1;
            _retryDelayMilliseconds = retryDelayMilliseconds > 0 ? retryDelayMilliseconds : 1;
        }

        internal void RequestFollowUp()
        {
            _remainingAttempts = _maxAttempts;
        }

        internal bool TryConsumeNextDelay(out int delayMilliseconds)
        {
            if (_remainingAttempts <= 0)
            {
                delayMilliseconds = 0;
                return false;
            }

            delayMilliseconds = _remainingAttempts == _maxAttempts
                ? _firstDelayMilliseconds
                : _retryDelayMilliseconds;
            _remainingAttempts--;
            return true;
        }
    }
}
