using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // Readable lines for provider usage results, one fact per line for list navigation.
    internal static class AuthenticationUsagePresentation
    {
        internal static readonly string[] Templates = { "token_plan", "balance", "newapi", "general", "sub2api", "custom" };

        // Native templates use the provider's own address and key.
        internal static bool IsNative(string template)
        {
            return template == "token_plan" || template == "balance";
        }

        internal static string TemplateName(string template)
        {
            switch (template)
            {
                case "token_plan": return UiLocalization.Text("AuthenticationUsage.Template.TokenPlan", "Coding plan usage");
                case "balance": return UiLocalization.Text("AuthenticationUsage.Template.Balance", "Account balance");
                case "newapi": return "NewAPI";
                case "general": return UiLocalization.Text("AuthenticationUsage.Template.General", "Generic balance endpoint");
                case "sub2api": return "Sub2API";
                case "custom": return UiLocalization.Text("AuthenticationUsage.Template.Custom", "Custom script");
                default: return template ?? string.Empty;
            }
        }

        internal static List<string> Lines(AuthenticationUsageResult result)
        {
            var lines = new List<string>();
            if (result == null || !result.Success)
            {
                lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Failed", "Query failed: {0}"),
                    result?.Error ?? UiLocalization.Text("ProviderUsage.Details.UnknownError", "unknown error")));
                return lines;
            }
            if (result.Items.Count == 0)
            {
                lines.Add(UiLocalization.Text("ProviderUsage.Details.NoData", "The provider returned no usage data."));
            }
            foreach (var item in result.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.PlanName))
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Plan", "Plan: {0}"), item.PlanName));
                if (item.IsValid == false)
                {
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Invalid", "Not valid: {0}"), item.InvalidMessage ?? string.Empty));
                    continue;
                }
                if (item.Remaining.HasValue)
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Remaining", "Remaining: {0}"), Amount(item.Remaining.Value, item.Unit)));
                if (item.Used.HasValue)
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Used", "Used: {0}"), Amount(item.Used.Value, item.Unit)));
                if (item.Total.HasValue)
                    lines.Add(string.Format(UiLocalization.Text("ProviderUsage.Details.Total", "Total: {0}"), Amount(item.Total.Value, item.Unit)));
                var extra = Extra(item.Extra);
                if (extra != null) lines.Add(extra);
            }
            return lines;
        }

        private static string Amount(decimal value, string unit)
        {
            var number = value.ToString("0.##", CultureInfo.CurrentCulture);
            if (string.IsNullOrWhiteSpace(unit)) return number;
            return unit == "%" ? number + "%" : number + " " + unit;
        }

        private static string Extra(string extra)
        {
            var text = extra?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                // Structured details (ZenMux) carry the reset time among amounts already shown.
                var match = Regex.Match(text, "\"resetsAt\"\\s*:\\s*\"([^\"]+)\"");
                if (!match.Success) return null;
                text = match.Groups[1].Value;
            }
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reset)
                ? string.Format(UiLocalization.Text("ProviderUsage.Details.Reset", "Resets: {0}"), reset.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : string.Format(UiLocalization.Text("ProviderUsage.Details.Extra", "Details: {0}"), text);
        }
    }
}
