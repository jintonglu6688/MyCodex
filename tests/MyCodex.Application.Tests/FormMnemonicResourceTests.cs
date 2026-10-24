using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class FormMnemonicResourceTests
    {
        private static readonly Regex MnemonicPattern = new Regex(@"\(&(?<key>[A-Za-z0-9])\)", RegexOptions.Compiled);

        [Category(SourceGuardCategory.Assets)]
        [TestCase("GuardianDeniedActionApprovalForm", new[]
        {
            "GuardianDeniedActionApprovalForm.Button.Approve",
            "Common.Cancel"
        })]
        [TestCase("ReviewStartForm", new[]
        {
            "ReviewStartForm.Label.Target",
            "ReviewStartForm.Label.Mode",
            "ReviewStartForm.Label.Branch",
            "ReviewStartForm.Label.Commit",
            "ReviewStartForm.Label.Instructions",
            "ReviewStartForm.Button.Start",
            "ReviewStartForm.Button.Cancel"
        })]
        [TestCase("GitCloneForm", new[]
        {
            "GitCloneForm.Label.RepositoryUrl",
            "GitCloneForm.Label.TargetParent",
            "GitCloneForm.Label.TargetFolder",
            "GitCloneForm.Button.RefreshRepositories",
            "GitCloneForm.Button.Browse",
            "GitCloneForm.Button.Clone",
            "GitCloneForm.Button.CancelClone",
            "GitCloneForm.Button.CopyOutput",
            "GitCloneForm.Button.OpenWorkspace",
            "GitCloneForm.Button.OpenGitWorkbench",
            "GitCloneForm.Button.Close",
            "GitCloneForm.Check.RecurseSubmodules",
            "GitCloneForm.Check.UseSshCloneUrl"
        })]
        [TestCase("ActivitySettingsForm", new[]
        {
            "ActivitySettingsForm.Check.Thinking",
            "ActivitySettingsForm.Check.Plan",
            "ActivitySettingsForm.Check.Tool",
            "ActivitySettingsForm.Check.Command",
            "ActivitySettingsForm.Check.File",
            "ActivitySettingsForm.Check.Notice",
            "ActivitySettingsForm.Check.Error",
            "ActivitySettingsForm.Check.Approval",
            "ActivitySettingsForm.Check.Account",
            "ActivitySettingsForm.Check.ProtocolNotice",
            "ActivitySettingsForm.Check.ProtocolWarning",
            "ActivitySettingsForm.Check.ClearOnNewTurn"
        })]
        [TestCase("NotificationSettingsForm", new[]
        {
            "NotificationSettingsForm.Check.TurnCompletedSound",
            "NotificationSettingsForm.Check.SubAgentTurnCompletedSound",
            "NotificationSettingsForm.Check.ApprovalRequestSound",
            "NotificationSettingsForm.Check.SendMessageSound"
        })]
        [TestCase("GitWorkbenchForm", new[]
        {
            "GitWorkbenchForm.Label.CommitSubject",
            "GitWorkbenchForm.Label.CommitBody",
            "GitWorkbenchForm.Button.Initialize",
            "GitWorkbenchForm.Button.Refresh",
            "GitWorkbenchForm.Button.Fetch",
            "GitWorkbenchForm.Button.Pull",
            "GitWorkbenchForm.Button.ContinueSequencer",
            "GitWorkbenchForm.Button.AbortSequencer",
            "GitWorkbenchForm.Button.SkipSequencer",
            "GitWorkbenchForm.Button.Commit",
            "GitWorkbenchForm.Button.Push",
            "GitWorkbenchForm.Button.Publish",
            "GitWorkbenchForm.Button.Remotes",
            "GitWorkbenchForm.Button.BranchesAndCommits",
            "GitWorkbenchForm.Button.Stashes"
        })]
        [TestCase("GitRemoteManagementForm", new[]
        {
            "GitRemoteManagementForm.Button.Refresh",
            "GitRemoteManagementForm.Button.Add",
            "GitRemoteManagementForm.Button.Edit",
            "GitRemoteManagementForm.Button.Delete",
            "GitRemoteManagementForm.Button.CopyName",
            "GitRemoteManagementForm.Button.CopyFetchUrl",
            "GitRemoteManagementForm.Button.CopyPushUrl"
        })]
        [TestCase("GitRemoteEditForm", new[]
        {
            "GitRemoteEditForm.Label.Name",
            "GitRemoteEditForm.Label.FetchUrl",
            "GitRemoteEditForm.Label.PushUrl",
            "GitRemoteEditForm.Button.Save",
            "GitRemoteEditForm.Button.Cancel"
        })]
        [TestCase("GitUpstreamEditForm", new[]
        {
            "GitUpstreamEditForm.Label.Branch",
            "GitUpstreamEditForm.Label.Upstream",
            "GitUpstreamEditForm.Button.Save",
            "GitUpstreamEditForm.Button.Cancel"
        })]
        [TestCase("GitCommitHistoryForm", new[]
        {
            "GitCommitHistoryForm.Label.HistoryRef",
            "GitCommitHistoryForm.Button.Refresh",
            "GitCommitHistoryForm.Button.LoadDetails"
        })]
        [TestCase("GitBranchIntegrateForm", new[]
        {
            "GitBranchIntegrateForm.Label.CurrentBranch",
            "GitBranchIntegrateForm.Label.TargetBranch",
            "GitBranchIntegrateForm.Label.Comparison",
            "GitBranchIntegrateForm.Label.Mode",
            "GitBranchIntegrateForm.Check.FetchFirst",
            "GitBranchIntegrateForm.Button.Start",
            "GitBranchIntegrateForm.Button.Cancel"
        })]
        [TestCase("GitRemoteRepositoriesForm", new[]
        {
            "GitRemoteRepositoriesForm.Label.Filter",
            "GitRemoteRepositoriesForm.Button.Refresh",
            "GitRemoteRepositoriesForm.Button.CreateRepository",
            "GitRemoteRepositoriesForm.Button.Close"
        })]
        [TestCase("GitEnvironmentForm", new[]
        {
            "GitEnvironmentForm.Button.Refresh",
            "GitEnvironmentForm.Button.SetName",
            "GitEnvironmentForm.Button.SetEmail",
            "GitEnvironmentForm.Button.CopySummary",
            "GitEnvironmentForm.Button.CopyDetails",
            "GitEnvironmentForm.Button.Close"
        })]
        [TestCase("GitRemoteCredentialsForm", new[]
        {
            "GitRemoteCredentialsForm.Button.Close"
        })]
        [TestCase("AuthenticationBootstrapForm", new[]
        {
            "AuthenticationBootstrapForm.Button.ChatGpt",
            "AuthenticationBootstrapForm.Button.DeviceCode",
            "AuthenticationBootstrapForm.Button.ApiKey",
            "AuthenticationBootstrapForm.Button.Refresh",
            "AuthenticationBootstrapForm.Button.CancelLogin",
            "AuthenticationBootstrapForm.Button.Close"
        })]
        [TestCase("PluginManagementForm", new[]
        {
            "PluginManagementForm.Button.Refresh",
            "PluginManagementForm.Button.SearchMarket",
            "PluginManagementForm.Button.RegisterMarketplace",
            "PluginManagementForm.Button.Install",
            "PluginManagementForm.Button.Uninstall",
            "PluginManagementForm.Button.OpenAuth",
            "PluginManagementForm.Button.OpenFolder"
        })]
        [TestCase("PluginDetailsForm", new[]
        {
            "PluginDetailsForm.Button.PreviewSkill",
            "PluginDetailsForm.Button.Close"
        })]
        [TestCase("PluginSkillPreviewForm", new[]
        {
            "PluginSkillPreviewForm.Button.OK"
        })]
        [TestCase("SkillsManagementForm", new[]
        {
            "SkillsManagementForm.Button.Refresh",
            "SkillsManagementForm.Button.New",
            "SkillsManagementForm.Button.Edit",
            "SkillsManagementForm.Button.Enable",
            "SkillsManagementForm.Button.Disable",
            "SkillsManagementForm.Button.OpenFolder",
            "SkillsManagementForm.Button.Errors"
        })]
        [TestCase("McpManagementForm", new[]
        {
            "McpManagementForm.Button.Refresh",
            "McpManagementForm.Button.Add",
            "McpManagementForm.Button.Settings",
            "McpManagementForm.Button.AdvancedDiagnostics",
            "McpManagementForm.Button.Login",
            "McpManagementForm.Button.Enable",
            "McpManagementForm.Button.Disable",
            "McpManagementForm.Button.Delete",
            "McpManagementForm.Native.Import",
            "McpManagementForm.Native.Reload"
        })]
        [TestCase("ThreadImportForm", new[]
        {
            "ThreadImportForm.Button.Refresh",
            "ThreadImportForm.Button.SelectAll",
            "ThreadImportForm.Button.ClearSelection",
            "ThreadImportForm.Button.CurrentWorkspace",
            "ThreadImportForm.Button.Import"
        })]
        [TestCase("ThreadAssociationForm", new[]
        {
            "ThreadAssociationForm.Label.Filter",
            "ThreadAssociationForm.Button.Refresh",
            "ThreadAssociationForm.Button.SelectAll",
            "ThreadAssociationForm.Button.ClearSelection",
            "ThreadAssociationForm.Button.Associate",
            "ThreadAssociationForm.Button.Close"
        })]
        [TestCase("ExternalAgentConfigImportForm", new[]
        {
            "ExternalAgentConfigImportForm.Option.IncludeHome",
            "ExternalAgentConfigImportForm.Option.CurrentWorkspace",
            "ExternalAgentConfigImportForm.Button.Detect",
            "ExternalAgentConfigImportForm.Button.SelectAll",
            "ExternalAgentConfigImportForm.Button.ClearSelection",
            "ExternalAgentConfigImportForm.Button.Import",
            "ExternalAgentConfigImportForm.Button.Close"
        })]
        [TestCase("MainForm.PhoneConnection", new[]
        {
            "Settings.Menu.Options",
            "Settings.Menu.Targets",
            "Settings.Menu.Extensions",
            "Settings.Menu.RemoteService"
        })]
        [TestCase("PhoneConnection.Actions", new[]
        {
            "RemoteServiceManagementForm.Button.EnableTarget",
            "RemoteServiceManagementForm.Button.GeneratePairingCode",
            "RemoteServiceManagementForm.Button.ServerSettings",
            "RemoteServiceManagementForm.Button.RefreshDevices",
            "RemoteServiceManagementForm.Button.Diagnostics",
            "RemoteServiceManagementForm.Button.Close"
        })]
        [TestCase("PhoneConnectionSettings.Embedded", new[]
        {
            "SharedRemoteService.Button.Apply",
            "SharedRemoteService.Button.Close",
            "SharedRemoteService.Button.Firewall"
        })]
        [TestCase("EmbeddedRelayFirewallForm", new[]
        {
            "TargetManagerForm.Button.Refresh",
            "RemoteServiceManagementForm.Button.AllowFirewall",
            "SharedRemoteService.Button.Close"
        })]
        [TestCase("TargetManagerForm", new[]
        {
            "TargetManagerForm.Button.Refresh",
            "TargetManagerForm.Button.AddWsl",
            "TargetManagerForm.Button.AddSsh",
            "TargetManagerForm.Button.Edit",
            "TargetManagerForm.Removal.Button.Remove",
            "TargetManagerForm.Button.Close"
        })]
        [TestCase("TargetSettingsForm.Wsl", new[]
        {
            "TargetSettingsForm.Label.Name",
            "TargetSettingsForm.Button.RefreshDistributions",
            "WslAppServerSettingsForm.Button.Detect",
            "TargetSettingsForm.Button.Save",
        })]
        [TestCase("TargetSettingsForm.Windows", new[]
        {
            "TargetSettingsForm.Label.Name",
            "WslAppServerSettingsForm.Button.BrowseCustom",
            "WslAppServerSettingsForm.WindowsSandbox.Button.Detect",
            "WslAppServerSettingsForm.WindowsSandbox.Button.Setup",
            "TargetSettingsForm.Button.Save",
        })]
        [TestCase("TargetSettingsForm.Ssh", new[]
        {
            "TargetSettingsForm.Label.Name",
            "WslAppServerSettingsForm.Button.BrowseRemotePrivateKey",
            "WslAppServerSettingsForm.Button.Detect",
            "TargetSettingsForm.Button.Save",
        })]
        [TestCase("WslAppServerSettingsForm.WindowsSandbox", new[]
        {
            "WslAppServerSettingsForm.WindowsSandbox.Button.Detect",
            "WslAppServerSettingsForm.WindowsSandbox.Button.Setup"
        })]
        [TestCase("RuntimeFileBrowserForm", new[]
        {
            "RuntimeFileBrowserForm.Label.Address",
            "RuntimeFileBrowserForm.Label.TargetFolder",
            "RuntimeFileBrowserForm.Button.Go",
            "RuntimeFileBrowserForm.Button.Home",
            "RuntimeFileBrowserForm.Button.Up",
            "RuntimeFileBrowserForm.Button.Refresh",
            "RuntimeFileBrowserForm.Button.SelectFolder"
        })]
        [TestCase("AppServerFileBrowserForm", new[]
        {
            "AppServerFileBrowserForm.Label.Address",
            "AppServerFileBrowserForm.Label.Search",
            "AppServerFileBrowserForm.Button.Home",
            "AppServerFileBrowserForm.Button.Up",
            "AppServerFileBrowserForm.Button.Refresh",
            "AppServerFileBrowserForm.Button.Search",
            "AppServerFileBrowserForm.Menu.NewFolder",
            "AppServerFileBrowserForm.Menu.NewFile",
            "AppServerFileBrowserForm.Menu.Delete",
            "AppServerFileBrowserForm.Menu.Paste",
            "AppServerFileBrowserForm.Button.Preview",
            "AppServerFileBrowserForm.Button.Copy",
            "AppServerFileBrowserForm.Button.CopyPath",
            "AppServerFileBrowserForm.Button.Attach"
        })]
        [TestCase("AppServerFilePreviewForm", new[]
        {
            "AppServerFilePreviewForm.Button.Reload",
            "AppServerFilePreviewForm.Button.Save",
            "AppServerFilePreviewForm.Button.FindPrevious",
            "AppServerFilePreviewForm.Button.FindNext",
            "AppServerFilePreviewForm.Button.Replace",
            "AppServerFilePreviewForm.Button.ReplaceAll",
            "AppServerFilePreviewForm.Button.GoToLine",
            "AppServerFilePreviewForm.Button.Close"
        })]
        [TestCase("LocalSessionDeleteConfirmationForm", new[]
        {
            "LocalSessionDeleteConfirmationForm.Check.DeleteLocalHistory",
            "LocalSessionDeleteConfirmationForm.Button.Delete"
        })]
        [TestCase("ApprovalRequestForm.GenericButtons", new[]
        {
            "ApprovalRequestForm.Button.AllowOnce.Generic",
            "ApprovalRequestForm.Button.AllowForSession.Generic",
            "ApprovalRequestForm.Button.Decline.Generic"
        })]
        [TestCase("AsyncInputRequestForm", new[]
        {
            "AsyncInputForm.Button.Send",
            "AsyncInputForm.Button.Ignore",
            "AsyncInputForm.Button.Later"
        })]
        [TestCase("MainForm.PendingAsyncInput", new[]
        {
            "MainForm.Button.Attach",
            "MainForm.Button.Send",
            "MainForm.Button.Stop",
            "MainForm.Button.PendingAsyncInput.Template",
            "VoiceChat.Button.Start"
        })]
        [TestCase("OptionsForm.VoiceChat", new[]
        {
            "VoiceSettings.Label.TalkKey",
            "VoiceSettings.Label.TalkMode",
            "VoiceSettings.Label.Voice",
            "VoiceSettings.Label.Pace",
            "VoiceSettings.Label.Volume",
            "VoiceSettings.Button.Preview",
            "Common.OK",
            "Common.Cancel"
        })]
        [TestCase("Settings.Menu", new[]
        {
            "Settings.Menu.Options",
            "Settings.Menu.Targets",
            "Settings.Menu.Extensions",
            "Settings.Menu.RemoteService"
        })]
        [TestCase("ApprovalRequestForm.DeleteButtons", new[]
        {
            "ApprovalRequestForm.Button.AllowOnce.Delete",
            "ApprovalRequestForm.Button.AllowForSession.Delete",
            "ApprovalRequestForm.Button.Decline.Delete"
        })]
        [TestCase("ApprovalRequestForm.WriteButtons", new[]
        {
            "ApprovalRequestForm.Button.AllowOnce.Write",
            "ApprovalRequestForm.Button.AllowForSession.Write",
            "ApprovalRequestForm.Button.Decline.Write"
        })]
        [TestCase("ApprovalRequestForm.ModifyButtons", new[]
        {
            "ApprovalRequestForm.Button.AllowOnce.Modify",
            "ApprovalRequestForm.Button.AllowForSession.Modify",
            "ApprovalRequestForm.Button.Decline.Modify"
        })]
        [TestCase("ApprovalRequestForm.MoveButtons", new[]
        {
            "ApprovalRequestForm.Button.AllowOnce.Move",
            "ApprovalRequestForm.Button.AllowForSession.Move",
            "ApprovalRequestForm.Button.Decline.Move"
        })]
        [TestCase("PlanExecutionPromptForm", new[]
        {
            "PlanExecutionPromptForm.Option.Execute",
            "PlanExecutionPromptForm.Option.ContinuePlanning"
        })]
        [TestCase("AutomationsForm", new[]
        {
            "AutomationsForm.Button.New",
            "AutomationsForm.Button.RunNow",
            "AutomationsForm.Button.Details",
            "AutomationsForm.Button.OpenThread",
            "AutomationsForm.Button.StopRun"
        })]
        [TestCase("AutomationEditForm", new[]
        {
            "AutomationEditForm.Label.Name",
            "AutomationEditForm.Label.Kind",
            "AutomationEditForm.Label.ScheduleKind",
            "AutomationEditForm.Label.Interval",
            "AutomationEditForm.Label.Time",
            "AutomationEditForm.Label.DayOfWeek",
            "AutomationEditForm.Label.Prompt",
            "AutomationEditForm.Button.InsertPlugin",
            "AutomationEditForm.Button.InsertSkill",
            "AutomationEditForm.Button.RemoveInput",
            "AutomationEditForm.Check.AutoArchive"
        })]
        [TestCase("AutomationRunDetailsForm", new[]
        {
            "AutomationRunDetailsForm.Button.Refresh",
            "AutomationRunDetailsForm.Button.OpenThread",
            "AutomationRunDetailsForm.Button.OpenWorkspace",
            "AutomationRunDetailsForm.Button.Close"
        })]
        [TestCase("GitHubStoreForm", new[]
        {
            "GitHubStoreForm.Label.Keyword",
            "GitHubStoreForm.Label.Language",
            "GitHubStoreForm.Label.Topic",
            "GitHubStoreForm.Label.Sort",
            "GitHubStoreForm.Label.Limit",
            "GitHubStoreForm.Label.Preset",
            "GitHubStoreForm.Button.Search",
            "GitHubStoreForm.Button.Close",
            "GitHubStoreForm.Check.PublicOnly"
        })]
        [TestCase("GitHubCommentForm", new[]
        {
            "GitHubCommentForm.Button.OK",
            "GitHubCommentForm.Button.Cancel"
        })]
        [TestCase("GitHubDiscussionForm", new[]
        {
            "GitHubDiscussionForm.Button.Refresh",
            "GitHubDiscussionForm.Button.Comment",
            "GitHubDiscussionForm.Button.Close"
        })]
        [TestCase("GitHubReleaseDetailsForm", new[]
        {
            "GitHubReleaseDetailsForm.Button.Refresh",
            "GitHubReleaseDetailsForm.Button.Close"
        })]
        [TestCase("GitHubRepositoryDetailsForm", new[]
        {
            "GitHubRepositoryDetailsForm.Button.Refresh",
            "GitHubRepositoryDetailsForm.Button.Close"
        })]
        [TestCase("GitHubIssueCreateForm", new[]
        {
            "GitHubIssueCreateForm.Button.OK",
            "GitHubIssueCreateForm.Button.Cancel"
        })]
        [TestCase("GitHubPullRequestCreateForm", new[]
        {
            "GitHubPullRequestCreateForm.Button.OK",
            "GitHubPullRequestCreateForm.CheckBox.Draft",
            "GitHubPullRequestCreateForm.Button.Cancel"
        })]
        [TestCase("ScreenReaderSettingsForm.Options", new[]
        {
            "ScreenReaderSettingsForm.Option.ManualBrowseUseMainChannel"
        })]
        [TestCase("StartupModeOnboardingForm", new[]
        {
            "StartupModeOnboardingForm.Option.OfficeMode",
            "StartupModeOnboardingForm.Option.CodingMode"
        })]
        [TestCase("OptionsForm.FontScale", new[] { "OptionsForm.Label.UiFontScale", "Common.OK", "Common.Cancel" })]
        [TestCase("OptionsForm.Office", new[]
        {
            "Common.Browse",
            "OptionsForm.Button.RestoreDefaultOfficeRootPath",
            "OptionsForm.Button.OfficeCliRefresh",
            "OptionsForm.Button.OfficeCliInstall",
            "OptionsForm.Button.OfficeCliUninstall"
        })]
        [TestCase("AuthenticationCenterForm", new[]
        {
            "AuthenticationCenter.Button.Add", "AuthenticationCenter.Button.Edit",
            "AuthenticationCenter.Button.Copy", "AuthenticationCenter.Button.Delete",
            "AuthenticationCenter.Button.Refresh", "AuthenticationCenter.Button.Close",
            "AuthenticationCenter.Button.Enable", "AuthenticationCenter.Button.RetryConnection", "AuthenticationCenter.Button.Reauthenticate", "AuthenticationCenter.Button.Accounts",
            "SessionHistory.Unified", "SessionHistory.Button.Retry"
        })]
        [TestCase("AuthenticationLoginControl", new[]
        {
            "AuthenticationProviderEdit.Label.Name", "AuthenticationLogin.Start", "AuthenticationLogin.DeviceFallback", "AuthenticationAccounts.Copy", "AuthenticationAccounts.Open",
            "AuthenticationProviderEdit.Button.Config", "AuthenticationContext.Window", "AuthenticationContext.Limit",
            "AuthenticationLogin.Cancel", "AuthenticationLogin.UseSaved", "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationAccountsForm", new[]
        {
            "AuthenticationSavedAccounts.Select", "AuthenticationCenter.Button.Delete", "AuthenticationCenter.Button.Refresh", "AuthenticationCenter.Button.Close"
        })]
        [TestCase("AuthenticationProviderSinglePage", new[]
        {
            "AuthenticationContext.Remote", "AuthenticationProviderEdit.Button.Config", "AuthenticationContext.Window", "AuthenticationContext.Limit",
            "AuthenticationProviderEdit.Label.Name", "AuthenticationProviderEdit.Label.UpstreamFormat",
            "AuthenticationProviderEdit.Label.Url", "AuthenticationProviderEdit.Label.Key", "AuthenticationProviderEdit.ClearKey",
            "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel",
            "AuthenticationModels.Fetch", "AuthenticationModels.Add",
            "AuthenticationAdvanced.FullUrl", "AuthenticationAdvanced.Auth", "AuthenticationAdvanced.Max", "AuthenticationAdvanced.Environment",
            "AuthenticationAdvanced.Agent", "AuthenticationAdvanced.Cache", "AuthenticationAdvanced.Thinking", "AuthenticationAdvanced.Effort",
            "AuthenticationAdvanced.EffortParameter", "AuthenticationAdvanced.Headers", "AuthenticationAdvanced.Body", "AuthenticationAdvanced.Query",
            "AuthenticationAdvanced.ThinkingParameter", "AuthenticationAdvanced.EffortMode", "AuthenticationAdvanced.Impersonate", "AuthenticationQuery.Add"
        })]
        [TestCase("AuthenticationProviderEditForm", new[]
        {
            "AuthenticationProviderEdit.Label.Name", "AuthenticationProviderEdit.Label.UpstreamFormat",
            "AuthenticationProviderEdit.Label.Url",
            "AuthenticationProviderEdit.Label.Key", "AuthenticationProviderEdit.ClearKey",
            "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationSessionHistoryConfirmForm", new[] { "Common.OK", "Common.Cancel" })]
        [TestCase("AuthenticationEditorConflictForm", new[]
        {
            "AuthenticationConflict.KeepMine", "AuthenticationConflict.KeepTheirs", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationProviderSelectionForm", new[]
        {
            "AuthenticationProviderSelection.Next", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationModelEditForm", new[]
        {
            "AuthenticationModel.Name", "AuthenticationModel.Id", "AuthenticationModel.Context", "AuthenticationModel.Levels",
            "AuthenticationModel.Default", "AuthenticationModel.Parallel", "AuthenticationModel.Save", "AuthenticationModel.Cancel"
        })]
        [TestCase("AuthenticationKeyValueEditForm.Query", new[]
        {
            "AuthenticationQuery.Name", "AuthenticationQuery.Value", "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationKeyValueEditForm.Typed", new[]
        {
            "AuthenticationKeyValue.Name", "AuthenticationKeyValue.Value", "AuthenticationKeyValue.Type",
            "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel"
        })]
        [TestCase("AuthenticationAdvancedControl", new[]
        {
            "AuthenticationAdvanced.FullUrl", "AuthenticationAdvanced.Auth", "AuthenticationAdvanced.Max", "AuthenticationAdvanced.Environment",
            "AuthenticationAdvanced.Agent", "AuthenticationAdvanced.Cache", "AuthenticationAdvanced.Thinking", "AuthenticationAdvanced.Effort",
            "AuthenticationAdvanced.EffortParameter", "AuthenticationAdvanced.Headers", "AuthenticationAdvanced.Body", "AuthenticationAdvanced.Query",
            "AuthenticationAdvanced.ThinkingParameter", "AuthenticationAdvanced.EffortMode", "AuthenticationQuery.Add",
            "AuthenticationAdvanced.AddHeader", "AuthenticationAdvanced.AddBody",
            "AuthenticationProviderEdit.Button.Save", "AuthenticationProviderEdit.Button.Cancel"
        })]
        public void ActiveChineseMnemonics_AreUniqueWithinForm(string formName, string[] keys)
        {
            var values = LoadResourceValues("Strings.zh-CN.resx", keys);
            var duplicates = values
                .Select(pair => new { pair.Key, Mnemonic = ExtractMnemonic(pair.Value) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Mnemonic))
                .GroupBy(item => item.Mnemonic, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key + ": " + string.Join(", ", group.Select(item => item.Key)))
                .ToArray();

            var missing = values
                .Where(pair => ExtractMnemonic(pair.Value) == null)
                .Select(pair => pair.Key)
                .ToArray();

            Assert.That(missing, Is.Empty, formName + " 缺少助记符。");
            Assert.That(duplicates, Is.Empty, formName + " 存在助记符重复。");
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void MainForm_AccountAttachmentAndUserCenterMnemonicsAreDistinct()
        {
            var values = LoadResourceValues("Strings.zh-CN.resx", new[]
            {
                "MainMenu.Account",
                "MainForm.Button.Attach",
                "MainMenu.UserCenter"
            });

            Assert.That(ExtractMnemonic(values["MainMenu.Account"]), Is.EqualTo("C"));
            Assert.That(ExtractMnemonic(values["MainForm.Button.Attach"]), Is.EqualTo("A"));
            Assert.That(ExtractMnemonic(values["MainMenu.UserCenter"]), Is.EqualTo("U"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [TestCase("Strings.resx")]
        [TestCase("Strings.zh-CN.resx")]
        public void MainForm_FileMenuMnemonicsAreUnique(string resourceFileName)
        {
            var values = LoadResourceValues(resourceFileName, new[]
            {
                "MainMenu.OpenWorkspace",
                "MainMenu.OpenWslWorkspace",
                "MainMenu.OpenGlobalFileBrowser",
                "MainMenu.NewSession",
                "MainMenu.Automations",
                "MainForm.ImportSync.Menu",
                "MainMenu.TargetReload",
                "MainMenu.Exit"
            });
            var mnemonics = values
                .Select(pair => new { pair.Key, Mnemonic = ExtractMenuMnemonic(pair.Value) })
                .ToArray();

            Assert.That(mnemonics.All(item => !string.IsNullOrWhiteSpace(item.Mnemonic)), Is.True, resourceFileName);
            Assert.That(
                mnemonics.GroupBy(item => item.Mnemonic, StringComparer.OrdinalIgnoreCase).All(group => group.Count() == 1),
                Is.True,
                resourceFileName);
            Assert.That(
                mnemonics.Single(item => item.Key == "MainMenu.TargetReload").Mnemonic,
                Is.EqualTo("R"),
                resourceFileName);
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void AuditedFocusableChoices_AllProvideChineseMnemonics()
        {
            var keys = new[]
            {
                "ActivitySettingsForm.Check.Thinking",
                "ActivitySettingsForm.Check.Plan",
                "ActivitySettingsForm.Check.Tool",
                "ActivitySettingsForm.Check.Command",
                "ActivitySettingsForm.Check.File",
                "ActivitySettingsForm.Check.Notice",
                "ActivitySettingsForm.Check.Error",
                "ActivitySettingsForm.Check.Approval",
                "ActivitySettingsForm.Check.Account",
                "ActivitySettingsForm.Check.ProtocolNotice",
                "ActivitySettingsForm.Check.ProtocolWarning",
                "ActivitySettingsForm.Check.ClearOnNewTurn",
                "NotificationSettingsForm.Check.TurnCompletedSound",
                "NotificationSettingsForm.Check.SubAgentTurnCompletedSound",
                "NotificationSettingsForm.Check.ApprovalRequestSound",
                "NotificationSettingsForm.Check.SendMessageSound",
                "PlanExecutionPromptForm.Option.Execute",
                "PlanExecutionPromptForm.Option.ContinuePlanning",
                "ScreenReaderSettingsForm.Option.ManualBrowseUseMainChannel",
                "StartupModeOnboardingForm.Option.OfficeMode",
                "StartupModeOnboardingForm.Option.CodingMode"
            };

            var values = LoadResourceValues("Strings.zh-CN.resx", keys);
            var missing = values
                .Where(pair => ExtractMnemonic(pair.Value) == null)
                .Select(pair => pair.Key)
                .ToArray();

            Assert.That(missing, Is.Empty, "以下选择项缺少助记符。");
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void AuditedActionButtons_AllProvideChineseMnemonics()
        {
            var keys = new[]
            {
                "ApprovalRequestForm.Button.AllowOnce.Generic",
                "ApprovalRequestForm.Button.AllowOnce.Delete",
                "ApprovalRequestForm.Button.AllowOnce.Write",
                "ApprovalRequestForm.Button.AllowOnce.Modify",
                "ApprovalRequestForm.Button.AllowOnce.Move",
                "ApprovalRequestForm.Button.AllowForSession.Generic",
                "ApprovalRequestForm.Button.AllowForSession.Delete",
                "ApprovalRequestForm.Button.AllowForSession.Write",
                "ApprovalRequestForm.Button.AllowForSession.Modify",
                "ApprovalRequestForm.Button.AllowForSession.Move",
                "ApprovalRequestForm.Button.Decline.Generic",
                "ApprovalRequestForm.Button.Decline.Delete",
                "ApprovalRequestForm.Button.Decline.Write",
                "ApprovalRequestForm.Button.Decline.Modify",
                "ApprovalRequestForm.Button.Decline.Move",
                "AutomationRunDetailsForm.Button.OpenThread",
                "AutomationRunDetailsForm.Button.OpenWorkspace",
                "AutomationRunDetailsForm.Button.Close",
                "AutomationsForm.Button.Details",
                "AutomationsForm.Button.OpenThread",
                "GitCloneForm.Button.Close",
                "GitEnvironmentForm.Button.Close",
                "GitHubCommentForm.Button.OK",
                "GitHubCommentForm.Button.Cancel",
                "GitHubDiscussionForm.Button.Close",
                "GitHubIssueCreateForm.Button.Cancel",
                "GitHubPullRequestCreateForm.Button.Cancel",
                "GitHubReleaseDetailsForm.Button.Close",
                "GitHubRepositoryDetailsForm.Button.Close",
                "GitRemoteCredentialsForm.Button.Close",
                "PluginDetailsForm.Button.PreviewSkill",
                "PluginDetailsForm.Button.Close",
                "PluginSkillPreviewForm.Button.OK",
                "OptionsForm.Button.RestoreDefaultOfficeRootPath"
            };

            var values = LoadResourceValues("Strings.zh-CN.resx", keys);
            var missing = values
                .Where(pair => ExtractMnemonic(pair.Value) == null)
                .Select(pair => pair.Key)
                .ToArray();

            Assert.That(missing, Is.Empty, "以下按钮缺少助记符。");
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void ActiveChineseMnemonicLabels_AllProvideMnemonics()
        {
            var keys = new[]
            {
                "GitCloneForm.Label.RepositoryUrl",
                "GitCloneForm.Label.TargetParent",
                "GitCloneForm.Label.TargetFolder",
                "GitRemoteRepositoriesForm.Label.Filter",
                "GitCommitHistoryForm.Label.HistoryRef",
                "GitBranchIntegrateForm.Label.CurrentBranch",
                "GitBranchIntegrateForm.Label.TargetBranch",
                "GitBranchIntegrateForm.Label.Comparison",
                "GitBranchIntegrateForm.Label.Mode",
                "GitWorkbenchForm.Label.CommitSubject",
                "GitWorkbenchForm.Label.CommitBody",
                "GitRemoteEditForm.Label.Name",
                "GitRemoteEditForm.Label.FetchUrl",
                "GitRemoteEditForm.Label.PushUrl",
                "GitUpstreamEditForm.Label.Branch",
                "GitUpstreamEditForm.Label.Upstream",
                "GitBranchIntegrateForm.Check.FetchFirst",
                "RuntimeFileBrowserForm.Label.Address",
                "RuntimeFileBrowserForm.Label.TargetFolder",
                "AppServerFileBrowserForm.Label.Address",
                "AppServerFileBrowserForm.Label.Search",
                "GitHubStoreForm.Label.Keyword",
                "GitHubStoreForm.Label.Language",
                "GitHubStoreForm.Label.Topic",
                "GitHubStoreForm.Label.Sort",
                "GitHubStoreForm.Label.Limit",
                "GitHubStoreForm.Label.Preset",
                "GitHubStoreForm.Check.PublicOnly"
            };

            var values = LoadResourceValues("Strings.zh-CN.resx", keys);
            var missing = values
                .Where(pair => ExtractMnemonic(pair.Value) == null)
                .Select(pair => pair.Key)
                .ToArray();

            Assert.That(missing, Is.Empty, "以下标签缺少助记符。");
        }

        private static Dictionary<string, string> LoadResourceValues(string fileName, IEnumerable<string> keys)
        {
            var path = RepositorySource.GetPath("src", "MyCodex.WinForms", "Resources", fileName);
            var document = XDocument.Load(path);
            var requested = new HashSet<string>(keys ?? Array.Empty<string>(), StringComparer.Ordinal);
            var values = document.Root
                .Elements("data")
                .Select(element => new
                {
                    Name = (string)element.Attribute("name"),
                    Value = (string)element.Element("value") ?? string.Empty
                })
                .Where(item => item.Name != null && requested.Contains(item.Name))
                .ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

            var missingKeys = requested.Where(key => !values.ContainsKey(key)).ToArray();
            Assert.That(missingKeys, Is.Empty, "测试资源键缺失。");
            return values;
        }

        private static string ExtractMnemonic(string value)
        {
            var match = MnemonicPattern.Match(value ?? string.Empty);
            return match.Success ? match.Groups["key"].Value.ToUpperInvariant() : null;
        }

        private static string ExtractMenuMnemonic(string value)
        {
            var text = value ?? string.Empty;
            for (var index = 0; index < text.Length - 1; index++)
            {
                if (text[index] != '&')
                {
                    continue;
                }

                if (text[index + 1] == '&')
                {
                    index++;
                    continue;
                }

                return text[index + 1].ToString().ToUpperInvariant();
            }

            return null;
        }
    }
}
