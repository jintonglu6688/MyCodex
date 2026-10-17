using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed class ComputerUsePluginPreparation
    {
        private readonly Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> _send;

        internal ComputerUsePluginPreparation(
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> send)
        {
            _send = send;
        }

        internal async Task<bool> EnsureAsync(ComputerUseBundle bundle, string codexHome, CancellationToken token, bool install,
            IDictionary<string, object> config = null)
        {
            var snapshot = await ReadAsync(token).ConfigureAwait(false);
            var legacy = snapshot.Plugins.FirstOrDefault(plugin => plugin.PluginId == ComputerUseBundle.LegacyPluginId && plugin.Installed);
            if (legacy != null && !legacy.Enabled) return false;
            var plugin = snapshot.Plugins.SingleOrDefault(item => item.PluginId == ComputerUseBundle.PluginId);
            if (plugin != null && plugin.Installed && !plugin.Enabled) return false;
            if (ComputerUseConfigurationPlanner.IsPluginDisabled(config)) return false;
            var registeredSource = RegisteredSource(config);
            var moved = registeredSource != null && !SamePath(registeredSource, bundle.MarketplaceRoot);
            if (plugin != null)
            {
                ValidateIdentity(plugin);
                if (!SamePath(plugin.SourcePath, bundle.PluginRoot))
                {
                    if (!IsBundledMarketplace(Path.GetDirectoryName(Path.GetDirectoryName(NormalizePath(plugin.SourcePath)))))
                        throw new InvalidDataException("CUA_PLUGIN_SOURCE_INVALID");
                    moved = true;
                }
            }
            if (!moved && snapshot.MarketplaceErrors.Any(error => error.MarketplaceName == ComputerUseBundle.MarketplaceName ||
                (!string.IsNullOrEmpty(error.MarketplaceName) && SamePath(error.MarketplaceName, bundle.MarketplacePath))))
                throw new InvalidDataException("CUA_MARKETPLACE_UNAVAILABLE");
            var needsInstall = moved || plugin == null || !plugin.Installed ||
                (plugin.LocalVersion ?? plugin.Version) != bundle.PluginVersion;
            if (needsInstall)
            {
                if (plugin != null && plugin.Installed && (plugin.LocalVersion ?? plugin.Version) == bundle.PluginVersion)
                {
                    bundle.VerifyInstalledPlugin(codexHome);
                    // A bound executor uses this verified package, even if another instance moved the registration.
                    if (!install) return true;
                }
                if (!install) return false;
                // Replace only our registration. The previous path is never loaded or deleted.
                token.ThrowIfCancellationRequested();
                if (moved)
                {
                    await _send("marketplace/remove", new Dictionary<string, object>
                    {
                        { "marketplaceName", ComputerUseBundle.MarketplaceName }
                    }, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                var registered = AppServerMarketplaceMapper.BuildAddResult(await _send("marketplace/add",
                    new Dictionary<string, object> { { "source", bundle.MarketplaceRoot } }, token).ConfigureAwait(false));
                if (registered.MarketplaceName != ComputerUseBundle.MarketplaceName)
                    throw new InvalidDataException("CUA_MARKETPLACE_IDENTITY_INVALID");
                token.ThrowIfCancellationRequested();
                await _send("plugin/install", new Dictionary<string, object>
                {
                    { "marketplacePath", bundle.MarketplacePath }, { "pluginName", "computer-use" }
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                snapshot = await ReadAsync(token).ConfigureAwait(false);
                plugin = snapshot.Plugins.SingleOrDefault(item => item.PluginId == ComputerUseBundle.PluginId);
            }
            if (plugin == null || !plugin.Installed || !plugin.Enabled) return false;
            ValidateSource(plugin, bundle);
            if ((plugin.LocalVersion ?? plugin.Version) != bundle.PluginVersion)
                throw new InvalidDataException("CUA_PLUGIN_VERSION_INVALID");
            bundle.VerifyInstalledPlugin(codexHome);
            return true;
        }

        private async Task<AppServerPluginInstalledSnapshot> ReadAsync(CancellationToken token)
        {
            return AppServerPluginSkillMapper.BuildInstalledPluginSnapshot(await _send("plugin/installed",
                new Dictionary<string, object>(), token).ConfigureAwait(false));
        }

        private static void ValidateSource(AppServerPluginSummary plugin, ComputerUseBundle bundle)
        {
            ValidateIdentity(plugin);
            if (!SamePath(plugin.SourcePath, bundle.PluginRoot))
                throw new InvalidDataException("CUA_PLUGIN_SOURCE_INVALID");
        }

        private static void ValidateIdentity(AppServerPluginSummary plugin)
        {
            if (plugin.PluginId != ComputerUseBundle.PluginId || plugin.Name != "computer-use" ||
                plugin.MarketplaceId != ComputerUseBundle.MarketplaceName || plugin.SourceType != "local" ||
                string.IsNullOrEmpty(plugin.SourcePath) || !Path.IsPathRooted(plugin.SourcePath))
                throw new InvalidDataException("CUA_PLUGIN_SOURCE_INVALID");
            var path = NormalizePath(plugin.SourcePath);
            if (!string.Equals(Path.GetFileName(path), "computer-use", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "plugins", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CUA_PLUGIN_SOURCE_INVALID");
        }

        private static string RegisteredSource(IDictionary<string, object> config)
        {
            var entry = AppServerJsonValue.GetDictionary(AppServerJsonValue.GetDictionary(config, "marketplaces"),
                ComputerUseBundle.MarketplaceName);
            if (entry.Count == 0) return null;
            var source = AppServerJsonValue.GetString(entry, "source");
            if (AppServerJsonValue.GetString(entry, "source_type") != "local" || !IsBundledMarketplace(source))
                throw new InvalidDataException("CUA_PLUGIN_SOURCE_INVALID");
            return source;
        }

        private static bool IsBundledMarketplace(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
            var directory = new DirectoryInfo(NormalizePath(path));
            return string.Equals(directory.Name, "marketplace", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(directory.Parent?.Parent?.Name, "cua", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(directory.Parent?.Parent?.Parent?.Name, "runtime", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            // App Server may return extended drive or UNC paths.
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
    }
}
