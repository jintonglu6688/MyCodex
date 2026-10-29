using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.BrowserUse;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed class ComputerUseSessionBinding
    {
        public readonly object Sync = new object();
        public string Slot;
        public string Thread;
        public string Turn;
        public string Pipe;
        public string HelperHome;
        public string Workspace;
        public ComputerUseHostProcess Host;
        public IDictionary<string, object> TrustOverrides;
        public CancellationTokenSource TurnLifetime;
        public string WrittenPolicy;
        public BrowserUseAccessBinding BrowserAccess;
        public bool NativeAvailable = true;
    }

    internal sealed partial class ComputerUseSessionManager : IDisposable
    {
        private readonly string _codexPath;
        private readonly string _codexVersion;
        private readonly string _codexHome;
        private readonly Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> _send;
        private readonly Action<string, string> _report;
        private readonly SemaphoreSlim _prepare = new SemaphoreSlim(1, 1);
        private readonly object _sync = new object();
        private readonly Dictionary<string, ComputerUseSessionBinding> _threads = new Dictionary<string, ComputerUseSessionBinding>(StringComparer.Ordinal);
        private readonly string _homeRoot;
        private ComputerUseComponents _components;
        private ComputerUseBundle _bundle;
        private readonly ComputerUsePluginPreparation _pluginPreparation;
        private ComputerUseHostProcess _host;
        private bool _disposed;
        private int _preserveAuxiliaryHome;
        private readonly Action<ComputerUseSessionBinding, string, string> _sendControl;
        private readonly Func<bool> _browserEnabled;

        public ComputerUseSessionManager(string codexPath, string codexVersion, string codexHome,
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> send,
            Action<string, string> report,
            Action<ComputerUseSessionBinding, string, string> sendControl = null,
            Func<bool> browserEnabled = null)
        {
            _codexPath = codexPath;
            _codexVersion = codexVersion;
            _codexHome = codexHome;
            _send = send;
            _pluginPreparation = new ComputerUsePluginPreparation(send);
            _report = (code, thread) =>
            {
                if (code == "CleanupFailed") Interlocked.Exchange(ref _preserveAuxiliaryHome, 1);
                report?.Invoke(code, thread);
            };
            _sendControl = sendControl;
            _browserEnabled = browserEnabled ?? (() => true);
            _homeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                "MyCodex", "computer-use", Guid.NewGuid().ToString("N"));
        }

        public async Task<ComputerUseSessionBinding> PrepareAsync(IDictionary<string, object> parameters, CancellationToken token, bool isResume = false)
        {
            await _prepare.WaitAsync(token).ConfigureAwait(false);
            ComputerUseSessionBinding binding = null;
            var stage = "configuration";
            try
            {
                if (_disposed) throw new OperationCanceledException(token);
                var existingThread = AppServerJsonValue.GetString(parameters, "threadId");
                if (isResume && await IsLoadedAsync(existingThread, token).ConfigureAwait(false))
                {
                    bool owned;
                    lock (_sync) owned = _threads.ContainsKey(existingThread);
                    if (!owned) _report("ReconnectRequired", existingThread);
                    return null;
                }
                var workspace = AppServerJsonValue.GetString(parameters, "cwd");
                if (string.IsNullOrWhiteSpace(workspace))
                {
                    var parent = await _send("thread/read", new Dictionary<string, object>
                    {
                        { "threadId", AppServerJsonValue.GetString(parameters, "threadId") }, { "includeTurns", false }
                    }, token).ConfigureAwait(false);
                    workspace = AppServerJsonValue.GetString(AppServerJsonValue.GetDictionary(parent, "thread"), "cwd");
                }
                if (string.IsNullOrWhiteSpace(workspace)) return Unavailable("ConfigurationUnavailable");
                var overrides = AppServerJsonValue.GetDictionary(parameters, "config");
                var config = await ReadConfigAsync(workspace, token, overrides).ConfigureAwait(false);
                if (!_browserEnabled())
                {
                    overrides = BrowserUseConfigurationPlanner.SuppressBrowser(overrides);
                    parameters["config"] = overrides;
                }
                var browserRequested = _browserEnabled() && BrowserUseConfigurationPlanner.IsRequested(config, overrides);
                if (!browserRequested && (ComputerUseConfigurationPlanner.IsPluginDisabled(config) ||
                    ComputerUseConfigurationPlanner.IsPluginDisabled(overrides))) return Unavailable("PluginDisabled");
                var requirements = await ReadRequirementsAsync(token).ConfigureAwait(false);
                if (_components == null || (_host != null && !_host.IsAlive))
                {
                    stage = "components";
                    var failure = await LoadComponentsAsync(token).ConfigureAwait(false);
                    if (failure != null) return Unavailable(failure);
                }
                // Validate before starting any host or writing auxiliary policy.
                var plan = ComputerUseConfigurationPlanner.Plan(config, requirements, overrides, _components,
                    @"\\.\pipe\mycodex-cua-validation", _codexHome);
                var nativeAvailable = plan.IsAvailable;
                if (!nativeAvailable && !browserRequested) return Unavailable(plan.DetailCode);
                if (nativeAvailable)
                {
                    stage = "plugin";
                    nativeAvailable = await _pluginPreparation.EnsureAsync(_bundle, _codexHome, token, true, config).ConfigureAwait(false);
                    if (!nativeAvailable && !browserRequested) return Unavailable("PluginDisabled");
                }
                var helperHome = Path.Combine(_homeRoot, Guid.NewGuid().ToString("N"));
                if (_disposed) throw new OperationCanceledException(token);
                if (_host == null || !_host.IsAlive)
                {
                    StopHost(_host);
                    stage = "host";
                    var host = CreateHost();
                    lock (_sync)
                    {
                        if (_disposed) { StopHost(host); throw new OperationCanceledException(token); }
                        _host = host;
                    }
                    await host.WaitReadyAsync(token).ConfigureAwait(false);
                }
                var current = _host;
                stage = "pipe";
                var slot = await current.RequestAsync("allocate", new Dictionary<string, object> { { "helperHome", helperHome } }, token).ConfigureAwait(false) as IDictionary<string, object>;
                binding = new ComputerUseSessionBinding
                {
                    Slot = AppServerJsonValue.GetString(slot, "slot"), Pipe = AppServerJsonValue.GetString(slot, "pipePath"),
                    HelperHome = helperHome, Workspace = workspace, Host = current, TrustOverrides = overrides,
                    NativeAvailable = nativeAvailable
                };
                ComputerUsePipeSecurity.Restrict(binding.Pipe);
                await current.RequestAsync("publish", new Dictionary<string, object> { { "slot", binding.Slot } }, token).ConfigureAwait(false);
                var configured = overrides;
                if (nativeAvailable)
                {
                    plan = ComputerUseConfigurationPlanner.Plan(config, requirements, overrides, _components, binding.Pipe, _codexHome);
                    if (!plan.IsAvailable) throw new InvalidOperationException("CUA_CONFIGURATION_CHANGED");
                    configured = plan.ConfigOverrides;
                }
                parameters["config"] = browserRequested ? await PrepareBrowserAsync(binding, config, requirements,
                    configured, token).ConfigureAwait(false) : configured;
                if (!nativeAvailable && binding.BrowserAccess == null)
                {
                    Abandon(binding);
                    return Unavailable("BrowserUnavailable");
                }
                parameters["config"] = ComputerUseConfigurationPlanner.ApplyToolRouting(config,
                    AppServerJsonValue.GetDictionary(parameters, "config"), nativeAvailable, binding.BrowserAccess != null);
                return binding;
            }
            catch (OperationCanceledException) { Abandon(binding); throw; }
            catch (InvalidOperationException ex) when (ex.Message == "CUA_COMPONENT_CONTRACT")
            { Abandon(binding); return Unavailable("ComponentContractUnsupported"); }
            catch (DirectoryNotFoundException) { Abandon(binding); return Unavailable("MissingComponents"); }
            catch (InvalidDataException) { Abandon(binding); return Unavailable("InvalidComponents"); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Computer Use preparation failed. Stage={0}; Exception={1}; Code={2}",
                    stage, ex.GetType().Name, ex.Message.StartsWith("CUA_", StringComparison.Ordinal) &&
                        ex.Message.All(character => character == '_' || (character >= 'A' && character <= 'Z'))
                        ? ex.Message : "unspecified");
                Abandon(binding);
                return Unavailable("PreparationFailed");
            }
            finally { _prepare.Release(); }
        }

        public async Task BindAsync(ComputerUseSessionBinding binding, string thread, CancellationToken token)
        {
            if (binding == null) return;
            try
            {
                if (string.IsNullOrWhiteSpace(thread)) throw new InvalidOperationException("CUA_THREAD_MISSING");
                await binding.Host.RequestAsync("bind", new Dictionary<string, object>
                {
                    { "slot", binding.Slot }, { "thread", thread }
                }, token).ConfigureAwait(false);
                ComputerUseSessionBinding previous;
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(ComputerUseSessionManager));
                    _threads.TryGetValue(thread, out previous);
                    binding.Thread = thread;
                    _threads[thread] = binding;
                }
                Abandon(previous);
                _report("Registered", thread);
            }
            catch { Abandon(binding); _report("PreparationFailed", thread); }
        }

        public async Task BeforeTurnAsync(string thread, string workspace, CancellationToken token)
        {
            try { await BeforeTurnCoreAsync(thread, workspace, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Remove(thread); throw; }
            catch (InvalidOperationException ex) when (ex.Message == "CUA_COMPONENT_CONTRACT")
            { Remove(thread); _report("ComponentContractUnsupported", thread); }
            catch { Remove(thread); _report("PreparationFailed", thread); }
        }

        private async Task BeforeTurnCoreAsync(string thread, string workspace, CancellationToken token)
        {
            ComputerUseSessionBinding binding;
            lock (_sync) _threads.TryGetValue(thread, out binding);
            if (binding == null)
            {
                if (!string.IsNullOrWhiteSpace(workspace))
                {
                    var config = await ReadConfigAsync(workspace, token).ConfigureAwait(false);
                    if (ComputerUseConfigurationPlanner.IsPluginDisabled(config) &&
                        !BrowserUseConfigurationPlanner.IsRequested(config, null))
                    {
                        _report("PluginDisabled", thread);
                        return;
                    }
                }
                // A loaded thread ignores resume configuration overrides. Only a cold resume
                // may acquire a new pipe; do not misreport an unchanged loaded thread as ready.
                if (await IsLoadedAsync(thread, token).ConfigureAwait(false))
                {
                    _report("ReconnectRequired", thread);
                    return;
                }
                var resume = new Dictionary<string, object> { { "threadId", thread }, { "cwd", workspace ?? binding?.Workspace }, { "excludeTurns", true } };
                var replacement = await PrepareAsync(resume, token).ConfigureAwait(false);
                if (replacement == null) { Remove(thread); return; }
                try
                {
                    await _send("thread/resume", resume, token).ConfigureAwait(false);
                    await BindAsync(replacement, thread, token).ConfigureAwait(false);
                }
                catch { Abandon(replacement); Remove(thread); _report("PreparationFailed", thread); }
                return;
            }
            binding.Workspace = workspace ?? binding.Workspace;
            token.ThrowIfCancellationRequested();
            if (binding.Host.IsAlive) return;
            await _prepare.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!binding.Host.IsAlive)
                {
                    if (binding.BrowserAccess != null)
                    {
                        binding.BrowserAccess = null;
                        _report("BrowserReconnectRequired", thread);
                    }
                    // Official NativePipeTransport reconnects on the next request after EOF.
                    // Preserve its random address, but create a new slot with no active turn.
                    StopHost(binding.Host);
                    if (_host == null || !_host.IsAlive)
                    {
                        var failure = await LoadComponentsAsync(token).ConfigureAwait(false);
                        if (failure != null) { Remove(thread); _report(failure, thread); return; }
                        var replacement = CreateHost();
                        lock (_sync)
                        {
                            if (_disposed) { StopHost(replacement); throw new OperationCanceledException(token); }
                            _host = replacement;
                        }
                        await replacement.WaitReadyAsync(token).ConfigureAwait(false);
                    }
                    var slot = await _host.RequestAsync("allocate", new Dictionary<string, object>
                    {
                        { "helperHome", binding.HelperHome }, { "pipePath", binding.Pipe }
                    }, token).ConfigureAwait(false) as IDictionary<string, object>;
                    binding.Host = _host;
                    binding.Slot = AppServerJsonValue.GetString(slot, "slot");
                    ComputerUsePipeSecurity.Restrict(binding.Pipe);
                    await _host.RequestAsync("publish", new Dictionary<string, object> { { "slot", binding.Slot } }, token).ConfigureAwait(false);
                    await _host.RequestAsync("bind", new Dictionary<string, object> { { "slot", binding.Slot }, { "thread", thread } }, token).ConfigureAwait(false);
                }
            }
            finally { _prepare.Release(); }
        }

        public void Begin(string thread, string turn)
        {
            ComputerUseSessionBinding binding;
            lock (_sync)
            {
                if (_disposed || !_threads.TryGetValue(thread, out binding)) return;
            }
            lock (binding.Sync)
            {
                lock (_sync)
                {
                    ComputerUseSessionBinding current;
                    if (_disposed || !_threads.TryGetValue(thread, out current) || !ReferenceEquals(binding, current)) return;
                    if (binding.Turn == turn) return;
                    CancelPreparation(binding);
                    binding.TurnLifetime = new CancellationTokenSource();
                    binding.Turn = turn;
                }
                Control(binding, "begin", turn);
            }
        }

        public void End(string thread, string turn)
        {
            ComputerUseSessionBinding binding;
            lock (_sync)
            {
                if (thread == null || !_threads.TryGetValue(thread, out binding)) return;
            }
            lock (binding.Sync)
            {
                if (binding.Turn != turn) return;
                CancelPreparation(binding);
                binding.Turn = null;
                Control(binding, "end", turn);
            }
        }

        public void Remove(string thread)
        {
            ComputerUseSessionBinding binding;
            lock (_sync)
            {
                if (thread == null || !_threads.TryGetValue(thread, out binding)) return;
                _threads.Remove(thread);
            }
            Abandon(binding);
        }

        public void Abandon(ComputerUseSessionBinding binding)
        {
            if (binding != null)
                lock (binding.Sync) { CancelPreparation(binding); binding.Turn = null; Control(binding, "remove", null); }
        }

        private void Control(ComputerUseSessionBinding binding, string command, string turn)
        {
            try
            {
                if (_sendControl != null) { _sendControl(binding, command, turn); return; }
                binding.Host.RequestAsync(command, new Dictionary<string, object>
                {
                    { "slot", binding.Slot }, { "turn", turn }
                }).GetAwaiter().GetResult();
            }
            catch { StopHost(binding.Host); _report("ExecutionFailed", binding.Thread); }
        }

        private ComputerUseSessionBinding Unavailable(string code) { _report(code, null); return null; }

        private async Task<bool> IsLoadedAsync(string thread, CancellationToken token)
        {
            string cursor = null;
            do
            {
                var parameters = new Dictionary<string, object>();
                if (cursor != null) parameters["cursor"] = cursor;
                var page = await _send("thread/loaded/list", parameters, token).ConfigureAwait(false);
                object data;
                if (!page.TryGetValue("data", out data) || !(data is System.Collections.IEnumerable))
                    throw new InvalidOperationException("CUA_LOADED_THREADS_UNAVAILABLE");
                if (((System.Collections.IEnumerable)data).Cast<object>().OfType<string>().Contains(thread)) return true;
                cursor = AppServerJsonValue.GetString(page, "nextCursor");
            } while (!string.IsNullOrEmpty(cursor));
            return false;
        }

        private async Task<IDictionary<string, object>> ReadConfigAsync(string workspace, CancellationToken token,
            IDictionary<string, object> overrides = null)
        {
            var response = await _send("config/read", new Dictionary<string, object>
            {
                { "cwd", workspace }, { "includeLayers", true }
            }, token).ConfigureAwait(false);
            object value;
            if (!response.TryGetValue("config", out value) || !(value is IDictionary<string, object>))
                throw new InvalidOperationException("CUA_CONFIG_UNAVAILABLE");
            if (ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(response, overrides))
                throw new InvalidOperationException("CUA_UNSUPPORTED_PROJECT_POLICY");
            return (IDictionary<string, object>)value;
        }

        private async Task<IDictionary<string, object>> ReadRequirementsAsync(CancellationToken token)
        {
            var response = await _send("configRequirements/read", new Dictionary<string, object>(), token).ConfigureAwait(false);
            object value;
            if (!response.TryGetValue("requirements", out value) || (value != null && !(value is IDictionary<string, object>)))
                throw new InvalidOperationException("CUA_REQUIREMENTS_UNAVAILABLE");
            return value as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        public void Dispose()
        {
            ComputerUseHostProcess host;
            ComputerUseSessionBinding[] bindings;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                host = _host;
                bindings = _threads.Values.ToArray();
                _threads.Clear();
            }
            foreach (var binding in bindings) lock (binding.Sync) CancelPreparation(binding);
            StopHost(host);
            Task.Run(CleanupAuxiliaryHomeAsync).Forget("Computer Use auxiliary cleanup");
        }

        private void StopHost(ComputerUseHostProcess host)
        {
            if (host == null) return;
            try { host.Dispose(); }
            finally
            {
                // A prior host may still own an executor, even after _host was replaced.
                // Concurrent Dispose also returns without proving that cleanup finished.
                if (!host.IsCleanupConfirmed) Interlocked.Exchange(ref _preserveAuxiliaryHome, 1);
            }
        }

        private async Task CleanupAuxiliaryHomeAsync()
        {
            await _prepare.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _preserveAuxiliaryHome) == 0 && Directory.Exists(_homeRoot))
                    DeleteOwnedDirectory(_homeRoot);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { _prepare.Release(); }
        }

        private void DeleteOwnedDirectory(string directory)
        {
            var fullPath = Path.GetFullPath(directory);
            var root = Path.GetFullPath(_homeRoot);
            if ((!string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("CUA_CLEANUP_PATH_INVALID");
            foreach (var child in Directory.GetDirectories(fullPath)) DeleteOwnedDirectory(child);
            foreach (var file in Directory.GetFiles(fullPath)) File.Delete(file);
            Directory.Delete(fullPath, false);
        }
    }
}
