using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.BrowserUse
{
    public sealed class WindowsBrowserExtensionSetupService : IBrowserExtensionSetupService
    {
        // Reviewed against the official Chrome plugin's extension-ids.json (26.1023.51421).
        internal static readonly string[] ExtensionIds =
        {
            "hehggadaopoacecdllhhajmbjkdcmajg", "odlomjlbamekndcpllcnffbgeohgkmjh"
        };
        private const string HostName = "com.openai.codexextension";
        private readonly string _localAppData;
        private readonly Func<string, string> _readRegistry;
        private readonly IAppServerClient _client;
        private readonly string _installation;
        private readonly IBrowserNativeMessagingRegistration _registration;

        public WindowsBrowserExtensionSetupService(IAppServerClient client = null) : this(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            key => Registry.GetValue(key, null, null) as string, client,
            AppDomain.CurrentDomain.BaseDirectory, new BrowserNativeMessagingRegistration()) { }

        internal WindowsBrowserExtensionSetupService(string localAppData, Func<string, string> readRegistry)
            : this(localAppData, readRegistry, null, AppDomain.CurrentDomain.BaseDirectory, new BrowserNativeMessagingRegistration()) { }

        internal WindowsBrowserExtensionSetupService(string localAppData, Func<string, string> readRegistry,
            IAppServerClient client, string installation, IBrowserNativeMessagingRegistration registration)
        {
            _localAppData = Path.GetFullPath(localAppData);
            _readRegistry = readRegistry ?? throw new ArgumentNullException(nameof(readRegistry));
            _client = client;
            _installation = Path.GetFullPath(installation);
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        public async Task<BrowserExtensionSetupReport> CheckAsync(BrowserExtensionBrowser browser, CancellationToken cancellationToken)
        {
            ValidateBrowser(browser);
            var report = await Task.Run(() => Check(browser, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (_client != null)
            {
                try
                {
                    var plugins = await _client.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false);
                    var servers = await _client.ListMcpServerStatusesAsync().ConfigureAwait(false);
                    report.Tools = BrowserUsePluginSetup.State(plugins.Plugins, servers);
                }
                catch (OperationCanceledException) { throw; }
                catch { report.Tools = BrowserAutomationToolState.Unknown; }
            }
            return report;
        }

        public async Task<bool> PrepareAsync(CancellationToken cancellationToken)
        {
            // Verify shipped files before changing the user's plugin configuration or registrations.
            var installation = _installation;
            var bytes = await Task.Run(() => BrowserExtensionHostBundle.Read(installation), cancellationToken).ConfigureAwait(false);
            BrowserUsePluginSetup.VerifyPackage(installation);
            var changed = await BrowserUsePluginSetup.EnableAsync(_client, installation, cancellationToken).ConfigureAwait(false);
            foreach (var browser in new[] { BrowserExtensionBrowser.Edge, BrowserExtensionBrowser.Chrome })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var report = await Task.Run(() => Check(browser, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (report.BrowserInstalled == false || report.Bridge == BrowserExtensionBridgeState.Registered) continue;
                if (!report.BrowserInstalled.HasValue) throw new IOException("BROWSER_DETECTION_UNAVAILABLE");
                if (report.Bridge == BrowserExtensionBridgeState.Unknown) throw new IOException("BROWSER_BRIDGE_UNREADABLE");
                await Task.Run(() => new BrowserExtensionBridgeInstaller(_localAppData, () => bytes,
                    _registration).Install(browser, cancellationToken), cancellationToken).ConfigureAwait(false);
                changed = true;
            }
            return changed;
        }

        private BrowserExtensionSetupReport Check(BrowserExtensionBrowser browser, CancellationToken token)
        {
            var report = new BrowserExtensionSetupReport { Bridge = ReadBridge(browser) };
            var root = Path.Combine(_localAppData, browser == BrowserExtensionBrowser.Edge ? "Microsoft/Edge/User Data" : "Google/Chrome/User Data");
            try
            {
                token.ThrowIfCancellationRequested();
                report.BrowserInstalled = FindBrowser(browser) != null;
                if (!Directory.Exists(root)) return report;
                RejectLink(root);
                var profiles = Directory.GetDirectories(root).Where(path =>
                    Regex.IsMatch(Path.GetFileName(path), @"^(Default|Profile \d+)$", RegexOptions.CultureInvariant)).Take(65).ToArray();
                if (profiles.Length > 64) throw new IOException("Too many browser profiles.");
                foreach (var profile in profiles.OrderBy(Path.GetFileName, StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    report.Profiles.Add(new BrowserExtensionProfileStatus
                    {
                        ProfileDirectory = Path.GetFileName(profile), State = ReadProfile(profile)
                    });
                }
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                report.Profiles.Clear();
                report.Profiles.Add(new BrowserExtensionProfileStatus { State = BrowserExtensionInstallState.Unknown });
            }
            return report;
        }

        private static BrowserExtensionInstallState ReadProfile(string profile)
        {
            try
            {
                RejectLink(profile);
                IDictionary<string, object> settings = null;
                var readPreferences = false;
                foreach (var file in new[] { "Secure Preferences", "Preferences" })
                {
                    var path = Path.Combine(profile, file);
                    if (!File.Exists(path)) continue;
                    var preferences = ReadJson(path, 32 * 1024 * 1024);
                    readPreferences = true;
                    preferences.TryGetValue("extensions", out var extensions);
                    if (extensions != null && !(extensions is IDictionary<string, object>)) throw new InvalidDataException("Invalid extension settings.");
                    (extensions as IDictionary<string, object> ?? new Dictionary<string, object>()).TryGetValue("settings", out var value);
                    if (value != null && !(value is IDictionary<string, object>)) throw new InvalidDataException("Invalid extension settings.");
                    if (value is IDictionary<string, object> entries)
                    {
                        // A complete secure settings table also authoritatively records removals.
                        settings = entries;
                        break;
                    }
                }
                if (settings == null)
                    return readPreferences && !ExtensionIds.Any(id => Directory.Exists(Path.Combine(profile, "Extensions", id)))
                        ? BrowserExtensionInstallState.NotInstalled : BrowserExtensionInstallState.Unknown;
                var states = ExtensionIds.Select(id => ReadExtension(profile, id, settings)).ToArray();
                if (states.Contains(BrowserExtensionInstallState.Enabled)) return BrowserExtensionInstallState.Enabled;
                if (states.Contains(BrowserExtensionInstallState.Unknown)) return BrowserExtensionInstallState.Unknown;
                return states.Contains(BrowserExtensionInstallState.Disabled) ? BrowserExtensionInstallState.Disabled : BrowserExtensionInstallState.NotInstalled;
            }
            catch (Exception ex) when (IsReadFailure(ex)) { return BrowserExtensionInstallState.Unknown; }
        }

        private static BrowserExtensionInstallState ReadExtension(string profile, string id, IDictionary<string, object> preferences)
        {
            if (!preferences.TryGetValue(id, out var registered)) return BrowserExtensionInstallState.NotInstalled;
            var entry = registered as IDictionary<string, object>;
            if (entry == null) return BrowserExtensionInstallState.Unknown;
            var folder = Path.Combine(profile, "Extensions", id);
            var installed = false;
            if (Directory.Exists(folder))
            {
                RejectLink(Path.GetDirectoryName(folder));
                RejectLink(folder);
                installed = Directory.GetDirectories(folder).Any(version => File.Exists(Path.Combine(version, "manifest.json")));
            }
            if (!installed) return BrowserExtensionInstallState.Unknown;
            entry.TryGetValue("state", out var state);
            entry.TryGetValue("disable_reasons", out var reasons);
            if (Equals(state, 0) || reasons is IList list && list.Count > 0 || reasons is int number && number != 0)
                return BrowserExtensionInstallState.Disabled;
            if (state != null && !Equals(state, 1) || reasons != null && !(reasons is IList) && !(reasons is int))
                return BrowserExtensionInstallState.Unknown;
            return BrowserExtensionInstallState.Enabled;
        }

        private BrowserExtensionBridgeState ReadBridge(BrowserExtensionBrowser browser)
        {
            try
            {
                var vendors = browser == BrowserExtensionBrowser.Edge ? new[] { "Microsoft\\Edge", "Google\\Chrome" } : new[] { "Google\\Chrome" };
                string path = null;
                foreach (var vendor in vendors)
                {
                    foreach (var hive in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
                    {
                        path = _readRegistry(hive + "\\Software\\" + vendor + "\\NativeMessagingHosts\\" + HostName);
                        if (path != null) break;
                    }
                    if (path != null) break;
                }
                if (path == null) return BrowserExtensionBridgeState.Missing;
                if (!Path.IsPathRooted(path) || !File.Exists(path)) return BrowserExtensionBridgeState.Invalid;
                var manifest = ReadJson(path, 64 * 1024);
                manifest.TryGetValue("name", out var name);
                manifest.TryGetValue("type", out var type);
                manifest.TryGetValue("path", out var executable);
                manifest.TryGetValue("allowed_origins", out var origins);
                return Equals(name, HostName) && Equals(type, "stdio") && executable is string exe &&
                    Path.IsPathRooted(exe) && File.Exists(exe) && origins is IList allowed &&
                    ExtensionIds.All(id => allowed.Cast<object>().Contains("chrome-extension://" + id + "/"))
                    ? BrowserExtensionBridgeState.Registered : BrowserExtensionBridgeState.Invalid;
            }
            catch (Exception ex) when (IsReadFailure(ex)) { return BrowserExtensionBridgeState.Unknown; }
        }

        private static IDictionary<string, object> ReadJson(string path, int limit)
        {
            RejectLink(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                if (stream.Length > limit) throw new IOException("Browser diagnostic input is too large.");
                var buffer = new char[(int)Math.Min((long)limit + 1, stream.Length + 1)];
                var length = reader.ReadBlock(buffer, 0, buffer.Length);
                if (length == buffer.Length) throw new IOException("Browser diagnostic input grew or is too large.");
                var text = new string(buffer, 0, length);
                return new JavaScriptSerializer { MaxJsonLength = limit }.Deserialize<Dictionary<string, object>>(text)
                    ?? throw new InvalidDataException("Invalid browser diagnostic input.");
            }
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked browser diagnostic input.");
        }

        private static bool IsReadFailure(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException;
        }

        internal static string PageUrl(BrowserExtensionBrowser browser, BrowserExtensionSetupPage page)
        {
            ValidateBrowser(browser);
            if (page == BrowserExtensionSetupPage.Extensions) return browser == BrowserExtensionBrowser.Edge ? "edge://extensions/" : "chrome://extensions/";
            if (page != BrowserExtensionSetupPage.Store) throw new ArgumentOutOfRangeException(nameof(page));
            return browser == BrowserExtensionBrowser.Edge
                ? "https://microsoftedge.microsoft.com/addons/detail/odlomjlbamekndcpllcnffbgeohgkmjh"
                : "https://chromewebstore.google.com/detail/chatgpt/hehggadaopoacecdllhhajmbjkdcmajg";
        }

        public void OpenPage(BrowserExtensionBrowser browser, BrowserExtensionSetupPage page)
        {
            var url = PageUrl(browser, page);
            var executable = FindBrowser(browser);
            if (executable == null) throw new FileNotFoundException("BROWSER_EXECUTABLE_UNAVAILABLE");
            using (Process.Start(new ProcessStartInfo(executable, WindowsCommandLineArgumentEscaper.Quote(url)) { UseShellExecute = false })) { }
        }

        private string FindBrowser(BrowserExtensionBrowser browser)
        {
            var name = browser == BrowserExtensionBrowser.Edge ? "msedge.exe" : "chrome.exe";
            var relative = browser == BrowserExtensionBrowser.Edge ? "Microsoft/Edge/Application/" : "Google/Chrome/Application/";
            var candidates = new List<string>();
            foreach (var hive in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE" })
                candidates.Add(_readRegistry(hive + "\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\" + name));
            foreach (var root in new[] { _localAppData, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, relative, name));
            return candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim('"'))
                .FirstOrDefault(value => Path.IsPathRooted(value) && string.Equals(Path.GetFileName(value), name, StringComparison.OrdinalIgnoreCase) && File.Exists(value));
        }

        private static void ValidateBrowser(BrowserExtensionBrowser browser)
        {
            if (browser != BrowserExtensionBrowser.Edge && browser != BrowserExtensionBrowser.Chrome) throw new ArgumentOutOfRangeException(nameof(browser));
        }
    }
}
