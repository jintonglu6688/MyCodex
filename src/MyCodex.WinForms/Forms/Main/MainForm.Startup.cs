using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly CancellationTokenSource _targetShutdownCancellation = new CancellationTokenSource();
        private TaskCompletionSource<bool> _targetExitDecision;

        private bool IsTargetStopping => _isExitRequested || IsDisposed || Disposing;

        // UI-thread checkpoints keep pending reads from changing saved state while
        // exit is prepared. Cancellation resumes those reads; commit discards them.
        private async Task<bool> WaitForTargetExitDecisionAsync()
        {
            while (!IsTargetStopping && _targetExitDecision != null)
                await _targetExitDecision.Task;
            return !IsTargetStopping;
        }

        private void PauseTargetInitialization()
        {
            if (_targetExitDecision == null)
                _targetExitDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void ResumeTargetInitialization()
        {
            var decision = _targetExitDecision;
            _targetExitDecision = null;
            decision?.TrySetResult(true);
        }

        private void StopTargetInitialization()
        {
            _targetShutdownCancellation.Cancel();
            _authenticationCaptureCancellation?.Cancel();
            ResumeTargetInitialization();
        }
    }
}
