using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests.SourceGuards
{
    /// <summary>
    /// Identifiers and files of removed features. None of them may reappear anywhere in production C# source.
    /// Add an entry here instead of a Does.Not.Contain assertion against one file when a feature is retired.
    /// </summary>
    [TestFixture]
    [Category(SourceGuardCategory.Retired)]
    public sealed class RetiredIdentifierGuardTests
    {
        private static readonly string[] RetiredIdentifiers =
        {
            // App Server client: authentication materialization and shared Codex home
            "AuthenticationMaterialization",
            "AppServerRuntimeConfigMaterializer",
            "AuthenticationEnvironment",
            "SourceMaterialization",
            "AuthenticationSharedSkills",
            "BuildAuthenticationEnvironmentVariablesAsync",
            "_currentAuthenticationModelCatalogJsonPath",
            "_lastAuthenticationMaterializationConnectionKey",
            "SharedWindowsHome",
            "GetSharedCodexHomeDirectory",
            "GetAuthenticationProfileCodexHomeDirectory",
            "ApplyLocalWindowsHomeEnvironment",
            "ApplyWslCodexHomeEnvironment",
            "MYCODEX_WSL_CODEX_HOME",
            "MYCODEX_WSL_CODEX_HOME_STRATEGY",
            "SynchronizeCurrentAuthenticationSharedSkills",
            "RepairLocalPluginMarketManifests",

            // Official config ownership
            "BuildLegacyContextOverrideCleanupEdits",
            "HostSettingsOfficial",

            // Startup composition and retired stores
            "JsonAuthenticationProfileStore",
            "JsonChatGptAuthBackupStore",
            "JsonAuthenticationSourceSelectionStore",

            // Authentication bootstrap and user center
            "AuthenticationSource",
            "ChatGptAuthBackup",
            "IChatGptAuthBackupStore",
            "UserCenterAuthenticationDisplayMerger",

            // Pure client architecture
            "chatgpt-auth-backups",
            "BuiltInDefaultReasoningEffort",
            "BuildFallbackReasoningEfforts",
            "BuildFallbackEffortChoices",

            // Session settings and model sources
            "Settings.Menu.ExternalAgentConfigImport",
            "Settings.Menu.AppServerQuickFileSearch",
            "AppServerQuickFileSearchMenuItem_Click",
            "AppServerFuzzyFileSearchForm",
            "_appServerFilesMenuItem",
            "Settings.Menu.AppServerFiles",
            "AppServerFilesMenuItem_Click",
            "LoadEffectiveAuthenticationProfileSnapshotSafe",
            "OpenRouterSourcePlanner",
            "ModelConversionSourcePlanner",
            "JsonRuntimeSessionDefaultsStore",
            "RemoteServiceFeatureGate",

            // Host settings
            "SetConfiguredDefaultModel",
            "AuthenticationSourceKind",
            "LoadCurrentAuthenticationSourceSelectionForDiagnostics",

            // Interaction modes and Office categories
            "_switchInteractionModeMenuItem",
            "SwitchInteractionModeMenuItem_Click",
            "UpdateModeMenuState",
            "_officeDocumentsMenuItem",
            "OfficeDocumentTaskForm",
            "OpenOfficeDocumentTasks",
            "IWorkbenchHost",
            "OfficeRuntimeSettings",
            "EnforceBundledAppServerRuntime",
            "_appConfigSnapshot.OfficeRootPath",
            "GetSessionRuntimeScopeId",
            "BuildDetachedComposeDraftScopeKey",
            "EnsureOfficeCategoriesDiscovered",
            "NormalizeAndPruneOfficeCategories",
            "EnsureOfficeRootDirectory",
            "RemoveSessionsForRemovedOfficeWorkspaces",
            "RefreshOfficeCategoriesAsync",
            "CreateOfficeCategoryAsync",
            "CreateAutomationFromOfficeWorkspace",
            "MainForm.Accessible.ModeTabs",
            "PrepareRuntimeWindowsForTransition",
            "ModeFocusRole",
            "RestoreCurrentModeFocusRole",
            "_officeCategory",
            "_deriveMenuItem",
            "_officeUnboundComposeDraft",
            "MainForm.Accessible.OfficeCategories",
            "MainForm.Label.OfficeCategory",
            "ManagedOfficeCategory",
            "InvalidateAvailableModelsForModeSwitch",

            // Office internal document tools
            "office/create_document",
            "office/read_document",
            "office/edit_document",

            // Main menu and shortcuts
            "MainMenu.RuntimeFileExplorer",
            "RuntimeFileExplorerForm",
            "ShowRuntimeFileExplorer",
            "_openRouterMenuItem",
            "_gatewayCenterMenuItem",
            "OnOpenRouterMenuItemClick",
            "OnGatewayCenterMenuItemClick",
            "AreEquivalentAuthenticationSourceSelections",
            "AddBuiltInSourceModels",
            "OpenRouterSourcePlanner.BuildEnabledModels",
            "ModelConversionSourcePlanner.BuildEnabledModels",
            "RefreshCurrentAuthenticationSourceSelectionAsync",

            // Main window structure
            "_turnDiffSnapshots",
            "_viewSelectedTurnChangesMenuItem",
            "AuthenticationProfileStore",
            "OpenRouterAccountStore",
            "RuntimeSessionDefaultsStore",
            "HandleAutomationRunCompletedNotification",
            "_activityTextBox",
            "_activityListBox",
            "ActivityVirtualBrowseItemAccessibleObject",
            "ActivityRecordReadRequested",
            "_manualReadChatHistoryIndex",
            "_chatFiltersMenuItem",
            "ChatFilterMenuItem_Click",
            "CreateChatFilterMenuItem",
            "MainForm.Context.MessageFilter",
            "MainForm.Context.Filter.",

            // Workspace and session context menus
            "_officeCategoryNewMenuItem",
            "MainForm.OfficeCategory.New",
            "MainForm.OfficeCategory.OpenFolder",
            "MainForm.OfficeCategory.Refresh",
            "OnOfficeCategoryNewMenuItemClick",
            "_workspaceDeletePermanentlyMenuItem",
            "_sessionDeletePermanentlyMenuItem",
            "MainForm.Workspace.DeletePermanent",
            "MainForm.Session.DeletePermanent",

            // Tray notifications
            "_desktopNotificationChannel",

            // Session workbench
            "ReleaseSessionManualBrowseContextIfUnused",

            // Git workbench
            "EnsureSelectedDiffLoadedOnDiffFocusAsync",

            // App Server file browser
            "InsertPath",
            "InsertSelectedPaths",

            // Update installer launch
            "DeferredUpdateInstallerLauncher.ScheduleAsync",
            "StartUpdateInstallerAndExit",

            // Plugin and skill marketplace services
            "PluginMarketMirrorService",
            "PluginMarketInstallService",
            "SkillMarketInstallService",
            "PluginMarketInstalledStateService",
            "SkillMarketTargetInstaller",
            "skill-market",
            "SharedSkillPathResolver",
            "IPluginMarketInstallService",
            "PluginRuntimeEnabledStateService",
            "AddRuntimeInstalledPluginAttachments",
            "TryLoadRuntimeInstalledSnapshotAsync",

            // Plugin management
            "InstallRemoteCatalogPluginAsync",
            "RegisterAndFindPluginAsync",
            "_toggleEnabledButton",
            "_toggleEnabledMenuItem",
            "ToggleSelectedPluginEnabledAsync",
            "CanTogglePluginEnabled",
            "PluginEnablementUnavailable",
            "SetPluginEnabledAsync",

            // Skills management
            "SkillMarket",
            "InstallSelectedMarketSkillAsync",
            "SkillMarketCatalogClient",
            "RemoteMacSkillFileEditor",
            "SkillsManagementForm.Header",
            "SkillsManagementForm.Tip",
            "SkillsManagementForm.Accessible.Tabs",
            "SkillUserUninstallService",
            "RemoveInstalledSkillBySourcePath",

            // Skill mirrors
            "_trySynchronizeCurrentAuthenticationSharedSkillsAsync",
            "_tryRepairLocalPluginMarketManifests",

            // MCP management
            "ReloadServerConfigAsync"
        };

        private static readonly string[] RetiredSourceFileNames =
        {
            // Main window structure
            "DiffWorkbenchForm.cs",

            // Plugin and skill marketplace services
            "LocalPluginSkillMirrorService.cs",
            "LocalSharedSkillMirrorService.cs",
            "SkillMarketTargetInstaller.cs",
            "SkillMarketInstallService.cs",
            "PluginMarketTargetMirrorInstaller.cs",
            "PluginMarketInstallService.cs",

            // Startup composition and retired stores
            "NodeRuntimeSources.cs",

            // Skills management
            "SkillMarketListComposer.cs",
            "ISkillMarketCatalogClient.cs",

            // Unix remote Git services
            "RemoteMacGitRepositoryService.cs",
            "WslGitRepositoryService.cs"
        };

        private static readonly string[][] RetiredRepositoryFiles =
        {
            // Startup composition and retired stores
            new[] { "tools", "runtime", "node-runtime.sha256" },
            new[] { "tools", "runtime", "node-runtime.version" },
            new[] { "tools", "runtime", "prepare-node-runtime.ps1" }
        };

        [Test]
        public void RetiredIdentifiers_AreListedOnce()
        {
            var duplicates = RetiredIdentifiers
                .GroupBy(identifier => identifier, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.That(duplicates, Is.Empty);
        }

        [Test]
        public void RetiredIdentifiers_DoNotReappearInProductionSource()
        {
            var violations = new List<string>();
            foreach (var path in RepositorySource.FindAllSourceFiles())
            {
                var source = File.ReadAllText(path);
                foreach (var identifier in RetiredIdentifiers)
                {
                    if (source.IndexOf(identifier, StringComparison.Ordinal) >= 0)
                    {
                        violations.Add(identifier + " in " + path.Substring(RepositorySource.Root.Length + 1));
                    }
                }
            }

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void RetiredSourceFiles_DoNotReappear()
        {
            var reappeared = RetiredSourceFileNames
                .Where(fileName => RepositorySource.FindSourceFile(fileName) != null)
                .Concat(RetiredRepositoryFiles
                    .Where(parts => File.Exists(RepositorySource.GetPath(parts)))
                    .Select(parts => string.Join("/", parts)))
                .ToArray();

            Assert.That(reappeared, Is.Empty);
        }
    }
}
