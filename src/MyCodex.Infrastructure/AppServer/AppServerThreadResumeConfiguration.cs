using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerThreadResumeConfiguration
    {
        internal static async Task<IDictionary<string, object>> SendAsync(
            AppServerRequestInvoker send, IDictionary<string, object> parameters,
            CancellationToken cancellationToken, int timeoutMs,
            Func<string, int, TimeoutException> timeoutFactory)
        {
            var watch = Stopwatch.StartNew();
            Func<string, IDictionary<string, object>, Task<IDictionary<string, object>>> request = (method, values) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = timeoutMs - watch.ElapsedMilliseconds;
                if (remaining <= 0) throw timeoutFactory("thread/resume", timeoutMs);
                return send(method, values, cancellationToken, (int)remaining);
            };
            var overrides = AppServerJsonValue.GetDictionary(parameters, "config");
            if (!parameters.ContainsKey("modelProvider") && !overrides.ContainsKey("model_provider"))
            {
                var cwd = AppServerJsonValue.GetString(parameters, "cwd");
                if (string.IsNullOrWhiteSpace(cwd))
                {
                    var thread = await request("thread/read", new Dictionary<string, object>
                    {
                        { "threadId", AppServerJsonValue.GetString(parameters, "threadId") }, { "includeTurns", false }
                    }).ConfigureAwait(false);
                    cwd = AppServerJsonValue.GetString(AppServerJsonValue.GetDictionary(thread, "thread"), "cwd");
                    if (string.IsNullOrWhiteSpace(cwd)) throw ConfigurationUnavailable();
                }
                var read = new Dictionary<string, object> { { "cwd", cwd }, { "includeLayers", false } };
                var result = await request("config/read", read).ConfigureAwait(false);
                if (!result.TryGetValue("config", out var value) || !(value is IDictionary<string, object> config))
                    throw ConfigurationUnavailable();
                // Native resume otherwise keeps the provider saved in the old
                // session. Omission in current config means the built-in provider.
                var provider = "openai";
                if (config.TryGetValue("model_provider", out var selected) && selected != null)
                {
                    provider = selected as string;
                    if (string.IsNullOrWhiteSpace(provider)) throw ConfigurationUnavailable();
                }
                parameters = new Dictionary<string, object>(parameters)
                {
                    ["modelProvider"] = provider
                };
            }
            return await request("thread/resume", parameters).ConfigureAwait(false);
        }

        private static InvalidOperationException ConfigurationUnavailable()
        {
            return new InvalidOperationException(SharedLocalization.Text("AppServerClient.Error.ResumeConfigUnavailable",
                "The current provider configuration could not be read. The conversation was not resumed. Reconnect and try again."));
        }
    }
}
