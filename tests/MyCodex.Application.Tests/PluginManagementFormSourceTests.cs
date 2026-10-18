using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginManagementFormSourceTests
    {
        [TestCase("TabControl_SelectedIndexChanged")]
        [TestCase("PluginListView_SelectedIndexChanged")]
        public void FocusAndFilterHandlers_DoNotInvokePluginServiceOperations(string methodName)
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, methodName);
            var forbiddenCalls = new[]
            {
                "LoadDataAsync",
                "ListPluginsAsync",
                "LoadIndexAsync",
                "LoadMarketAsync",
                "ReadPluginAsync",
                "InstallAsync",
                "UninstallPluginAsync",
                "EnsureAppServerAvailableForActionAsync"
            };

            foreach (var call in forbiddenCalls)
            {
                Assert.That(
                    body.IndexOf(call, StringComparison.Ordinal),
                    Is.LessThan(0),
                    methodName + " 不应触发高成本插件服务调用：" + call);
            }
        }

        [Test]
        public void LoadDataAsync_RemoteMarketFailureOnlyUpdatesStatus_WithoutShowingMessageBox()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "LoadDataAsync");
            var remoteErrorBlock = ExtractBlock(body, "if (remoteError != null)");
            var loadPluginsFailedIndex = body.IndexOf(
                "PluginManagementForm.Status.LoadPluginsFailed",
                StringComparison.Ordinal);

            Assert.That(
                remoteErrorBlock.IndexOf("MessageBox.Show", StringComparison.Ordinal),
                Is.LessThan(0),
                "远程市场单独失败时不应自动弹出错误框。");
            Assert.That(
                remoteErrorBlock.IndexOf("SetStatus", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "远程市场单独失败时应保留状态栏提示。");
            Assert.That(
                loadPluginsFailedIndex,
                Is.GreaterThanOrEqualTo(0),
                "应保留整体加载失败状态文案。");
            Assert.That(
                body.IndexOf("MessageBox.Show", loadPluginsFailedIndex, StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "整体加载失败时仍应弹出错误框。");
        }

        [Test]
        public void LoadDataAsync_PublishesFinalStatusAfterLeavingBusyState()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "LoadDataAsync");
            var clearBusyIndex = body.LastIndexOf("SetBusy(false)", StringComparison.Ordinal);
            var finalStatusIndex = body.LastIndexOf("UpdateStatusForCurrentView", StringComparison.Ordinal);

            Assert.That(clearBusyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(finalStatusIndex, Is.GreaterThan(clearBusyIndex));
        }

        [Test]
        public void RemoteMarketOperations_UseFormCancellationToken()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var loadRemoteMarketItemsBody = ExtractMethodBody(source, "LoadRemoteMarketItemsAsync");
            var installSelectedPluginBody = ExtractMethodBody(source, "InstallSelectedPluginAsync");

            Assert.That(source, Does.Contain("CancellationTokenSource _lifetimeCancellation"));
            Assert.That(source, Does.Contain("CreateOperationCancellationTokenSource"));
            Assert.That(loadRemoteMarketItemsBody, Does.Not.Contain("CancellationToken.None"));
            Assert.That(installSelectedPluginBody, Does.Not.Contain("CancellationToken.None"));
        }

        [Test]
        public void TabMetadata_DoesNotAssignAccessibleTabsPrefix_OrTabMnemonics()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");

            Assert.That(
                source.IndexOf("PluginManagementForm.Accessible.Tabs", StringComparison.Ordinal),
                Is.LessThan(0),
                "插件管理标签组前不应再声明“视图/Views”可访问前缀。");
            Assert.That(
                source.IndexOf("\"PluginManagementForm.Tab.Installed\", \"Installed\"", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "已安装标签页不应包含助记符。");
            Assert.That(
                source.IndexOf("\"PluginManagementForm.Tab.Market\", \"Market\"", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "市场标签页不应包含助记符。");
        }

        [Test]
        public void PluginIdentityMatching_TreatsSharedDisplayNameAsSameVisiblePlugin()
        {
            var plugin = new AppServerPluginSummary
            {
                PluginId = "codex-official-superpowers",
                Name = "codex-official-superpowers",
                DisplayName = "Superpowers"
            };

            Assert.That(InvokePluginIdentityMatches(plugin, "personal-superpowers"), Is.False);
            Assert.That(InvokePluginIdentityMatches(plugin, "Superpowers"), Is.True);

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InvokeAddPluginIdentityKeys(keys, plugin);

            Assert.That(
                InvokeAddPluginIdentityKeysForCandidate(keys, "personal-superpowers", "Superpowers"),
                Is.False,
                "A cached market plugin with a different id but the same display name should not create a duplicate installed row.");
        }

        [Test]
        public void PluginIdentityMatching_DoesNotMergeSamePluginNameAcrossQualifiedMarketplaces()
        {
            var plugin = new AppServerPluginSummary
            {
                PluginId = "github@openai-curated",
                Name = "github",
                DisplayName = "GitHub"
            };

            Assert.That(
                InvokePluginIdentityMatches(plugin, "github@openai-curated-remote"),
                Is.False);

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InvokeAddPluginIdentityKeys(keys, plugin);

            Assert.That(
                InvokeAddPluginIdentityKeysForCandidate(
                    keys,
                    "github@openai-curated-remote",
                    "GitHub"),
                Is.True,
                "Marketplace-qualified plugin ids must remain distinct even when their base and display names match.");
        }

        [Test]
        public void InstalledList_PreservesSamePluginNameAcrossQualifiedMarketplaces()
        {
            var plugins = new List<AppServerPluginSummary>
            {
                new AppServerPluginSummary
                {
                    PluginId = "github@openai-curated",
                    Name = "github",
                    DisplayName = "GitHub",
                    Installed = true,
                    Enabled = true
                },
                new AppServerPluginSummary
                {
                    PluginId = "github@openai-curated-remote",
                    Name = "github",
                    DisplayName = "GitHub",
                    Installed = false,
                    Enabled = false
                }
            };

            var result = InvokeDeduplicatePluginSummariesForView(plugins);

            Assert.That(result, Has.Count.EqualTo(2));
        }

        [Test]
        public void RemoteCatalogIdentity_IncludesMarketplaceId()
        {
            var item = new PluginMarketListItem
            {
                PluginId = "github",
                RemoteEntry = new PluginMarketEntrySnapshot
                {
                    MarketId = "openai-curated-remote",
                    PluginId = "github"
                }
            };

            Assert.That(
                InvokeGetMarketItemPluginIdentity(item),
                Is.EqualTo("github@openai-curated-remote"));
        }

        [Test]
        public void InstalledList_DeduplicatesInitialRuntimePluginList_AndPrefersEnabledMarketEntry()
        {
            var plugins = new List<AppServerPluginSummary>
            {
                new AppServerPluginSummary
                {
                    MarketplaceName = "Codex official",
                    PluginId = "codex-official-superpowers",
                    Name = "codex-official-superpowers",
                    DisplayName = "Superpowers",
                    Installed = true
                },
                new AppServerPluginSummary
                {
                    MarketplaceName = "mycodex-official",
                    PluginId = "mycodex-official-superpowers",
                    Name = "mycodex-official-superpowers",
                    DisplayName = "Superpowers",
                    Installed = true,
                    Enabled = true,
                    Category = "Coding"
                }
            };

            var result = InvokeDeduplicatePluginSummariesForView(plugins);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].MarketplaceName, Is.EqualTo("mycodex-official"));
            Assert.That(result[0].Enabled, Is.True);
        }

        [Test]
        public void InstalledList_DeduplicatesOfficialInstalledPluginObservation()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "GetInstalledPluginsForView");

            Assert.That(
                body.IndexOf("DeduplicatePluginSummariesForView(_installedPlugins)", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "The App Server plugin/installed result can contain duplicate visible plugins and must be deduplicated for display.");
        }

        [Test]
        public void PluginApps_WithInstallUrlRemainAvailableForConnectionSetup()
        {
            var details = new AppServerPluginDetails
            {
                Apps = new List<AppServerPluginAppInfo>
                {
                    new AppServerPluginAppInfo
                    {
                        Id = "connector-github",
                        InstallUrl = "https://chatgpt.com/apps/github",
                        NeedsAuth = false
                    }
                }
            };

            var apps = InvokeGetAuthApps(details);

            Assert.That(apps, Has.Count.EqualTo(1));
            Assert.That(apps[0].Id, Is.EqualTo("connector-github"));
        }

        [Test]
        public void ReadPluginDetails_RefreshesAuthorizationActionAfterCachingApps()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "ReadPluginDetailsAsync");

            Assert.That(body, Does.Contain("CacheAuthApps"));
            Assert.That(body, Does.Contain("RefreshActionButtons"));
            Assert.That(
                body.IndexOf("CacheAuthApps", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("RefreshActionButtons", StringComparison.Ordinal)));
        }

        [Test]
        public void OpenAuth_RefreshesOnInstallAuthorizationOnlyAfterExplicitAction()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var clickBody = ExtractMethodBody(source, "OpenAuthButton_Click");
            var openBody = ExtractMethodBody(source, "OpenSelectedPluginAuthAsync");
            var loadBody = ExtractMethodBody(source, "LoadDataAsync");

            Assert.That(clickBody, Does.Contain("await OpenSelectedPluginAuthAsync()"));
            Assert.That(openBody, Does.Contain("CanRefreshPluginAuthorization"));
            Assert.That(openBody, Does.Contain("InstallAppServerPluginAsync(plugin)"));
            Assert.That(openBody, Does.Contain("CacheAuthApps"));
        }

        [Test]
        public void OpenAuth_RemoteOnInstallPluginCanRefreshAfterRestart()
        {
            var plugin = new AppServerPluginSummary
            {
                PluginId = "github@openai-curated-remote",
                Name = "github",
                Installed = true,
                SourceType = "remote",
                AuthPolicy = "ON_INSTALL"
            };

            Assert.That(InvokeCanRefreshPluginAuthorization(plugin), Is.True);

            plugin.Installed = false;
            Assert.That(InvokeCanRefreshPluginAuthorization(plugin), Is.False);

            plugin.Installed = true;
            plugin.AuthPolicy = "NONE";
            Assert.That(InvokeCanRefreshPluginAuthorization(plugin), Is.False);
        }

        [Test]
        public void RemoteCatalogRegistration_PrecedesTargetRuntimeInstall()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var canInstallBody = ExtractMethodBody(source, "CanInstallMarketItem");
            var sourceBody = ExtractMethodBody(source, "CanInstallMarketItemSource");
            var installBody = ExtractMethodBody(source, "InstallSelectedPluginAsync");

            Assert.That(
                canInstallBody.IndexOf("CanInstallMarketItemSource", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "远程市场目录项仍应经过统一安装资格判断。");
            Assert.That(
                sourceBody.IndexOf("return false", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "统一安装资格判断仍应覆盖远程市场目录项。");
            Assert.That(
                installBody.IndexOf("CanInstallMarketItemSource", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "安装入口也应再次校验运行时作用域，避免快捷键或旧状态绕过按钮禁用。");
            Assert.That(
                canInstallBody.IndexOf("AppServerHostPlatform.Windows", StringComparison.Ordinal),
                Is.LessThan(0),
                "WSL/远端 SSH 已支持目标端安装后，市场项不能再按 Windows 本机运行时一刀切禁用。");
        }

        [Test]
        public void RemoteCatalog_RequiresExplicitMarketplaceRegistrationBeforePluginInstall()
        {
            var mainSource = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var registrationSource = File.ReadAllText(Path.Combine(
                Path.GetDirectoryName(RepositorySource.FindSourceFile("PluginManagementForm.cs")),
                "PluginManagementForm.MarketplaceRegistration.cs"));
            var installBody = ExtractMethodBody(mainSource, "InstallSelectedPluginAsync");
            var registerBody = ExtractMethodBody(registrationSource, "RegisterSelectedMarketplaceAsync");

            Assert.That(mainSource, Does.Contain("_registerMarketplaceButton"));
            Assert.That(mainSource, Does.Contain("_registerMarketplaceMenuItem"));
            Assert.That(mainSource, Does.Contain("PluginManagementForm.Accessible.RegisterMarketplace"));
            Assert.That(registerBody, Does.Contain("RegisterMarketplaceFromItemAsync"));
            Assert.That(registrationSource, Does.Contain("AppServerMarketplaceRegistrationService.RegisterMarketplaceAsync"));
            Assert.That(registrationSource, Does.Contain("MyCodexOfficialMarketplaceBootstrap.RegisterPinnedMarketplaceAsync"));
            Assert.That(registrationSource, Does.Contain("MyCodexOfficialMarketplaceBootstrap.MarketplaceId"));
            Assert.That(registrationSource, Does.Contain("MyCodexOfficialMarketplaceBootstrap.MarketplaceSource"));
            Assert.That(installBody, Does.Not.Contain("InstallRemoteCatalogPluginAsync"));
            Assert.That(mainSource + registrationSource, Does.Not.Contain("RegisterAndFindPluginAsync"));
        }

        [Test]
        public void RegisterableRemoteCatalogItem_ExplainsRegistrationBeforeInstall()
        {
            var item = new PluginMarketListItem
            {
                PluginId = "superpowers",
                DisplayName = "Superpowers",
                MarketName = "MyCodex Official",
                AppServerSource = "https://market.example.com/mycodex-official.git",
                IsRemoteCatalog = true,
                AvailabilityStatus = PluginMarketAvailabilityStatus.InstallAvailable,
                RemoteEntry = new PluginMarketEntrySnapshot
                {
                    MarketId = "mycodex-official",
                    PluginId = "superpowers"
                }
            };

            var status = (string)GetPrivateStaticMethod("BuildMarketItemStatusText")
                .Invoke(null, new object[] { item });
            var unavailableMessage = (string)GetPrivateStaticMethod("GetInstallSourceUnavailableMessage")
                .Invoke(null, new object[] { item });

            Assert.That(
                status,
                Is.EqualTo(UiLocalization.Text(
                    "PluginManagementForm.Value.MarketplaceRegistrationRequired",
                    "Register Marketplace First")));
            Assert.That(
                unavailableMessage,
                Is.EqualTo(UiLocalization.Text(
                    "PluginManagementForm.Message.MarketplaceRegistrationRequired",
                    "Register this marketplace with the target App Server before installing its plugins.")));
            Assert.That(unavailableMessage, Does.Not.Contain("does not publish a secure App Server marketplace source"));
        }

        [Test]
        public void RemoteAppServerMarketplaceInstall_UsesRemoteInstallRequestInsteadOfLocalMarketplacePath()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var installBody = ExtractMethodBody(source, "InstallSelectedPluginAsync");
            var helperBody = ExtractMethodBody(source, "InstallAppServerPluginAsync");

            Assert.That(
                installBody.IndexOf("InstallAppServerPluginAsync(plugin)", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "App Server 市场条目安装应集中判断本地路径/远程市场两种协议来源。");
            Assert.That(
                helperBody.IndexOf("InstallRemotePluginAsync", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "OpenAI Curated Remote 这类无 marketplacePath 的远程市场条目应走 remoteMarketplaceName + pluginName 安装请求。");
            Assert.That(
                helperBody.IndexOf("InstallRemotePluginAsync", StringComparison.Ordinal),
                Is.LessThan(helperBody.IndexOf("InstallPluginAsync", StringComparison.Ordinal)),
                "远程市场条目不应先落入需要 marketplacePath 的本地安装请求。");
        }

        [Test]
        public void RemoteAppServerMarketplaceUninstallAndAuth_UseResolvedIdentityAliases()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var uninstallBody = ExtractMethodBody(source, "UninstallSelectedPluginAsync");
            var authBody = ExtractMethodBody(source, "GetCachedAuthApps");

            Assert.That(
                uninstallBody.IndexOf("ResolveUninstallPluginId", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "远程市场插件卸载应使用 App Server 期望的远程插件 ID，而不是直接发送列表显示 ID。");
            Assert.That(
                authBody.IndexOf("BuildCacheKeys", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "远程市场插件授权入口缓存应按完整 ID、基础 ID 等别名查找，避免刷新后按钮置灰。");
        }

        [Test]
        public void UninstallSelectedPluginAsync_VerifiesRefreshedStateBeforeReportingSuccess()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "UninstallSelectedPluginAsync");
            var reloadIndex = body.IndexOf("LoadDataAsync", StringComparison.Ordinal);
            var verificationIndex = body.IndexOf("PluginUninstallWorkflow.IsStillInstalled", StringComparison.Ordinal);
            var successIndex = body.IndexOf("PluginManagementForm.Status.PluginUninstalled.Template", StringComparison.Ordinal);

            Assert.That(reloadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(verificationIndex, Is.GreaterThan(reloadIndex));
            Assert.That(successIndex, Is.GreaterThan(verificationIndex));
        }

        [Test]
        public void UninstallAction_UsesSharedCapabilityAndAnnouncesPersistentOutcomes()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var operationBody = ExtractMethodBody(source, "UninstallSelectedPluginAsync");
            var buttonBody = ExtractMethodBody(source, "RefreshActionButtons");
            var menuBody = ExtractMethodBody(source, "RefreshContextMenuItems");
            var failureBlock = ExtractBlock(operationBody, "catch (Exception ex)");
            var successIndex = operationBody.IndexOf(
                "PluginManagementForm.Status.PluginUninstalled.Template",
                StringComparison.Ordinal);

            Assert.That(buttonBody, Does.Contain("_uninstallButton.Enabled = CanUninstallPlugin(plugin, isInstalledTab)"));
            Assert.That(menuBody, Does.Contain("_uninstallMenuItem.Enabled = CanUninstallPlugin(plugin, isInstalledTab)"));
            Assert.That(failureBlock, Does.Contain("true"));
            Assert.That(operationBody, Does.Contain("SetStatus(message, true)"));
            Assert.That(successIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                Regex.IsMatch(
                    operationBody.Substring(successIndex),
                    @"PluginManagementForm\.Status\.PluginUninstalled\.Template[\s\S]*?\)\s*,\s*true\s*\)"),
                Is.True,
                "Successful uninstall completion must be announced through the persistent status channel.");
            Assert.That(operationBody, Does.Not.Contain(".Focus()"));
        }

        [Test]
        public void MarketSourceTree_UsesRemoteCatalogBeforeAvailableItemFiltering()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "RebuildMarketSourceTreeForScope");

            Assert.That(
                body.IndexOf("GetRemoteMarketSummaries", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0),
                "市场来源应从远程市场原始数据生成，避免所有插件已安装后来源消失。");
            Assert.That(
                body.IndexOf("GetRemoteMarketSummaries", StringComparison.Ordinal),
                Is.LessThan(body.IndexOf("GetAvailableMarketItems", StringComparison.Ordinal)),
                "市场来源应先读取远程原始来源，再合并可安装项来源。");
        }

        [Test]
        public void MarketCategories_IncludeRuntimeMarketplacePluginCategories()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "GetLoadedCategoriesForMarket");

            Assert.That(
                body,
                Does.Contain("GetAvailableMarketItems()"),
                "OpenAI Curated Remote 等 App Server 市场分类来自 plugin/list 的 Category 字段。");
        }

        [Test]
        public void MarketBrowse_UsesTreeAndExplicitEnterInsteadOfSourceCombo()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var constructorBody = ExtractConstructorBody(source, "PluginManagementForm");
            var keyDownBody = ExtractMethodBody(source, "MarketSourceTreeView_KeyDown");

            Assert.That(source, Does.Contain("TreeView _marketSourceTreeView"));
            Assert.That(source, Does.Not.Contain("ComboBox _marketSourceComboBox"));
            Assert.That(constructorBody, Does.Contain("_marketSourceTreeView.KeyDown += MarketSourceTreeView_KeyDown"));
            Assert.That(constructorBody, Does.Not.Contain("_marketSourceTreeView.AfterSelect"));
            Assert.That(keyDownBody, Does.Contain("BrowseSelectedMarketScopeAsync"));
        }

        [Test]
        public void MarketBrowse_DoesNotAnnounceTransientStatus()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "BrowseMarketScopeAsync");

            Assert.That(body, Does.Not.Contain(", true)"));
        }

        [Test]
        public void MarketLayout_DoesNotSetSplitDistanceOrPanelMinSizeBeforeContainerHasWidth()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "BuildLayout");

            Assert.That(body, Does.Not.Contain("SplitterDistance ="));
            Assert.That(body, Does.Not.Contain("Panel1MinSize ="));
            Assert.That(body, Does.Not.Contain("Panel2MinSize ="));
        }

        [Test]
        public void LoadDataAsync_UsesCatalogAndInstalledOfficialObservations()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "LoadDataAsync");
            var observationSource = RepositorySource.ReadSourceFile("AppServerInstalledPluginObservationService.cs");

            Assert.That(body, Does.Contain("ListPluginsAsync"));
            Assert.That(body, Does.Contain("LoadInstalledPluginObservationAsync"));
            Assert.That(observationSource, Does.Contain("GetInstalledPluginsAsync"));
            Assert.That(source, Does.Contain("_installedPlugins"));
        }

        [Test]
        public void LoadDataAsync_ExternalRuntimeWithoutInstalledContractRetainsReadOnlyCatalogObservation()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "LoadInstalledPluginObservationAsync");
            var observationSource = RepositorySource.ReadSourceFile("AppServerInstalledPluginObservationService.cs");

            Assert.That(body, Does.Contain("AppServerInstalledPluginObservationService.LoadAsync"));
            Assert.That(observationSource, Does.Contain("SupportsInstalledPluginObservation"));
            Assert.That(observationSource, Does.Contain("plugin.Installed"));
            Assert.That(observationSource, Does.Contain("GetInstalledPluginsAsync"));
            Assert.That(observationSource, Does.Not.Contain("Directory."));
            Assert.That(observationSource, Does.Not.Contain("File."));
        }

        [Test]
        public void RemoteCatalogStatus_UsesLocalVersionFromPluginList()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, "GetAvailableMarketItems");

            Assert.That(body, Does.Contain("FindInstalledPlugin"));
            Assert.That(body, Does.Contain("LocalVersion"));
            Assert.That(body, Does.Not.Contain("RuntimeVersion"));
        }

        [Test]
        public void MarketplaceRefreshAction_UsesOfficialApiAndAccessibleButton()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var handlerBody = ExtractMethodBody(source, "RefreshMarketplaceButton_Click");
            var operationBody = ExtractMethodBody(source, "RefreshMarketplaceAsync");

            Assert.That(source, Does.Contain("_refreshMarketplaceButton"));
            Assert.That(source, Does.Contain("PluginManagementForm.Button.RefreshMarketplace"));
            Assert.That(source, Does.Contain("PluginManagementForm.Accessible.RefreshMarketplace"));
            Assert.That(handlerBody, Does.Contain("RefreshMarketplaceAsync"));
            Assert.That(operationBody, Does.Contain("UpgradeMarketplaceAsync"));
            Assert.That(operationBody, Does.Contain("_refreshMarketplaceButton.Enabled = false"));
            Assert.That(operationBody, Does.Contain("LoadDataAsync"));
            Assert.That(operationBody.IndexOf("MessageBox.Show", StringComparison.Ordinal), Is.LessThan(0));
        }

        [Test]
        public void PluginManagement_RemovesUnsupportedGlobalEnablementAction()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");

            Assert.That(source, Does.Not.Contain("_toggleEnabledButton"));
            Assert.That(source, Does.Not.Contain("_toggleEnabledMenuItem"));
            Assert.That(source, Does.Not.Contain("ToggleSelectedPluginEnabledAsync"));
            Assert.That(source, Does.Not.Contain("CanTogglePluginEnabled"));
            Assert.That(source, Does.Not.Contain("PluginEnablementUnavailable"));
        }

        [Test]
        public void PluginFolderResolution_UsesTrackedCachePathWhenProtocolPathIsMissing()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex-PluginFolderResolution-" + Guid.NewGuid().ToString("N"));
            var trackedPath = Path.Combine(root, "1.0.0");
            var cachePath = Path.Combine(root, "cache-root");
            try
            {
                Directory.CreateDirectory(trackedPath);
                Directory.CreateDirectory(cachePath);

                var resolved = (string)GetPrivateStaticMethod("ResolveExistingPluginFolderPath")
                    .Invoke(null, new object[] { null, trackedPath, cachePath });

                Assert.That(resolved, Is.EqualTo(trackedPath));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void PluginFolderAction_UsesResolvedInstalledFolderForButtonMenuAndOpen()
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var buttonBody = ExtractMethodBody(source, "RefreshActionButtons");
            var menuBody = ExtractMethodBody(source, "RefreshContextMenuItems");
            var openBody = ExtractMethodBody(source, "OpenSelectedPluginFolder");

            Assert.That(buttonBody, Does.Contain("ResolveSelectedPluginFolderPath"));
            Assert.That(menuBody, Does.Contain("ResolveSelectedPluginFolderPath"));
            Assert.That(openBody, Does.Contain("ResolveSelectedPluginFolderPath"));
        }

        [TestCase("TabControl_SelectedIndexChanged")]
        [TestCase("PluginListView_SelectedIndexChanged")]
        public void FocusAndFilterHandlers_DoNotTogglePluginEnabledState(string methodName)
        {
            var source = RepositorySource.ReadSourceFile("PluginManagementForm.cs");
            var body = ExtractMethodBody(source, methodName);

            Assert.That(body, Does.Not.Contain("ToggleSelectedPluginEnabledAsync"));
            Assert.That(body, Does.Not.Contain("SetPluginEnabledAsync"));
        }

        [Test]
        public void PluginDetails_ProvidesRemoteSkillPreviewWithoutProtocolMethodText()
        {
            var source = RepositorySource.ReadSourceFile("PluginDetailsForm.cs");

            Assert.That(source, Does.Contain("ReadPluginSkillAsync"));
            Assert.That(source, Does.Contain("PluginDetailsForm.Button.PreviewSkill"));
            Assert.That(source, Does.Contain("PluginDetailsForm.Accessible.Skill"));
            Assert.That(source, Does.Not.Contain("plugin/skill/read"));
            Assert.That(source, Does.Not.Contain("resource URI"));
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                Assert.Fail("源文件内容为空。");
            }

            var signaturePattern = new Regex(
                @"private\s+(?:static\s+)?(?:async\s+)?[\w\.<>,]+\s+" + Regex.Escape(methodName) + @"\s*\(",
                RegexOptions.CultureInvariant);
            var signatureMatch = signaturePattern.Match(source);
            if (!signatureMatch.Success)
            {
                Assert.Fail("未找到方法：" + methodName);
            }

            var bodyStart = source.IndexOf('{', signatureMatch.Index);
            if (bodyStart < 0)
            {
                Assert.Fail("未找到方法体开始：" + methodName);
            }

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(bodyStart + 1, i - bodyStart - 1);
                    }
                }
            }

            Assert.Fail("未找到方法体结束：" + methodName);
            return string.Empty;
        }

        private static string ExtractConstructorBody(string source, string typeName)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                Assert.Fail("源文件内容为空。");
            }

            var signaturePattern = new Regex(
                @"public\s+" + Regex.Escape(typeName) + @"\s*\(",
                RegexOptions.CultureInvariant);
            var signatureMatch = signaturePattern.Match(source);
            if (!signatureMatch.Success)
            {
                Assert.Fail("未找到构造函数：" + typeName);
            }

            var bodyStart = source.IndexOf('{', signatureMatch.Index);
            if (bodyStart < 0)
            {
                Assert.Fail("未找到构造函数体开始：" + typeName);
            }

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(bodyStart + 1, i - bodyStart - 1);
                    }
                }
            }

            Assert.Fail("未找到构造函数体结束：" + typeName);
            return string.Empty;
        }

        private static bool InvokePluginIdentityMatches(AppServerPluginSummary plugin, string pluginId)
        {
            return (bool)GetPrivateStaticMethod("PluginIdentityMatches")
                .Invoke(null, new object[] { plugin, pluginId });
        }

        private static string InvokeGetMarketItemPluginIdentity(PluginMarketListItem item)
        {
            return (string)GetPrivateStaticMethod("GetMarketItemPluginIdentity")
                .Invoke(null, new object[] { item });
        }

        private static void InvokeAddPluginIdentityKeys(HashSet<string> keys, AppServerPluginSummary plugin)
        {
            GetPrivateStaticMethod("AddPluginIdentityKeys")
                .Invoke(null, new object[] { keys, plugin });
        }

        private static bool InvokeAddPluginIdentityKeysForCandidate(
            HashSet<string> keys,
            string pluginId,
            string displayName)
        {
            return (bool)GetPrivateStaticMethod("AddPluginIdentityKeysForCandidate")
                .Invoke(null, new object[] { keys, pluginId, displayName });
        }

        private static List<AppServerPluginSummary> InvokeDeduplicatePluginSummariesForView(
            List<AppServerPluginSummary> plugins)
        {
            return (List<AppServerPluginSummary>)GetPrivateStaticMethod("DeduplicatePluginSummariesForView")
                .Invoke(null, new object[] { plugins });
        }

        private static List<AppServerPluginAppInfo> InvokeGetAuthApps(AppServerPluginDetails details)
        {
            return (List<AppServerPluginAppInfo>)GetPrivateStaticMethod("GetAuthApps")
                .Invoke(null, new object[] { details });
        }

        private static bool InvokeCanRefreshPluginAuthorization(AppServerPluginSummary plugin)
        {
            return (bool)GetPrivateStaticMethod("CanRefreshPluginAuthorization")
                .Invoke(null, new object[] { plugin });
        }

        private static bool InvokeIsPluginCacheInUseError(Exception exception)
        {
            return (bool)GetPrivateStaticMethod("IsPluginCacheInUseError")
                .Invoke(null, new object[] { exception });
        }

        private static MethodInfo GetPrivateStaticMethod(string methodName)
        {
            var method = typeof(PluginManagementForm).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "未找到私有静态方法：" + methodName);
            return method;
        }

        private static string ExtractBlock(string source, string blockHeader)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                Assert.Fail("源代码片段为空。");
            }

            var headerIndex = source.IndexOf(blockHeader, StringComparison.Ordinal);
            if (headerIndex < 0)
            {
                Assert.Fail("未找到代码块：" + blockHeader);
            }

            var blockStart = source.IndexOf('{', headerIndex);
            if (blockStart < 0)
            {
                Assert.Fail("未找到代码块开始：" + blockHeader);
            }

            var depth = 0;
            for (var i = blockStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(blockStart + 1, i - blockStart - 1);
                    }
                }
            }

            Assert.Fail("未找到代码块结束：" + blockHeader);
            return string.Empty;
        }
    }
}
