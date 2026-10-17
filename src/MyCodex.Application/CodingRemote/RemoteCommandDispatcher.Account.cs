using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private async Task<RemoteRelayEvent> DispatchHostAccountGetAsync(
            RemoteRelayCommand command,
            CancellationToken cancellationToken)
        {
            if (_accountClient == null)
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.OperationNotAvailable,
                    "User Center is unavailable.");
            }

            var authentication = await _accountClient
                .GetAuthenticationStateAsync(false)
                .ConfigureAwait(false);
            authentication = authentication ?? new AppServerAuthenticationState();
            var isChatGpt = authentication.IsAuthenticated &&
                !string.Equals(authentication.AuthMode, "apikey", StringComparison.OrdinalIgnoreCase);
            var rateLimits = isChatGpt
                ? await _accountClient.GetRateLimitsAsync().ConfigureAwait(false)
                : new AppServerRateLimitsSnapshot();
            AppServerAccountTokenUsageSnapshot tokenUsage = null;
            if (isChatGpt)
            {
                try
                {
                    tokenUsage = await _accountClient
                        .GetAccountTokenUsageAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    tokenUsage = null;
                }
            }

            return CreateEvent(
                command,
                RemoteEventTypes.HostAccountResult,
                MapHostAccount(authentication, rateLimits, tokenUsage, isChatGpt));
        }

        private static Dictionary<string, object> MapHostAccount(
            AppServerAuthenticationState authentication,
            AppServerRateLimitsSnapshot rateLimits,
            AppServerAccountTokenUsageSnapshot tokenUsage,
            bool isChatGpt)
        {
            rateLimits = rateLimits ?? new AppServerRateLimitsSnapshot();
            var planType = authentication.PlanType;
            if (string.IsNullOrWhiteSpace(planType))
            {
                foreach (var bucket in rateLimits.Buckets)
                {
                    if (bucket != null && !string.IsNullOrWhiteSpace(bucket.PlanType))
                    {
                        planType = bucket.PlanType;
                        break;
                    }
                }
            }

            var buckets = new List<Dictionary<string, object>>();
            foreach (var bucket in rateLimits.Buckets)
            {
                if (bucket == null)
                {
                    continue;
                }

                buckets.Add(new Dictionary<string, object>
                {
                    { "id", bucket.LimitId },
                    { "name", bucket.LimitName },
                    { "primary", MapRateLimitWindow(bucket.Primary) },
                    { "secondary", MapRateLimitWindow(bucket.Secondary) },
                    { "credits", MapCredits(bucket.Credits) }
                });
            }

            var resetCredits = new List<Dictionary<string, object>>();
            foreach (var credit in rateLimits.ResetCredits ?? new List<AppServerRateLimitResetCredit>())
            {
                if (credit == null)
                {
                    continue;
                }

                resetCredits.Add(new Dictionary<string, object>
                {
                    { "status", credit.Status },
                    { "expiresAtUtc", FormatUtc(credit.ExpiresAtLocalTime ?? default(DateTime)) }
                });
            }

            return new Dictionary<string, object>
            {
                { "available", isChatGpt },
                { "email", authentication.Email },
                { "planType", planType },
                { "resetCreditsAvailableCount", rateLimits.RateLimitResetCreditsAvailableCount },
                { "resetCredits", resetCredits },
                { "buckets", buckets },
                { "tokenUsage", MapTokenUsage(tokenUsage) }
            };
        }

        private static Dictionary<string, object> MapTokenUsage(AppServerAccountTokenUsageSnapshot usage)
        {
            if (usage == null)
            {
                return null;
            }

            var dailyBuckets = new List<Dictionary<string, object>>();
            foreach (var bucket in usage.DailyBuckets)
            {
                if (bucket != null)
                {
                    dailyBuckets.Add(new Dictionary<string, object>
                    {
                        { "startDate", bucket.StartDate },
                        { "tokens", bucket.Tokens }
                    });
                }
            }

            return new Dictionary<string, object>
            {
                { "lifetimeTokens", usage.LifetimeTokens },
                { "peakDailyTokens", usage.PeakDailyTokens },
                { "currentStreakDays", usage.CurrentStreakDays },
                { "longestStreakDays", usage.LongestStreakDays },
                { "dailyBuckets", dailyBuckets }
            };
        }

        private static Dictionary<string, object> MapRateLimitWindow(AppServerRateLimitWindowInfo window)
        {
            if (window == null)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                { "usedPercent", window.UsedPercent },
                { "durationMinutes", window.WindowDurationMinutes },
                { "resetsAtUtc", FormatUtc(window.ResetsAtLocalTime ?? default(DateTime)) }
            };
        }

        private static Dictionary<string, object> MapCredits(AppServerCreditsInfo credits)
        {
            if (credits == null)
            {
                return null;
            }

            return new Dictionary<string, object>
            {
                { "hasCredits", credits.HasCredits },
                { "unlimited", credits.Unlimited },
                { "balance", credits.Balance }
            };
        }
    }
}
