using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerPluginClient
    {
        Task<List<AppServerPluginSummary>> ListPluginsAsync();

        Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken);

        Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName);

        Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(
            string remoteMarketplaceName,
            string remotePluginId,
            string skillName,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(
            string source,
            CancellationToken cancellationToken = default(CancellationToken));

        Task RemoveMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName);

        Task<AppServerPluginInstallResult> InstallRemotePluginAsync(
            string remoteMarketplaceName,
            string remotePluginId);

        Task UninstallPluginAsync(string pluginId);
    }
}
