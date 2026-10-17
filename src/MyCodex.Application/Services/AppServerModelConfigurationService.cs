using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public sealed class AppServerModelConfigurationObservation
    {
        public string Scope { get; set; }
        public AppServerConfigSnapshot GlobalConfig { get; set; }
        public AppServerConfigSnapshot WorkspaceConfig { get; set; }
        public AppServerConfigRequirementsSnapshot Requirements { get; set; }
        public IReadOnlyList<AppServerModelOption> Models { get; set; }
    }

    // Reads effective configuration and writes only explicitly selected official defaults.
    public sealed class AppServerModelConfigurationService
    {
        private readonly IAppServerConfigClient _client;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Task<AppServerModelConfigurationObservation>> _pending =
            new Dictionary<string, Task<AppServerModelConfigurationObservation>>(StringComparer.Ordinal);
        private readonly Dictionary<string, AppServerModelConfigurationObservation> _observations =
            new Dictionary<string, AppServerModelConfigurationObservation>(StringComparer.Ordinal);
        private int _generation;
        // ponytail: serialize reads per client; split per connection if independent clients share this service.
        private readonly SemaphoreSlim _readLock = new SemaphoreSlim(1, 1);

        public AppServerModelConfigurationService(IAppServerConfigClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public AppServerModelConfigurationObservation GetObserved(string scope, string workspacePath)
        {
            lock (_sync)
            {
                AppServerModelConfigurationObservation result;
                return _observations.TryGetValue(Key(scope, workspacePath), out result) ? result : null;
            }
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                _generation++;
                _observations.Clear();
                _pending.Clear();
            }
        }

        public async Task WriteAsync(AppServerConfigSnapshot observedConfig, string modelId, string reasoningEffort)
        {
            var edits = OfficialCodexSettingsEditPlanner.BuildModelEdits(observedConfig, modelId, reasoningEffort);
            if (edits.Count == 0)
            {
                return;
            }
            var guard = AppServerConfigWriteGuard.EvaluateUserConfig(observedConfig, edits);
            if (!guard.CanWrite)
            {
                throw new InvalidOperationException("The official configuration version could not be verified.");
            }
            await _client.WriteConfigBatchAsync(edits, expectedVersion: guard.ExpectedVersion, reloadUserConfig: true).ConfigureAwait(false);
            Invalidate();
        }

        public Task<AppServerModelConfigurationObservation> RefreshAsync(
            string scope, string workspacePath, bool supportsRequirements)
        {
            var key = Key(scope, workspacePath);
            lock (_sync)
            {
                Task<AppServerModelConfigurationObservation> task;
                if (!_pending.TryGetValue(key, out task))
                {
                    task = ReadAsync(scope, workspacePath, supportsRequirements, key, _generation);
                    _pending.Add(key, task);
                }
                return task;
            }
        }

        private async Task<AppServerModelConfigurationObservation> ReadAsync(
            string scope, string workspacePath, bool supportsRequirements, string key, int generation)
        {
            await Task.Yield();
            await _readLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var global = await _client.ReadConfigAsync(null, true).ConfigureAwait(false);
                var workspace = string.IsNullOrWhiteSpace(workspacePath) ? global :
                    await _client.ReadConfigAsync(workspacePath, true).ConfigureAwait(false);
                if (global == null || workspace == null)
                {
                    throw new InvalidOperationException("App Server did not return model configuration.");
                }

                var previous = GetObserved(scope, null);
                var sameConfig = previous != null && SameConfiguration(previous.GlobalConfig, global);
                var models = sameConfig ? previous.Models : await _client.ListModelsAsync().ConfigureAwait(false);
                var requirements = sameConfig ? previous.Requirements :
                    supportsRequirements ? await _client.ReadConfigRequirementsAsync().ConfigureAwait(false) : null;
                var observation = new AppServerModelConfigurationObservation
                {
                    Scope = scope,
                    GlobalConfig = global,
                    WorkspaceConfig = workspace,
                    Models = models ?? new List<AppServerModelOption>(),
                    Requirements = requirements
                };
                lock (_sync)
                {
                    if (generation != _generation)
                    {
                        throw new OperationCanceledException("The execution target configuration changed.");
                    }
                    if (!sameConfig)
                    {
                        foreach (var staleKey in _observations.Where(pair => pair.Value.Scope == scope).Select(pair => pair.Key).ToArray())
                        {
                            _observations.Remove(staleKey);
                        }
                    }
                    _observations[Key(scope, null)] = new AppServerModelConfigurationObservation
                    {
                        Scope = scope, GlobalConfig = global, WorkspaceConfig = global,
                        Models = observation.Models, Requirements = requirements
                    };
                    _observations[key] = observation;
                }
                return observation;
            }
            catch
            {
                lock (_sync)
                {
                    if (generation == _generation)
                    {
                        foreach (var staleKey in _observations.Where(pair => pair.Value.Scope == scope).Select(pair => pair.Key).ToArray())
                        {
                            _observations.Remove(staleKey);
                        }
                    }
                }
                throw;
            }
            finally
            {
                _readLock.Release();
                lock (_sync)
                {
                    if (generation == _generation)
                    {
                        _pending.Remove(key);
                    }
                }
            }
        }

        private static string Key(string scope, string workspacePath)
        {
            // Preserve Unix path case. Native Windows paths are normalized by the workspace layer.
            return (scope ?? string.Empty) + "\n" + (workspacePath ?? string.Empty);
        }

        private static bool SameConfiguration(AppServerConfigSnapshot left, AppServerConfigSnapshot right)
        {
            return left.ModelId == right.ModelId && left.ModelReasoningEffort == right.ModelReasoningEffort &&
                left.ModelProviderId == right.ModelProviderId && left.ReviewModelId == right.ReviewModelId && left.Layers.Count > 0 &&
                left.Layers.All(layer => layer != null && !string.IsNullOrWhiteSpace(layer.Version)) &&
                right.Layers.All(layer => layer != null) &&
                left.Layers.Select(LayerIdentity).SequenceEqual(right.Layers.Select(LayerIdentity), StringComparer.Ordinal);
        }

        private static string LayerIdentity(AppServerConfigLayer layer)
        {
            return string.Join("\n", layer.SourceType, layer.SourceText, layer.Version, layer.DisabledReason);
        }
    }
}
