using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerAccountTokenUsageMapper
    {
        internal static AppServerAccountTokenUsageSnapshot BuildSnapshot(IDictionary<string, object> result)
        {
            var snapshot = new AppServerAccountTokenUsageSnapshot();
            var summary = AppServerJsonValue.GetDictionary(result, "summary");
            snapshot.LifetimeTokens = GetNullableLong(summary, "lifetimeTokens");
            snapshot.PeakDailyTokens = GetNullableLong(summary, "peakDailyTokens");
            snapshot.LongestRunningTurnSeconds = GetNullableLong(summary, "longestRunningTurnSec");
            snapshot.CurrentStreakDays = GetNullableLong(summary, "currentStreakDays");
            snapshot.LongestStreakDays = GetNullableLong(summary, "longestStreakDays");

            foreach (var item in GetArray(result, "dailyUsageBuckets"))
            {
                var bucket = item as IDictionary<string, object>;
                if (bucket == null)
                {
                    continue;
                }

                var startDate = AppServerJsonValue.GetString(bucket, "startDate");
                var tokens = GetNullableLong(bucket, "tokens");
                if (string.IsNullOrWhiteSpace(startDate) || !tokens.HasValue)
                {
                    continue;
                }

                snapshot.DailyBuckets.Add(new AppServerAccountTokenUsageDailyBucket
                {
                    StartDate = startDate,
                    Tokens = tokens.Value
                });
            }

            return snapshot;
        }

        private static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            if (value is long)
            {
                return (long)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is short)
            {
                return (short)value;
            }

            if (value is byte)
            {
                return (byte)value;
            }

            if (value is decimal)
            {
                return Convert.ToInt64((decimal)value, CultureInfo.InvariantCulture);
            }

            if (value is double)
            {
                return Convert.ToInt64((double)value, CultureInfo.InvariantCulture);
            }

            if (value is float)
            {
                return Convert.ToInt64((float)value, CultureInfo.InvariantCulture);
            }

            long result;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? (long?)result
                : null;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }
}
