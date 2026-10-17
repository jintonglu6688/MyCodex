using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerWindowsSandboxMapper
    {
        internal static AppServerWindowsSandboxReadiness BuildReadiness(IDictionary<string, object> result)
        {
            var rawStatus = GetString(result, "status");
            return new AppServerWindowsSandboxReadiness
            {
                RawStatus = rawStatus,
                Status = MapReadinessStatus(rawStatus)
            };
        }

        internal static AppServerWindowsSandboxSetupStartResult BuildSetupStartResult(IDictionary<string, object> result)
        {
            return new AppServerWindowsSandboxSetupStartResult
            {
                Started = GetBool(result, "started")
            };
        }

        internal static AppServerWindowsSandboxSetupCompletedEvent BuildSetupCompleted(IDictionary<string, object> parameters)
        {
            var rawMode = GetString(parameters, "mode");
            return new AppServerWindowsSandboxSetupCompletedEvent
            {
                RawMode = rawMode,
                Mode = MapSetupMode(rawMode),
                Success = GetBool(parameters, "success"),
                Error = GetString(parameters, "error")
            };
        }

        internal static string MapSetupMode(AppServerWindowsSandboxSetupMode mode)
        {
            return mode == AppServerWindowsSandboxSetupMode.Elevated ? "elevated" : "unelevated";
        }

        private static AppServerWindowsSandboxReadinessStatus MapReadinessStatus(string rawStatus)
        {
            switch ((rawStatus ?? string.Empty).Trim())
            {
                case "ready":
                    return AppServerWindowsSandboxReadinessStatus.Ready;
                case "notConfigured":
                    return AppServerWindowsSandboxReadinessStatus.NotConfigured;
                case "updateRequired":
                    return AppServerWindowsSandboxReadinessStatus.UpdateRequired;
                default:
                    return AppServerWindowsSandboxReadinessStatus.Unknown;
            }
        }

        private static AppServerWindowsSandboxSetupMode MapSetupMode(string rawMode)
        {
            return string.Equals(rawMode, "elevated", StringComparison.Ordinal)
                ? AppServerWindowsSandboxSetupMode.Elevated
                : AppServerWindowsSandboxSetupMode.Unelevated;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            object value;
            bool result;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value != null &&
                bool.TryParse(Convert.ToString(value), out result) &&
                result;
        }
    }
}
