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

        private async Task<IDictionary<string, object>> PrepareBrowserAsync(ComputerUseSessionBinding binding,
            IDictionary<string, object> config, IDictionary<string, object> requirements,
            IDictionary<string, object> overrides, CancellationToken token)
        {
            var allocated = false;
            try
            {
                if (_browserComponents == null)
                    _browserComponents = await Task.Run(() => BrowserUseRuntimeBundle.Load(_codexPath), token).ConfigureAwait(false);
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
                        return plan.IsAvailable && binding.Host == host && binding.Turn == turn &&
                            binding.TurnLifetime != null && !binding.TurnLifetime.IsCancellationRequested &&
                            binding.BrowserAccess?.ExecutorId == executor;
                    }
                }
                finally { _prepare.Release(); }
            }
        }
    }
}
