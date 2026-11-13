using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // Preparation and reconnection use the same target transition lease as other runtime changes.
    internal sealed class BrowserControlOptionsCoordinator : IDisposable
    {
        private readonly Form _owner;
        private readonly IAppServerClient _client;
        private readonly IBrowserExtensionSetupService _service;
        private readonly Func<IDisposable> _tryTransition;
        private readonly Func<Task<bool>> _reconnect;
        private readonly Action<string> _status;
        private readonly Func<Func<Task>, Task> _runPreparation;
        private readonly System.Windows.Forms.Timer _retry = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private bool _pending;
        private bool _enabled;
        private bool _needsReconnect;
        private bool _applying;
        private bool _disposed;
        private bool _silent;
        private string _failure;

        internal BrowserControlOptionsCoordinator(Form owner, IAppServerClient client, IBrowserExtensionSetupService service,
            Func<IDisposable> tryTransition, Func<Task<bool>> reconnect, Action<string> status,
            Func<Func<Task>, Task> runPreparation = null)
        {
            _owner = owner;
            _client = client;
            _service = service;
            _tryTransition = tryTransition;
            _reconnect = reconnect;
            _status = status;
            _runPreparation = runPreparation ?? RunPreparationAsync;
            _retry.Tick += async (sender, args) => await TryApplyAsync();
            _owner.FormClosed += (sender, args) => Dispose();
        }

        internal async Task<BrowserControlOptionsPage> CreatePageAsync(bool? enabled)
        {
            var supported = _client.ProtocolCapabilities?.SupportsBrowserUseIntegration == true;
            if (!enabled.HasValue && supported)
            {
                try
                {
                    var plugins = await _client.GetInstalledPluginsAsync(_closing.Token);
                    var servers = await _client.ListMcpServerStatusesAsync();
                    enabled = plugins.Plugins.Any(plugin => plugin.Installed && plugin.Enabled &&
                        (plugin.PluginId == "browser-use@mycodex-browser-use" || plugin.PluginId == "chrome@openai-bundled" ||
                            plugin.PluginId == "unified-computer-use@openai-bundled")) ||
                        servers.Any(server => server.ServerName == "cua_repl" && server.Enabled != false);
                }
                catch { supported = false; }
            }
            return new BrowserControlOptionsPage(_service, supported, enabled == true);
        }

        // Silent applies (startup restore) show no wait dialog or message box; status text carries the outcome.
        internal async Task ApplyAsync(bool enabled, bool settingChanged, bool silent = false)
        {
            if (_disposed) return;
            _silent = silent;
            _enabled = enabled;
            _needsReconnect |= settingChanged;
            _pending = enabled || _needsReconnect;
            if (!enabled && _client is IBrowserControlRuntime runtime)
            {
                try { await runtime.RevokeBrowserControlAsync(); }
                catch (Exception)
                {
                    _needsReconnect = _pending = true;
                    var message = UiLocalization.Text("BrowserControl.StopFailed", "Browser control is disabled in settings, but stopping an existing browser operation could not be confirmed. Stop the current browser task. MyCodex will apply the change when current work finishes.");
                    _status(message);
                    MessageBox.Show(_owner, message, UiLocalization.Text("BrowserControl.Title", "Browser Control"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            await TryApplyAsync();
        }

        private async Task TryApplyAsync()
        {
            if (_disposed || !_pending || _applying) return;
            using (var lease = _tryTransition())
            {
                if (lease == null)
                {
                    _retry.Start();
                    if (!_silent) _status(UiLocalization.Text("BrowserControl.Pending", "Browser control setting saved. Waiting for current work to finish before applying configuration."));
                    return;
                }
                _retry.Stop();
                _applying = true;
                _pending = false;
                var stage = "prepare";
                var changed = false;
                var silent = _silent;
                Func<Task> operation = async () =>
                {
                    if (_enabled) _needsReconnect |= await _service.PrepareAsync(_closing.Token);
                    _closing.Token.ThrowIfCancellationRequested();
                    if (_needsReconnect)
                    {
                        stage = "reconnect";
                        changed = true;
                        if (!await _reconnect()) throw new InvalidOperationException("BROWSER_RECONNECT_FAILED");
                    }
                    _needsReconnect = false;
                };
                try
                {
                    await (silent ? operation() : _runPreparation(operation));
                    _failure = null;
                    if (!silent || changed) _status(_enabled
                        ? UiLocalization.Text("BrowserControl.Prepared", "Browser control configured. Install or enable the browser extension if needed. If the browser was already open, fully exit and reopen it once.")
                        : UiLocalization.Text("BrowserControl.Disabled", "Browser control is off."));
                }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    if (!_disposed)
                    {
                        // Report a bounded stage/code, never raw command lines or configuration.
                        var code = error is AuthenticationCenterException busy && busy.Code == "runtime_busy" ? "runtime_busy" : "unspecified";
                        System.Diagnostics.Trace.TraceWarning("[BrowserControl] Setup failed. Stage={0}; ExceptionType={1}; Code={2}", stage, error.GetType().Name, code);
                        var message = UiLocalization.Text("BrowserControl.SetupFailed", "Browser control setup did not finish. Check that the MyCodex installation is complete, the runtime is connected and browser control is permitted by your settings. Open Options > Browser Control and select OK to retry.");
                        _failure = message;
                        _status(message);
                        if (!silent) MessageBox.Show(_owner, message, UiLocalization.Text("BrowserControl.Title", "Browser Control"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                finally
                {
                    _applying = false;
                    // An apply requested while this one ran (no wait dialog blocks Options) must not be lost.
                    if (_pending && !_disposed) _retry.Start();
                }
            }
        }

        // Later startup status messages can replace a silent failure; repeat it once startup finishes.
        internal void RepeatFailureStatus()
        {
            if (!_disposed && _failure != null) _status(_failure);
        }

        private Task RunPreparationAsync(Func<Task> operation)
        {
            using (var wait = new RuntimeSwitchWaitForm(
                UiLocalization.Text("BrowserControl.Title", "Browser Control"),
                UiLocalization.Text("BrowserControl.Preparing", "Preparing browser control and applying the saved setting..."), operation))
            {
                wait.ShowDialog(_owner);
                if (wait.OperationException != null) throw wait.OperationException;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _retry.Dispose();
            _closing.Cancel();
            _closing.Dispose();
        }
    }
}
