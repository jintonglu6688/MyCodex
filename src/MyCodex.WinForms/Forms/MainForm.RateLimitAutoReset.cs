using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private const int RateLimitAutoResetCheckIntervalMilliseconds = 5 * 60 * 1000;

        private readonly RateLimitResetCreditAutoResetPlanner _rateLimitAutoResetPlanner =
            new RateLimitResetCreditAutoResetPlanner();
        private readonly HashSet<string> _rateLimitAutoResetAttemptedCreditIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _isRateLimitAutoResetRunning;
        private bool _isRateLimitAutoResetCheckScheduled;

        private async void RateLimitAutoResetTimer_Tick(object sender, EventArgs e)
        {
            await TryAutoResetExpiringRateLimitCreditAsync().ConfigureAwait(true);
        }

        private void UpdateRateLimitAutoResetState()
        {
            if (_rateLimitAutoResetTimer == null)
            {
                return;
            }

            if (_isPersistenceRecoveryPending ||
                !IsConnectedStatus() ||
                !IsChatGptAccountAuthenticationActive())
            {
                _rateLimitAutoResetTimer.Stop();
                return;
            }

            if (!_rateLimitAutoResetTimer.Enabled)
            {
                _rateLimitAutoResetTimer.Start();
            }

            ScheduleRateLimitAutoResetCheck();
        }

        private void ScheduleRateLimitAutoResetCheck()
        {
            if (_isRateLimitAutoResetCheckScheduled || IsDisposed || Disposing)
            {
                return;
            }

            _isRateLimitAutoResetCheckScheduled = this.BeginInvokeSafe(async () =>
            {
                _isRateLimitAutoResetCheckScheduled = false;
                await TryAutoResetExpiringRateLimitCreditAsync().ConfigureAwait(true);
            });
        }

        private async Task TryAutoResetExpiringRateLimitCreditAsync()
        {
            if (_isRateLimitAutoResetRunning ||
                _isPersistenceRecoveryPending ||
                !IsConnectedStatus() ||
                !IsChatGptAccountAuthenticationActive())
            {
                return;
            }

            _isRateLimitAutoResetRunning = true;
            try
            {
                var snapshot = await _appServerClient.GetRateLimitsAsync().ConfigureAwait(true);
                var credit = _rateLimitAutoResetPlanner.SelectCreditToAutoReset(
                    snapshot,
                    DateTime.Now,
                    _rateLimitAutoResetAttemptedCreditIds);
                if (credit == null)
                {
                    return;
                }

                var result = await _appServerClient.ConsumeRateLimitResetCreditAsync(
                    Guid.NewGuid().ToString("D"),
                    credit.Id).ConfigureAwait(true);
                _rateLimitAutoResetAttemptedCreditIds.Add(credit.Id);

                if (IsRateLimitAutoResetSuccess(result))
                {
                    SetLowPriorityStatus(UiLocalization.Text(
                        "MainForm.Status.RateLimitAutoResetSucceeded",
                        "Expiring reset credit was used automatically."));
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to auto-reset expiring rate limit credit: {0}", ex.Message);
            }
            finally
            {
                _isRateLimitAutoResetRunning = false;
            }
        }

        private static bool IsRateLimitAutoResetSuccess(AppServerRateLimitResetConsumeResult result)
        {
            return result != null &&
                   string.Equals(result.Outcome, "reset", StringComparison.OrdinalIgnoreCase);
        }

        private void DisposeRateLimitAutoResetTimerOnExit()
        {
            if (_rateLimitAutoResetTimer == null)
            {
                return;
            }

            _rateLimitAutoResetTimer.Stop();
            _rateLimitAutoResetTimer.Dispose();
        }
    }
}
