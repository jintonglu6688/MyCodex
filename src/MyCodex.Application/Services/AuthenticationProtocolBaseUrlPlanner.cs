using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    // Providers often serve each upstream format at a different endpoint, such as
    // DeepSeek's Anthropic format under /anthropic. When the form's base URL is one of
    // a preset's endpoints, a format change moves it to that preset's endpoint for the
    // new format. Custom URLs, unknown formats and ambiguous matches stay unchanged.
    public static class AuthenticationProtocolBaseUrlPlanner
    {
        public static string Resolve(string baseUrl, string kind, AuthenticationProviderPreset template,
            IEnumerable<AuthenticationProviderPreset> presets)
        {
            var current = Normalize(baseUrl);
            if (current == null || string.IsNullOrEmpty(kind)) return null;
            if (Serves(template, current)) return Target(template, kind, current);
            var targets = (presets ?? Enumerable.Empty<AuthenticationProviderPreset>())
                .Where(preset => Serves(preset, current))
                .Select(preset => Target(preset, kind, current))
                .ToList();
            if (targets.Count == 0 || targets.Any(target => target == null)) return null;
            var distinct = targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        private static bool Serves(AuthenticationProviderPreset preset, string url)
        {
            return preset?.ProtocolBaseUrls != null && preset.ProtocolBaseUrls.Values
                .Any(value => string.Equals(Normalize(value), url, StringComparison.OrdinalIgnoreCase));
        }

        private static string Target(AuthenticationProviderPreset preset, string kind, string current)
        {
            if (!preset.ProtocolBaseUrls.TryGetValue(kind, out var value)) return null;
            var target = Normalize(value);
            return target == null || string.Equals(target, current, StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
        }

        private static string Normalize(string url)
        {
            var text = url?.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
