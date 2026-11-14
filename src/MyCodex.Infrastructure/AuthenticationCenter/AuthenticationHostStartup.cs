using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.Instances;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public static class AuthenticationHostStartup
    {
        internal const string RegistrationRoot = @"Software\MyCodex\AuthenticationHosts";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        internal static string KeyFor(string path)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    Path.GetFullPath(path).TrimEnd('\\').ToUpperInvariant()))).Replace("-", string.Empty);
            }
        }

        internal static string StartupCommand(string applicationPath)
        {
            if (!Path.IsPathRooted(applicationPath)) throw new AuthenticationCenterException("startup_registration_failed");
            var command = WindowsCommandLineArgumentEscaper.Quote(applicationPath) + " --resume-authentication-hosts";
            if (command.Length > 260) throw new AuthenticationCenterException("startup_registration_failed");
            return command;
        }

        internal static bool RouteRequired(IDictionary<string, object> status)
        {
            return status.TryGetValue("route", out var value) && value is IDictionary<string, object> route &&
                route.TryGetValue("takeover", out var required) && Equals(required, true);
        }

        internal static bool RouteRunning(IDictionary<string, object> status)
        {
            return status.TryGetValue("route", out var value) && value is IDictionary<string, object> route &&
                route.TryGetValue("running", out var running) && Equals(running, true) &&
                route.TryGetValue("accepting", out var accepting) && Equals(accepting, true);
        }

        internal static void Configure(string executable, string home, string store, bool required)
        {
            try
            {
                var applicationPath = Process.GetCurrentProcess().MainModule.FileName;
                using (var root = required ? Registry.CurrentUser.CreateSubKey(RegistrationRoot)
                    : Registry.CurrentUser.OpenSubKey(RegistrationRoot, true))
                using (var run = required ? Registry.CurrentUser.CreateSubKey(RunKey) : Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (root != null && run != null) Configure(root, run, applicationPath, executable, home, store, required);
                }
            }
            catch (Exception)
            {
                throw new AuthenticationCenterException("startup_registration_failed");
            }
        }

        internal static void Configure(RegistryKey root, RegistryKey run, string applicationPath,
            string executable, string home, string store, bool required)
        {
            var applicationKey = KeyFor(applicationPath);
            var name = KeyFor(home);
            using (var registrations = required ? root.CreateSubKey(applicationKey) : root.OpenSubKey(applicationKey, true))
            {
                if (registrations == null) return;
                if (required)
                {
                    // Only paths are registered; credentials remain in the protected backend store.
                    var command = StartupCommand(applicationPath);
                    registrations.SetValue(name, new[] { executable, home, store }, RegistryValueKind.MultiString);
                    run.SetValue("MyCodex.Authentication." + applicationKey, command, RegistryValueKind.String);
                }
                else
                {
                    registrations.DeleteValue(name, false);
                    if (registrations.ValueCount == 0) run.DeleteValue("MyCodex.Authentication." + applicationKey, false);
                    return;
                }
            }
            // One shared store needs one startup owner; stale installation entries must not win the next sign-in race.
            foreach (var key in root.GetSubKeyNames())
            {
                if (string.Equals(key, applicationKey, StringComparison.OrdinalIgnoreCase)) continue;
                using (var previous = root.OpenSubKey(key, true))
                {
                    var paths = previous?.GetValue(name) as string[];
                    if (paths == null || paths.Length != 3 || string.IsNullOrWhiteSpace(paths[1]) || string.IsNullOrWhiteSpace(paths[2]) ||
                        !Path.IsPathRooted(paths[1]) || !Path.IsPathRooted(paths[2]) || !string.Equals(KeyFor(paths[1]), name, StringComparison.Ordinal) ||
                        !string.Equals(KeyFor(paths[2]), KeyFor(store), StringComparison.Ordinal)) continue;
                    previous.DeleteValue(name, false);
                    if (previous.ValueCount == 0) run.DeleteValue("MyCodex.Authentication." + key, false);
                }
            }
        }

        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length != 1 || args[0] != "--resume-authentication-hosts") return false;
            try
            {
                using (var reservation = TargetInstanceCoordinator.TryReserveSharedUpdate())
                    exitCode = reservation == null ? 1 : ResumeAsync().GetAwaiter().GetResult();
            }
            catch { exitCode = 1; }
            return true;
        }

        internal static List<AuthenticationInstallationStateStore.Target> ReadInstallationTargets(string installationRoot)
        {
            var result = new List<AuthenticationInstallationStateStore.Target>();
            var applicationPath = Path.Combine(installationRoot, "MyCodex.exe");
            var executable = Path.Combine(installationRoot, "runtime", "auth-core", "mycodex-auth-host.exe");
            using (var registrations = Registry.CurrentUser.OpenSubKey(RegistrationRoot + "\\" + KeyFor(applicationPath)))
            {
                if (registrations == null) return result;
                foreach (var name in registrations.GetValueNames())
                {
                    var paths = registrations.GetValue(name) as string[];
                    if (paths == null || paths.Length != 3 || name != KeyFor(paths[1]) ||
                        !AuthenticationInstallationStateStore.SamePath(paths[0], executable))
                        throw new AuthenticationCenterException("authentication_update_invalid_registration");
                    result.Add(new AuthenticationInstallationStateStore.Target
                    {
                        home = AuthenticationInstallationStateStore.Normalize(paths[1]),
                        store = AuthenticationInstallationStateStore.Normalize(paths[2])
                    });
                }
            }
            return result;
        }

        internal static void ClearInstallation(string installationRoot)
        {
            var key = KeyFor(Path.Combine(installationRoot, "MyCodex.exe"));
            Registry.CurrentUser.DeleteSubKeyTree(RegistrationRoot + "\\" + key, false);
            using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                run?.DeleteValue("MyCodex.Authentication." + key, false);
        }

        private static async Task<int> ResumeAsync()
        {
            var applicationPath = Process.GetCurrentProcess().MainModule.FileName;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                await AuthenticationInstallationMaintenance.ResumePendingAsync(Path.GetDirectoryName(applicationPath), timeout.Token).ConfigureAwait(false);
            using (var registrations = Registry.CurrentUser.OpenSubKey(RegistrationRoot + "\\" + KeyFor(applicationPath)))
            {
                if (registrations == null) return 0;
                var failures = 0;
                foreach (var name in registrations.GetValueNames())
                {
                    try
                    {
                        var paths = registrations.GetValue(name) as string[];
                        var expectedExecutable = Path.Combine(Path.GetDirectoryName(applicationPath), "runtime", "auth-core", "mycodex-auth-host.exe");
                        if (paths == null || paths.Length != 3 || name != KeyFor(paths[1]) ||
                            !string.Equals(paths[0], expectedExecutable, StringComparison.OrdinalIgnoreCase)) { failures++; continue; }
                        var host = new LocalAuthenticationHost("Bundled", paths[0], paths[1], paths[2], manageStartup: true);
                        var status = await host.RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None).ConfigureAwait(false);
                        if (!RouteRequired(status))
                        {
                            await host.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                            Configure(paths[0], paths[1], paths[2], false);
                        }
                        else if (!RouteRunning(status)) failures++;
                    }
                    catch { failures++; }
                }
                return failures == 0 ? 0 : 1;
            }
        }
    }
}
