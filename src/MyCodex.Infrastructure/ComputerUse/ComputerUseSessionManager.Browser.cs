using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.BrowserUse;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed partial class ComputerUseSessionManager
    {
        private BrowserUseComponents _browserComponents;

        // Warmup and thread preparation both hold _prepare while publishing this cache.
        private async Task<BrowserUseComponents> LoadBrowserComponentsAsync(CancellationToken token)
        {
            if (_browserComponents != null) return _browserComponents;
            var watch = Stopwatch.StartNew();
            var components = await Task.Run(() => _loadBrowserComponents(_codexPath), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _browserComponents = components;
            Trace.TraceInformation("[BrowserUse] Components verified. ElapsedMs={0}", watch.ElapsedMilliseconds);
            return components;
        }

        private async Task<IDictionary<string, object>> PrepareBrowserAsync(ComputerUseSessionBinding binding,
            IDictionary<string, object> config, IDictionary<string, object> requirements,
            IDictionary<string, object> overrides, CancellationToken token)
        {
            var allocated = false;
            try
            {
                await LoadBrowserComponentsAsync(token).ConfigureAwait(false);
                var value = await binding.Host.RequestAsync("allocateBrowser", new Dictionary<string, object>
                {
                    { "slot", binding.Slot }
                }, token).ConfigureAwait(false) as IDictionary<string, object>;
                allocated = true;
                var access = new BrowserUseAccessBinding
                {
                    ExecutorId = AppServerJsonValue.GetString(value, "executorId"),
                    PipePath = AppServerJsonValue.GetString(value, "pipePath"),
                    VerifyingIdentity = AppServerJsonValue.GetString(value, "verifyingIdentity")
                };
                var plan = BrowserUseConfigurationPlanner.Plan(config, requirements, overrides,
                    _browserComponents, access, _codexHome);
                if (!plan.IsAvailable)
                {
                    _report(plan.DetailCode == "BrowserDisabled" || plan.DetailCode == "BrowserServerConflict"
                        ? plan.DetailCode : "BrowserUnavailable", null);
                    return overrides;
                }
                ComputerUsePipeSecurity.Restrict(access.PipePath);
                await binding.Host.RequestAsync("publishBrowser", new Dictionary<string, object>
                {
                    { "slot", binding.Slot }
                }, token).ConfigureAwait(false);
                if (!_browserEnabled()) return BrowserUseConfigurationPlanner.SuppressBrowser(config, overrides, _codexPath);
                binding.BrowserAccess = access;
                return plan.ConfigOverrides;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                Trace.TraceWarning("[BrowserUse] Preparation failed. ExceptionType={0}", error.GetType().Name);
                _report("BrowserUnavailable", null);
                return overrides;
            }
            finally
            {
                if (allocated && binding.BrowserAccess == null)
                    await binding.Host.RequestAsync("removeBrowser", new Dictionary<string, object>
                    {
                        { "slot", binding.Slot }
                    }, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> PrepareBrowserExecutionAsync(ComputerUseHostProcess host, string thread,
            string turn, string executor, CancellationToken token)
        {
            if (!_browserEnabled()) return false;
            ComputerUseSessionBinding binding;
            CancellationToken turnToken;
            lock (_sync)
            {
                if (_disposed || thread == null || !_threads.TryGetValue(thread, out binding)) return false;
            }
            lock (binding.Sync)
            {
                if (binding.Host != host || binding.Turn != turn || binding.TurnLifetime == null ||
                    binding.BrowserAccess == null || binding.BrowserAccess.ExecutorId != executor) return false;
                turnToken = binding.TurnLifetime.Token;
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, turnToken))
            {
                token = linked.Token;
                await _prepare.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var config = await ReadConfigAsync(binding.Workspace, token, binding.TrustOverrides).ConfigureAwait(false);
                    var requirements = await ReadRequirementsAsync(token).ConfigureAwait(false);
                    var plan = BrowserUseConfigurationPlanner.Plan(config, requirements, binding.TrustOverrides,
                        _browserComponents, binding.BrowserAccess, _codexHome);
                    token.ThrowIfCancellationRequested();
                    lock (binding.Sync)
                    {
                        return _browserEnabled() && plan.IsAvailable && binding.Host == host && binding.Turn == turn &&
                            binding.TurnLifetime != null && !binding.TurnLifetime.IsCancellationRequested &&
                            binding.BrowserAccess?.ExecutorId == executor;
                    }
                }
                finally { _prepare.Release(); }
            }
        }

        internal async Task RevokeBrowserAsync()
        {
            ComputerUseSessionBinding[] bindings;
            lock (_sync) bindings = new List<ComputerUseSessionBinding>(_threads.Values).ToArray();
            Exception failure = null;
            foreach (var binding in bindings)
            {
                string turn;
                BrowserUseAccessBinding access;
                lock (binding.Sync)
                {
                    if (binding.BrowserAccess == null) continue;
                    access = binding.BrowserAccess;
                    turn = binding.Turn;
                }
                try
                {
                    if (binding.Host != null)
                        await binding.Host.RequestAsync("removeBrowser", new Dictionary<string, object>
                        {
                            { "slot", binding.Slot }
                        }).ConfigureAwait(false);
                }
                catch
                {
                    // A host that cannot acknowledge revocation must not keep authorizing tools.
                    StopHost(binding.Host);
                    _report("BrowserUnavailable", binding.Thread);
                }
                if (turn != null)
                {
                    try
                    {
                        // The upstream service caches permission within a turn. Reset only its browser REPL.
                        var result = await _send("mcpServer/tool/call", new Dictionary<string, object>
                        {
                            { "threadId", binding.Thread }, { "server", "cua_repl" }, { "tool", "js_reset" },
                            { "arguments", new Dictionary<string, object>() },
                            { "_meta", new Dictionary<string, object>
                                {
                                    { "x-codex-turn-metadata", new Dictionary<string, object>
                                    {
                                        { "session_id", binding.Thread }, { "turn_id", turn }, { "session_context", "live" }
                                    }
                                    }
                                }
                            }
                        }, CancellationToken.None).ConfigureAwait(false);
                        if (result.TryGetValue("isError", out var failed) && Equals(failed, true))
                            throw new InvalidOperationException("BROWSER_REVOCATION_UNCONFIRMED");
                    }
                    catch (Exception error) { failure = error; continue; }
                }
                lock (binding.Sync)
                    if (ReferenceEquals(binding.BrowserAccess, access)) binding.BrowserAccess = null;
            }
            if (failure != null) throw new InvalidOperationException("BROWSER_REVOCATION_UNCONFIRMED", failure);
        }
    }
}
