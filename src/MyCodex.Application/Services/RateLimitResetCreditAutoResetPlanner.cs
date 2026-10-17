using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class RateLimitResetCreditAutoResetPlanner
    {
        public static readonly TimeSpan DefaultExpiryThreshold = TimeSpan.FromMinutes(30);

        public AppServerRateLimitResetCredit SelectCreditToAutoReset(
            AppServerRateLimitsSnapshot snapshot,
            DateTime now,
            ISet<string> attemptedCreditIds)
        {
            if (snapshot == null || snapshot.ResetCredits == null)
            {
                return null;
            }

            AppServerRateLimitResetCredit selected = null;
            foreach (var credit in snapshot.ResetCredits)
            {
                if (!IsEligible(credit, now, attemptedCreditIds))
                {
                    continue;
                }

                if (selected == null || credit.ExpiresAtLocalTime.Value < selected.ExpiresAtLocalTime.Value)
                {
                    selected = credit;
                }
            }

            return selected;
        }

        private static bool IsEligible(
            AppServerRateLimitResetCredit credit,
            DateTime now,
            ISet<string> attemptedCreditIds)
        {
            if (credit == null ||
                string.IsNullOrWhiteSpace(credit.Id) ||
                !string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) ||
                !credit.ExpiresAtLocalTime.HasValue)
            {
                return false;
            }

            if (ContainsAttemptedCredit(attemptedCreditIds, credit.Id))
            {
                return false;
            }

            var remaining = credit.ExpiresAtLocalTime.Value - now;
            return remaining > TimeSpan.Zero && remaining <= DefaultExpiryThreshold;
        }

        private static bool ContainsAttemptedCredit(ISet<string> attemptedCreditIds, string creditId)
        {
            if (attemptedCreditIds == null)
            {
                return false;
            }

            foreach (var attemptedCreditId in attemptedCreditIds)
            {
                if (string.Equals(attemptedCreditId, creditId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
