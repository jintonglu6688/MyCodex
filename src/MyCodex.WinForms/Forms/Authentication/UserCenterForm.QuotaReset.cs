using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class UserCenterForm
    {
        private async Task ResetQuotaAsync()
        {
            if (_isRefreshing || _isResettingQuota)
            {
                return;
            }

            var selectedCredit = GetSelectedResetCreditChoice();
            if (!ConfirmResetQuota(selectedCredit == null ? null : selectedCredit.Credit))
            {
                SetStatusAndSpeak(UiLocalization.Text(
                    "UserCenterForm.Status.ResetQuotaCancelled",
                    "Quota reset cancelled."));
                return;
            }

            var actionName = UiLocalization.Text("UserCenterForm.Action.ResetQuota", "Reset quota");
            if (!await EnsureAppServerAvailableForActionAsync(actionName))
            {
                return;
            }

            _isResettingQuota = true;
            RefreshActionButtons();
            SetStatusAndSpeak(UiLocalization.Text("UserCenterForm.Status.ResettingQuota", "Resetting quota..."));

            try
            {
                var result = await _authenticationClient.ConsumeRateLimitResetCreditAsync(
                    Guid.NewGuid().ToString("D"),
                    selectedCredit == null ? null : selectedCredit.CreditId);
                await RefreshObservationsAfterResetAsync();
                RefreshDetailsList();
                RefreshResetCreditSelector();
                SetStatusAndSpeak(DescribeResetQuotaOutcome(result == null ? null : result.Outcome));
            }
            catch (Exception ex)
            {
                SetStatusAndSpeak(UiLocalization.Text("UserCenterForm.Status.ResetQuotaFailed", "Quota reset failed."));
                MessageBox.Show(
                    this,
                    ex.Message,
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _isResettingQuota = false;
                RefreshActionButtons();
                this.BeginInvokeSafe(FocusDetailsList);
            }
        }

        private async Task RefreshObservationsAfterResetAsync()
        {
            try
            {
                _rateLimitsSnapshot = await _authenticationClient.GetRateLimitsAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to refresh user center rate limits after reset: {0}", ex.Message);
                _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
            }

            try
            {
                _tokenUsageSnapshot = await _authenticationClient.GetAccountTokenUsageAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to refresh user center token usage after reset: {0}", ex.Message);
                _tokenUsageSnapshot = null;
            }
        }

        protected virtual bool ConfirmResetQuota(AppServerRateLimitResetCredit selectedCredit)
        {
            var prompt = BuildResetQuotaConfirmationPrompt(selectedCredit);
            return MessageBox.Show(
                this,
                prompt.Message,
                prompt.Title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private ResetQuotaConfirmationPrompt BuildResetQuotaConfirmationPrompt(
            AppServerRateLimitResetCredit selectedCredit)
        {
            var title = UiLocalization.Text("UserCenterForm.Action.ResetQuota", "Reset quota");
            var availableCount = _rateLimitsSnapshot == null
                ? null
                : _rateLimitsSnapshot.RateLimitResetCreditsAvailableCount;
            var countText = availableCount.HasValue
                ? Math.Max(0, availableCount.Value).ToString("N0", CultureInfo.CurrentCulture)
                : UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)");
            var message = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text(
                    "UserCenterForm.ResetQuota.Confirm.Message",
                    "Reset quota now?\r\n\r\nThis will consume 1 reset credit.\r\nCurrent remaining: {0}."),
                countText);
            if (selectedCredit != null)
            {
                var details = string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text(
                        "UserCenterForm.ResetCredit.Detail.Template",
                        "{0} ({1}), expires {2}. {3}"),
                    GetDisplayText(selectedCredit.Title, selectedCredit.Id),
                    GetDisplayText(
                        selectedCredit.ResetType,
                        UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)")),
                    FormatResetTime(selectedCredit.ExpiresAtLocalTime),
                    selectedCredit.Description ?? string.Empty);
                message += string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text(
                        "UserCenterForm.ResetQuota.Confirm.SelectedCredit.Template",
                        "\r\n\r\nSelected credit: {0}"),
                    details);
            }

            return new ResetQuotaConfirmationPrompt(title, message);
        }

        private void RefreshResetCreditSelector()
        {
            var selected = GetSelectedResetCreditChoice();
            var choices = BuildResetCreditChoices(
                _rateLimitsSnapshot,
                selected == null ? null : selected.CreditId,
                DateTime.Now,
                out var selectedIndex);

            _resetCreditComboBox.BeginUpdate();
            _resetCreditComboBox.Items.Clear();
            foreach (var choice in choices)
            {
                _resetCreditComboBox.Items.Add(choice);
            }

            _resetCreditComboBox.SelectedIndex = selectedIndex;
            _resetCreditComboBox.EndUpdate();
        }

        private ResetCreditChoice GetSelectedResetCreditChoice()
        {
            return _resetCreditComboBox.SelectedItem as ResetCreditChoice;
        }

        private static List<ResetCreditChoice> BuildResetCreditChoices(
            AppServerRateLimitsSnapshot snapshot,
            string selectedCreditId,
            DateTime now,
            out int selectedIndex)
        {
            var choices = new List<ResetCreditChoice>
            {
                new ResetCreditChoice(
                    null,
                    null,
                    UiLocalization.Text("UserCenterForm.ResetCredit.Automatic", "Automatic selection"))
            };
            if (snapshot != null && snapshot.ResetCredits != null)
            {
                foreach (var credit in snapshot.ResetCredits)
                {
                    if (credit == null ||
                        string.IsNullOrWhiteSpace(credit.Id) ||
                        !string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    choices.Add(new ResetCreditChoice(
                        credit.Id,
                        credit,
                        string.Format(
                            CultureInfo.CurrentCulture,
                            UiLocalization.Text(
                                "UserCenterForm.ResetCredit.Numbered.Template",
                                "Reset credit {0}: {1}"),
                            choices.Count,
                            FormatResetTimeWithCountdown(credit.ExpiresAtLocalTime, now))));
                }
            }

            selectedIndex = 0;
            for (var i = 1; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].CreditId, selectedCreditId, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }

            return choices;
        }

        private void RefreshResetQuotaButton()
        {
            var availableCount = _rateLimitsSnapshot == null
                ? null
                : _rateLimitsSnapshot.RateLimitResetCreditsAvailableCount;
            var count = Math.Max(0, availableCount.GetValueOrDefault());
            var isAvailable = IsChatGptAuthActive(_authenticationState) && availableCount.HasValue && count > 0;
            _resetQuotaButton.Text = isAvailable
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text(
                        "UserCenterForm.ResetQuota.Available.Template",
                        "Reset &quota ({0} left)"),
                    count)
                : UiLocalization.Text(
                    "UserCenterForm.ResetQuota.Unavailable",
                    "Reset &quota (unavailable)");
            _resetQuotaButton.AccessibleName = UiLocalization.Text(
                "UserCenterForm.Action.ResetQuota",
                "Reset quota");
            _resetQuotaButton.Enabled = isAvailable && !_isRefreshing && !_isResettingQuota;
            _resetCreditComboBox.Enabled = isAvailable && !_isRefreshing && !_isResettingQuota;
        }

        private static string DescribeResetQuotaOutcome(string outcome)
        {
            if (string.Equals(outcome, "reset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outcome, "alreadyRedeemed", StringComparison.OrdinalIgnoreCase))
            {
                return UiLocalization.Text(
                    "UserCenterForm.Status.ResetQuotaSucceeded",
                    "Quota reset completed.");
            }

            if (string.Equals(outcome, "nothingToReset", StringComparison.OrdinalIgnoreCase))
            {
                return UiLocalization.Text(
                    "UserCenterForm.Status.ResetQuotaNoWindow",
                    "No quota window can be reset right now.");
            }

            if (string.Equals(outcome, "noCredit", StringComparison.OrdinalIgnoreCase))
            {
                return UiLocalization.Text(
                    "UserCenterForm.Status.ResetQuotaNoCredit",
                    "No reset credits are available.");
            }

            return UiLocalization.Text(
                "UserCenterForm.Status.ResetQuotaUnknown",
                "Quota reset returned an unknown result.");
        }

        private sealed class ResetCreditChoice
        {
            public ResetCreditChoice(
                string creditId,
                AppServerRateLimitResetCredit credit,
                string displayText)
            {
                CreditId = creditId;
                Credit = credit;
                DisplayText = displayText;
            }

            public string CreditId { get; }

            public AppServerRateLimitResetCredit Credit { get; }

            public string DisplayText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private sealed class ResetQuotaConfirmationPrompt
        {
            public ResetQuotaConfirmationPrompt(string title, string message)
            {
                Title = title;
                Message = message;
            }

            public string Title { get; }

            public string Message { get; }
        }
    }
}
