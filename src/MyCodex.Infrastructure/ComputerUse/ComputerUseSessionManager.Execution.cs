using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed partial class ComputerUseSessionManager
    {
        public async Task WarmupAsync(CancellationToken token)
        {
            try
            {
                await _prepare.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_disposed || _components != null) return;
                    var config = await ReadConfigAsync(null, token).ConfigureAwait(false);
                    if (!ComputerUseConfigurationPlanner.IsPluginDisabled(config))
                        await LoadComponentsAsync(token).ConfigureAwait(false);
                }
                finally { _prepare.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Trace.TraceInformation("[ComputerUse] Warmup deferred. ExceptionType={0}", error.GetType().Name); }
        }

        private async Task<string> LoadComponentsAsync(CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(_codexPath) || !Path.IsPathRooted(_codexPath) || !File.Exists(_codexPath))
                return "SelectedRuntimeMissing";
            var watch = Stopwatch.StartNew();
            _bundle = await Task.Run(() => ComputerUseBundle.Load(), token).ConfigureAwait(false);
            var resolution = await Task.Run(() => new ComputerUseComponentResolver().Resolve(
                _codexPath, _codexVersion, _bundle.RuntimeDirectory), token).ConfigureAwait(false);
            Trace.TraceInformation("[ComputerUse] Components verified. Status={0}; ElapsedMs={1}", resolution.Status, watch.ElapsedMilliseconds);
            if (resolution.Status != ComputerUseComponentStatus.Ready) return resolution.Status.ToString();
            _components = resolution.Components;
            return null;
        }

        private ComputerUseHostProcess CreateHost()
        {
            Directory.CreateDirectory(_homeRoot);
            ComputerUseHostProcess host = null;
            host = new ComputerUseHostProcess(_components, _homeRoot, _report,
                (thread, turn, token) => PrepareExecutionAsync(host, thread, turn, token));
            return host;
        }

        private async Task PrepareExecutionAsync(ComputerUseHostProcess host, string thread, string turn, CancellationToken token)
        {
            ComputerUseSessionBinding binding;
            CancellationToken turnToken;
            lock (_sync)
            {
                if (_disposed || !_threads.TryGetValue(thread, out binding)) throw new OperationCanceledException(token);
            }
            lock (binding.Sync)
            {
                if (binding.Host != host || binding.Turn != turn || binding.TurnLifetime == null)
                    throw new OperationCanceledException(token);
                turnToken = binding.TurnLifetime.Token;
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, turnToken))
            {
                token = linked.Token;
                await _prepare.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var watch = Stopwatch.StartNew();
                    var config = await ReadConfigAsync(binding.Workspace, token, binding.TrustOverrides).ConfigureAwait(false);
                    var requirements = await ReadRequirementsAsync(token).ConfigureAwait(false);
                    if (ComputerUseConfigurationPlanner.IsPluginDisabled(config) ||
                        !await _pluginPreparation.EnsureAsync(_bundle, _codexHome, token, false, config).ConfigureAwait(false))
                    { _report("PluginDisabled", thread); throw new InvalidOperationException("CUA_PLUGIN_DISABLED"); }
                    var plan = ComputerUseConfigurationPlanner.Plan(config, requirements, null, _components, binding.Pipe, _codexHome);
                    if (!plan.IsAvailable)
                    { _report(plan.DetailCode, thread); throw new InvalidOperationException("CUA_POLICY_UNAVAILABLE"); }
                    var policy = new JavaScriptSerializer().Serialize(plan.HelperPolicy);
                    if (binding.WrittenPolicy != policy || !File.Exists(Path.Combine(binding.HelperHome, "config.toml")))
                    {
                        await ComputerUsePolicyWriter.WriteAsync(_codexPath, binding.HelperHome, plan.HelperPolicy, token).ConfigureAwait(false);
                        binding.WrittenPolicy = policy;
                    }
                    token.ThrowIfCancellationRequested();
                    Trace.TraceInformation("[ComputerUse] Execution prepared. ElapsedMs={0}", watch.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    if (error.Message != "CUA_PLUGIN_DISABLED" && error.Message != "CUA_POLICY_UNAVAILABLE")
                        _report("PreparationFailed", thread);
                    Trace.TraceWarning("[ComputerUse] Execution preparation failed. ExceptionType={0}", error.GetType().Name);
                    throw;
                }
                finally { _prepare.Release(); }
            }
        }

        private static void CancelPreparation(ComputerUseSessionBinding binding)
        {
            binding.TurnLifetime?.Cancel();
            binding.TurnLifetime?.Dispose();
            binding.TurnLifetime = null;
        }
    }
}
