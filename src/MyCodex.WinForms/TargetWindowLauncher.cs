using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms
{
    internal sealed class TargetWindowLauncher
    {
        private readonly TargetCatalogStore _catalog;
        private readonly string _channelId;
        private readonly string _sharedRoot;
        private readonly Action<string[]> _startProcess;
        private readonly int _startupWaitMilliseconds;

        public TargetWindowLauncher(TargetCatalogStore catalog, string channelId, string sharedRoot)
            : this(catalog, channelId, sharedRoot, StartProcess, 15000)
        {
        }

        internal TargetWindowLauncher(TargetCatalogStore catalog, string channelId, string sharedRoot,
            Action<string[]> startProcess, int startupWaitMilliseconds = 15000)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
            _sharedRoot = sharedRoot ?? throw new ArgumentNullException(nameof(sharedRoot));
            _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
            if (startupWaitMilliseconds < 1 || startupWaitMilliseconds > 30000) throw new ArgumentOutOfRangeException(nameof(startupWaitMilliseconds));
            _startupWaitMilliseconds = startupWaitMilliseconds;
        }

        public async Task<TargetInstanceReply> OpenAsync(string targetId, string workspacePath = null)
        {
            TargetStartupSelection selection;
            try
            {
                selection = await Task.Run(() => ValidateSelection(targetId, workspacePath)).ConfigureAwait(false);
                var reservation = await Task.Run(() => TargetInstanceCoordinator.TryReserveOfflineEdit(
                    _channelId, selection.Target.Id, _sharedRoot)).ConfigureAwait(false);
                if (reservation == null)
                {
                    var activation = await Task.Run(() => TargetInstanceCoordinator.Send(_channelId, selection.Target.Id, _sharedRoot,
                        new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate, WorkspacePath = selection.WorkspacePath }, 3000)).ConfigureAwait(false);
                    if (activation.Success) await RememberOpenedAsync(selection.Target.Id).ConfigureAwait(false);
                    return activation;
                }
                // Startup must acquire the same gate. A competing launch can win, but only one owner can open a window.
                reservation.Dispose();
            }
            catch
            {
                return TargetInstanceReply.Failure("The selected target or its saved data cannot be verified. Check it in the target manager.");
            }

            try
            {
                var arguments = new List<string> { "--channel", _channelId, "--target", selection.Target.Id };
                if (!string.IsNullOrWhiteSpace(selection.WorkspacePath)) arguments.AddRange(new[] { "--workspace", selection.WorkspacePath });
                await Task.Run(() => _startProcess(arguments.ToArray())).ConfigureAwait(false);
            }
            catch
            {
                return TargetInstanceReply.Failure("The selected target could not be started. Its saved data was kept.");
            }

            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < _startupWaitMilliseconds)
            {
                var reply = await Task.Run(() => TargetInstanceCoordinator.Send(_channelId, selection.Target.Id, _sharedRoot,
                    new TargetInstanceRequest { Operation = TargetInstanceOperation.Status }, 500)).ConfigureAwait(false);
                if (reply.Success && selection.Target.Kind == "Windows" && reply.Status?.IsPreparingBundledRuntime == true)
                    elapsed.Restart();
                if (reply.Success && reply.Status != null && reply.Status.Lifecycle == TargetInstanceLifecycle.Running)
                {
                    try { await RememberOpenedAsync(selection.Target.Id).ConfigureAwait(false); }
                    catch { return TargetInstanceReply.Failure("The target opened, but the last-opened target could not be saved."); }
                    return reply;
                }
                if (reply.Success && reply.Status?.Lifecycle == TargetInstanceLifecycle.Closing)
                    // The window we just opened can now exit before initialization finishes.
                    // Keep cancellation distinct from success so callers cannot hand work to it.
                    throw new OperationCanceledException("The newly opened target was closed during startup.");
                if (!reply.Success && reply.Instance != null) return reply;
                await Task.Delay(100).ConfigureAwait(false);
            }
            return TargetInstanceReply.Failure("The selected target has not confirmed startup. Check its window before trying again.");
        }

        public async Task<TargetInstanceReply> SaveAsync(string targetId, TargetRuntimeSettings settings)
        {
            if (settings == null) return TargetInstanceReply.Failure("Runtime settings are required.");
            try
            {
                var edit = CloneSettings(settings);
                return await Task.Run(() =>
                {
                    var selection = ValidateSelection(targetId, null);
                    using (var reservation = TargetInstanceCoordinator.TryReserveOfflineEdit(_channelId, selection.Target.Id, _sharedRoot))
                    {
                        if (reservation == null)
                        {
                            if (selection.Target.Kind == "Ssh" && !TargetRuntimeSettingsPlanner.IsSameSshHost(
                                _catalog.LoadRuntimeSettings(selection.Target.Id).RemoteSsh.HostName, edit.RemoteSsh?.HostName))
                                return TargetInstanceReply.Failure(UiLocalization.Text("TargetSettingsForm.Error.HostRequiresClosed",
                                    "Close this target before changing its SSH host. Your draft is retained."));
                            // Never replay an edit after timeout: its outcome may be unknown while the owner asks for confirmation.
                            var timeout = selection.Target.Kind == "Windows" &&
                                MyCodex.Shared.Constants.CodexRuntimeSources.IsBundledFamily(edit.CodexRuntimeSource)
                                ? 15 * 60 * 1000 : 30000;
                            return TargetInstanceCoordinator.Send(_channelId, selection.Target.Id, _sharedRoot,
                                new TargetInstanceRequest { Operation = TargetInstanceOperation.ApplyRuntimeSettings, RuntimeSettings = edit }, timeout);
                        }
                        _catalog.SaveRuntimeSettings(selection.Target.Id, edit);
                        return new TargetInstanceReply { Success = true, SettingsSaved = true };
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return TargetInstanceReply.Failure(Forms.TargetManagerForm.FormatSettingsError(ex));
            }
        }

        private TargetStartupSelection ValidateSelection(string targetId, string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentException("An explicit target is required.", nameof(targetId));
            var selection = TargetStartupPlanner.Resolve(_catalog.Load(), targetId, workspacePath);
            _catalog.GetPaths(selection.Target.Id);
            return selection;
        }

        private Task RememberOpenedAsync(string id)
        {
            return Task.Run(() => _catalog.RememberOpened(id));
        }

        private static TargetRuntimeSettings CloneSettings(TargetRuntimeSettings settings)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(TargetRuntimeSettings));
                serializer.WriteObject(stream, settings);
                stream.Position = 0;
                return (TargetRuntimeSettings)serializer.ReadObject(stream);
            }
        }

        private static void StartProcess(string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = System.Windows.Forms.Application.ExecutablePath,
                Arguments = string.Join(" ", arguments.Select(WindowsCommandLineArgumentEscaper.Quote)),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Normal
            };
            using (var process = Process.Start(startInfo))
                if (process == null) throw new InvalidOperationException("The target process was not started.");
        }
    }
}
