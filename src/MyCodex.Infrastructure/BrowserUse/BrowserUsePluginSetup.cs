using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal static class BrowserUsePluginSetup
    {
        internal const string Marketplace = "mycodex-browser-use";
        internal const string PluginId = "browser-use@" + Marketplace;
        internal const string EnableKey = "plugins.\"" + PluginId + "\".enabled";
        private static readonly Dictionary<string, string> Files = new Dictionary<string, string>
        {
            { ".agents/plugins/marketplace.json", "MyCodex.BrowserUse.PluginMarketplace" },
            { "plugins/browser-use/.codex-plugin/plugin.json", "MyCodex.BrowserUse.PluginManifest" },
            { "plugins/browser-use/skills/browser-use/SKILL.md", "MyCodex.BrowserUse.PluginSkill" }
        };

        internal static string VerifyPackage(string installation)
        {
            var root = Path.GetFullPath(Path.Combine(installation, "browser-use"));
            BrowserExtensionHostBundle.RejectLinkedAncestors(root);
            if (PackageFiles(root).Take(Files.Count + 1).Count() != Files.Count) throw new InvalidDataException("BROWSER_PLUGIN_PACKAGE_INVALID");
            foreach (var item in Files)
            {
                var path = Path.Combine(root, item.Key);
                BrowserExtensionHostBundle.RejectLinkedAncestors(path);
                using (var expected = typeof(BrowserUsePluginSetup).Assembly.GetManifestResourceStream(item.Value))
                using (var copy = new MemoryStream())
                {
                    if (expected == null) throw new InvalidDataException("BROWSER_PLUGIN_PACKAGE_INVALID");
                    expected.CopyTo(copy);
                    if (new FileInfo(path).Length != copy.Length || !File.ReadAllBytes(path).SequenceEqual(copy.ToArray()))
                        throw new InvalidDataException("BROWSER_PLUGIN_PACKAGE_INVALID");
                }
            }
            return root;
        }

        private static IEnumerable<string> PackageFiles(string root)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root))
            {
                BrowserExtensionHostBundle.RejectLinkedAncestors(path);
                if (Directory.Exists(path))
                {
                    foreach (var file in PackageFiles(path)) yield return file;
                }
                else yield return path;
            }
        }

        internal static async Task<bool> EnableAsync(IAppServerClient client, string installation, CancellationToken token)
        {
            if (client == null) throw new InvalidOperationException("BROWSER_RUNTIME_NOT_CONNECTED");
            var root = VerifyPackage(installation);
            using ((client as IBrowserControlRuntime)?.BeginBrowserControlSetup(root))
                return await EnableCoreAsync(client, root, token).ConfigureAwait(false);
        }

        private static async Task<bool> EnableCoreAsync(IAppServerClient client, string root, CancellationToken token)
        {
            var plugins = await client.GetInstalledPluginsAsync(token).ConfigureAwait(false);
            var own = plugins.Plugins.FirstOrDefault(value => value.PluginId == PluginId && value.Installed);
            var source = Path.Combine(root, "plugins", "browser-use");
            if (own != null && own.Enabled && SamePath(own.SourcePath, source)) return false;
            if (own != null && !SamePath(own.SourcePath, source))
            {
                if (own.Name != "browser-use" || own.MarketplaceId != Marketplace || own.SourceType != "local" ||
                    string.IsNullOrEmpty(own.SourcePath) || !Path.IsPathRooted(own.SourcePath) ||
                    !own.SourcePath.Replace('\\', '/').TrimEnd('/').EndsWith("/browser-use/plugins/browser-use", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("BROWSER_PLUGIN_SOURCE_CONFLICT");
                await client.RemoveMarketplaceAsync(Marketplace, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            var added = await client.AddMarketplaceAsync(root, token).ConfigureAwait(false);
            if (added.MarketplaceName != Marketplace) throw new InvalidDataException("BROWSER_PLUGIN_MARKETPLACE_CONFLICT");
            token.ThrowIfCancellationRequested();
            await client.InstallPluginAsync(Path.Combine(root, ".agents", "plugins", "marketplace.json"), "browser-use").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await client.WriteConfigBatchAsync(new[] { AppServerConfigWriteEdit.CreateReplace(EnableKey, true) }, reloadUserConfig: true).ConfigureAwait(false);
            var installed = await client.GetInstalledPluginsAsync(token).ConfigureAwait(false);
            if (!installed.Plugins.Any(value => value.PluginId == PluginId && value.Installed && value.Enabled && SamePath(value.SourcePath, source)))
                throw new InvalidDataException("BROWSER_PLUGIN_NOT_ENABLED");
            return true;
        }

        internal static BrowserAutomationToolState State(IReadOnlyCollection<AppServerPluginSummary> plugins, IReadOnlyCollection<AppServerMcpServerStatus> servers)
        {
            if (servers.Any(value => value.ServerName == "cua_repl" && value.Enabled != false && value.ToolCount > 0 && string.IsNullOrWhiteSpace(value.ErrorText)))
                return BrowserAutomationToolState.Ready;
            var candidates = plugins.Where(value => value.Installed &&
                (value.PluginId == PluginId || value.PluginId == "chrome@openai-bundled" || value.PluginId == "unified-computer-use@openai-bundled")).ToArray();
            if (candidates.Any(value => value.Enabled)) return BrowserAutomationToolState.Configured;
            return candidates.Length > 0 ? BrowserAutomationToolState.Disabled : BrowserAutomationToolState.Missing;
        }

        private static bool SamePath(string left, string right)
        {
            string Normalize(string path)
            {
                if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
                else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
                return Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            return !string.IsNullOrWhiteSpace(left) && string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
