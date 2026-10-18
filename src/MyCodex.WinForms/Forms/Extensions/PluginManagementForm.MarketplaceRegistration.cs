using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class PluginManagementForm
    {
        private bool CanRegisterMarketplace(PluginMarketListItem item, bool isInstalledTab)
        {
            return !_isBusy &&
                !isInstalledTab &&
                CanInstallRemoteCatalogItem(item) &&
                MarketplaceMutationPolicy.EvaluateRegistration(
                    _appServerClient.ProtocolCapabilities).IsAvailable;
        }

        private async Task RegisterSelectedMarketplaceAsync()
        {
            var item = GetSelectedMarketItem();
            if (item == null || _isBusy || !CanInstallRemoteCatalogItem(item))
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text(
                    "PluginManagementForm.Action.RegisterMarketplace",
                    "Register marketplace")))
            {
                return;
            }

            if (!EnsureMarketplaceRegistrationAvailable())
            {
                return;
            }

            AppServerMarketplaceRegistrationResult result;
            try
            {
                SetBusy(true);
                SetStatus(string.Format(
                    UiLocalization.Text(
                        "PluginManagementForm.Status.RegisteringMarketplace.Template",
                        "Registering marketplace {0}..."),
                    item.MarketName),
                    true);
                result = await RegisterMarketplaceFromItemAsync(item);
            }
            catch (Exception)
            {
                var message = UiLocalization.Text(
                    "PluginManagementForm.Status.RegisterMarketplaceFailed",
                    "The target App Server rejected or could not load this marketplace. Verify the marketplace source and manifest before trying again.");
                SetStatus(message, true);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            _plugins = (result.CatalogPlugins ?? new List<AppServerPluginSummary>())
                .Where(plugin => plugin != null)
                .ToList();
            _installedPlugins = (result.InstalledPlugins ?? new List<AppServerPluginSummary>())
                .Where(plugin => plugin != null)
                .ToList();
            RebuildInstalledList();
            RebuildMarketSourceTree();
            await BrowseMarketScopeAsync(_currentMarketScope, _currentMarketKeyword, 1, true);

            var status = result.RediscoveredInstalledCount > 0
                ? string.Format(
                    UiLocalization.Text(
                        "PluginManagementForm.Status.MarketplaceRegisteredRediscovered.Template",
                        "Marketplace {0} registered. {1} existing installed plugin(s) were rediscovered."),
                    result.MarketplaceName,
                    result.RediscoveredInstalledCount)
                : string.Format(
                    UiLocalization.Text(
                        "PluginManagementForm.Status.MarketplaceRegistered.Template",
                        "Marketplace {0} registered."),
                    result.MarketplaceName);
            SetStatus(status, true);
            if (_marketListView.Items.Count > 0)
            {
                _marketListView.Focus();
            }
            else
            {
                _marketSourceTreeView.Focus();
            }
        }

        private Task<AppServerMarketplaceRegistrationResult> RegisterMarketplaceFromItemAsync(
            PluginMarketListItem item)
        {
            var marketId = item.RemoteEntry == null ? null : item.RemoteEntry.MarketId;
            if (string.Equals(
                    marketId,
                    MyCodexOfficialMarketplaceBootstrap.MarketplaceId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.AppServerSource,
                    MyCodexOfficialMarketplaceBootstrap.MarketplaceSource,
                    StringComparison.OrdinalIgnoreCase))
            {
                return MyCodexOfficialMarketplaceBootstrap.RegisterPinnedMarketplaceAsync(
                    _appServerClient,
                    _lifetimeCancellation.Token);
            }

            return AppServerMarketplaceRegistrationService.RegisterMarketplaceAsync(
                _appServerClient,
                item.AppServerSource,
                marketId,
                _lifetimeCancellation.Token);
        }

        private bool EnsureMarketplaceRegistrationAvailable()
        {
            if (MarketplaceMutationPolicy.EvaluateRegistration(
                    _appServerClient.ProtocolCapabilities).IsAvailable)
            {
                return true;
            }

            var message = UiLocalization.Text(
                "PluginManagementForm.Message.MarketplaceRegistrationUnavailable",
                "Marketplace registration is unavailable for the selected App Server runtime. Target state was not changed.");
            SetStatus(message, true);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
    }
}
