using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using State = MyCodex.Infrastructure.AuthenticationCenter.AuthenticationInstallationStateStore.State;
using Target = MyCodex.Infrastructure.AuthenticationCenter.AuthenticationInstallationStateStore.Target;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed class AuthenticationInstallationMaintenance
    {
        private readonly AuthenticationInstallationStateStore _store;
        private readonly Func<List<Target>> _findRunning;
        private readonly Func<List<Target>> _findRegistered;
        private readonly Func<Target, bool, CancellationToken, Task> _stop;
        private readonly Func<Target, bool, CancellationToken, Task> _restore;
        private readonly Action _clearRegistrations;

        internal AuthenticationInstallationMaintenance(string root, string statePath,
            Func<List<Target>> findRunning = null, Func<List<Target>> findRegistered = null,
            Func<Target, bool, CancellationToken, Task> stop = null,
            Func<Target, bool, CancellationToken, Task> restore = null, Action clearRegistrations = null)
        {
            _store = new AuthenticationInstallationStateStore(root, statePath);
            _findRunning = findRunning ?? (() => AuthenticationInstallationProcesses.Find(_store.Root));
            _findRegistered = findRegistered ?? (() => AuthenticationHostStartup.ReadInstallationTargets(_store.Root));
            _stop = stop ?? StopAsync;
            _restore = restore ?? ((target, older, token) => Host(target).MaintainInstallationAsync(
                stop: false, uninstall: false, startIfMissing: true, allowOlderRelease: older, token: token));
            _clearRegistrations = clearRegistrations ?? (() => AuthenticationHostStartup.ClearInstallation(_store.Root));
        }

        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 1;
            if (args == null || args.Length == 0 || args[0] != "--authentication-maintenance") return false;
            if (args.Length != 4 || !new[] { "Prepare", "Finalize", "Resume", "Uninstall" }.Contains(args[1], StringComparer.Ordinal)) return true;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    exitCode = new AuthenticationInstallationMaintenance(args[2], args[3]).RunAsync(args[1], timeout.Token).GetAwaiter().GetResult();
            }
            catch { /* Fail closed even if the state path cannot be initialized; never print arguments. */ }
            return true;
        }

        internal async Task<int> RunAsync(string action, CancellationToken token)
        {
            _store.Log(action, "started");
            try
            {
                if (action == "Prepare" || action == "Uninstall") await PrepareAsync(action == "Uninstall", token).ConfigureAwait(false);
                else if (action == "Finalize" || action == "Resume") return await RestoreAsync(action, token).ConfigureAwait(false);
                else throw new AuthenticationCenterException("authentication_update_invalid_action");
                _store.Log(action, "completed");
                return 0;
            }
            catch (Exception error)
            {
                _store.Log(action, ErrorCode(error));
                return 1;
            }
        }

        private async Task PrepareAsync(bool uninstall, CancellationToken token)
        {
            var running = _findRunning();
            var previous = _store.Read();
            var state = new State { installRoot = _store.Root, targets = new List<Target>(running) };
            if (previous != null && !previous.completed) Merge(state.targets, previous.targets);
            if (uninstall) Merge(state.targets, _findRegistered());
            var valid = AuthenticationInstallationStateStore.IsPackageValid(_store.PackageDirectory);
            if (!valid && (running.Count > 0 || (uninstall && File.Exists(_store.Executable))))
                throw new AuthenticationCenterException("authentication_update_invalid_package");
            state.hadPackage = valid || (previous != null && previous.hadPackage);
            // Preserve a post-copy identity across interrupted updates; cancellation must not restore an older core.
            state.expectedPackage = previous == null || previous.completed ? null : _store.Expected(previous);
            _store.Write(state);
            if (valid) _store.CopyPackage(_store.PackageDirectory, _store.BackupDirectory);
            foreach (var target in uninstall ? state.targets : running)
            {
                token.ThrowIfCancellationRequested();
                await _stop(target, uninstall, token).ConfigureAwait(false);
            }
            if (uninstall)
            {
                _clearRegistrations();
                _store.Complete(state);
            }
        }

        private async Task<int> RestoreAsync(string action, CancellationToken token)
        {
            var state = _store.Read();
            if (state == null)
            {
                if (action == "Finalize") throw new AuthenticationCenterException("authentication_update_state_missing");
                return 0;
            }
            var expected = _store.Expected(state);
            if (action == "Finalize" && expected == null)
                throw new AuthenticationCenterException("authentication_expected_package_missing");
            if (expected != null)
            {
                _store.VerifyExpected(expected);
                state.expectedPackage = expected;
                _store.Write(state);
                _store.Log(action, "package_verified");
            }
            else if (!AuthenticationInstallationStateStore.IsPackageValid(_store.PackageDirectory))
            {
                if (!state.hadPackage && state.targets.Count == 0) { _store.Complete(state); return 0; }
                if (!AuthenticationInstallationStateStore.IsPackageValid(_store.BackupDirectory))
                    throw new AuthenticationCenterException("authentication_update_repair_required");
                _store.CopyPackage(_store.BackupDirectory, _store.PackageDirectory);
                _store.Log(action, "previous_package_restored");
            }
            // Persist registered routes too, so a failed restart is retried without requiring another installation.
            Merge(state.targets, _findRegistered());
            state.completed = false;
            _store.Write(state);
            var failed = false;
            foreach (var target in state.targets)
            {
                if (token.IsCancellationRequested) { failed = true; _store.Log(action, "authentication_update_timeout"); break; }
                try { await _restore(target, expected == null, token).ConfigureAwait(false); }
                catch (Exception error) { failed = true; _store.Log(action, ErrorCode(error)); }
            }
            if (failed)
            {
                _store.Log(action, "recovery_pending");
                return 2;
            }
            _store.Complete(state);
            _store.Log(action, "completed");
            return 0;
        }

        private LocalAuthenticationHost Host(Target target)
        {
            return new LocalAuthenticationHost("Bundled", _store.Executable, target.home, target.store);
        }

        private async Task StopAsync(Target target, bool uninstall, CancellationToken token)
        {
            await Host(target).MaintainInstallationAsync(stop: true, uninstall: uninstall,
                startIfMissing: target.processId == 0, allowOlderRelease: true, token: token).ConfigureAwait(false);
            if (target.processId <= 0) return;
            Process process;
            try { process = Process.GetProcessById(target.processId); }
            catch (ArgumentException) { return; }
            using (process)
            {
                var timer = Stopwatch.StartNew();
                while (!process.HasExited)
                {
                    token.ThrowIfCancellationRequested();
                    if (timer.ElapsedMilliseconds >= 15000) throw new AuthenticationCenterException("authentication_backend_did_not_exit");
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }

        private static void Merge(List<Target> targets, IEnumerable<Target> additional)
        {
            foreach (var target in additional)
                if (!targets.Any(existing => AuthenticationInstallationStateStore.SameTarget(existing, target)))
                    targets.Add(new Target { home = target.home, store = target.store });
        }

        private static string ErrorCode(Exception error)
        {
            var code = (error as AuthenticationCenterException)?.Code;
            if (error is OperationCanceledException) return "authentication_update_timeout";
            return !string.IsNullOrEmpty(code) && code.Length <= 80 && code.All(c => c == '_' || (c >= 'a' && c <= 'z'))
                ? code : "authentication_maintenance_failed";
        }

        internal static async Task ResumePendingAsync(string installationRoot, CancellationToken token)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCodex", "authentication-updates");
            if (!Directory.Exists(directory)) return;
            // Older installers used an ANSI, case-sensitive directory hash. Match the recorded installation instead.
            foreach (var candidate in Directory.GetDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) continue;
                    var store = new AuthenticationInstallationStateStore(installationRoot, Path.Combine(candidate, "state.json"));
                    var state = store.Read();
                    if (state == null || state.completed) continue;
                    await new AuthenticationInstallationMaintenance(installationRoot, store.Path).RunAsync("Resume", token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* Other installations and unreadable old records are not permission to alter them. */ }
            }
        }
    }
}
