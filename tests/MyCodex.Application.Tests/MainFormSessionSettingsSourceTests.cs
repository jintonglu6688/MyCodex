using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormSessionSettingsSourceTests
    {
        [Test]
        public void SettingsMenu_DoesNotExposeEffectiveConfigOrRuntimeCapabilitiesEntries()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var forbidden = new[]
            {
                "Settings.Menu.EffectiveConfig",
                "Settings.Menu.RuntimeCapabilities",
                "EffectiveConfigMenuItem_Click",
                "RuntimeCapabilitiesMenuItem_Click",
                "OpenEffectiveConfigAsync",
                "new EffectiveConfigForm",
                "new RuntimeCapabilitiesForm"
            };

            foreach (var text in forbidden)
            {
                Assert.That(
                    source.IndexOf(text, StringComparison.Ordinal),
                    Is.LessThan(0),
                    "设置菜单不应继续暴露或引用已移除入口：" + text);
            }
        }

        [Test]
        public void ExtensionsMenu_DoesNotExposeExternalAgentConfigImportEntry()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Not.Contain("Settings.Menu.ExternalAgentConfigImport"));
            Assert.That(source, Does.Not.Contain("_extensionsMenuItem.MenuItems.Add(_externalAgentConfigImportMenuItem)"));
            Assert.That(source, Does.Contain("RefreshAfterExternalAgentConfigImportAsync"));
        }

        [Test]
        public void SettingsMenu_DoesNotExposeQuickFileSearchOrAppServerFilesEntry()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Not.Contain("Settings.Menu.AppServerQuickFileSearch"));
            Assert.That(source, Does.Not.Contain("AppServerQuickFileSearchMenuItem_Click"));
            Assert.That(source, Does.Not.Contain("AppServerFuzzyFileSearchForm"));
            Assert.That(source, Does.Not.Contain("_appServerFilesMenuItem"));
            Assert.That(source, Does.Not.Contain("Settings.Menu.AppServerFiles"));
            Assert.That(source, Does.Not.Contain("AppServerFilesMenuItem_Click"));
        }

        [Test]
        public void SessionDefaults_UseTargetScopedHostPreferencesAndAppServerObservations()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Contain("_sessionDefaultsService.Resolve("));
            Assert.That(source, Does.Contain("_availableModels,"));
            Assert.That(source, Does.Not.Contain("LoadEffectiveAuthenticationProfileSnapshotSafe"));
            Assert.That(source, Does.Not.Contain("OpenRouterSourcePlanner"));
            Assert.That(source, Does.Not.Contain("ModelConversionSourcePlanner"));
        }

        [Test]
        public void RuntimeSessionDefaults_DoNotUseTargetEndpointStorage()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");

            Assert.That(source, Does.Not.Contain("JsonRuntimeSessionDefaultsStore"));
            Assert.That(source, Does.Not.Contain("UsesRuntimeEndpointStorage"));
            Assert.That(mainFormSource, Does.Not.Contain("JsonRuntimeSessionDefaultsStore"));
        }

        [Test]
        public void ConversationPath_UsesOnlyAppServerModelObservations()
        {
            var modelSource = RepositorySource.ReadSourceFile("MainForm.Models.cs");
            var settingsSource = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var sendingSource = RepositorySource.ReadSourceFile("MainForm.MessageSending.cs");

            Assert.That(modelSource, Does.Contain("await RefreshModelConfigurationAsync("));
            var configSource = RepositorySource.ReadSourceFile("MainForm.ModelConfiguration.cs");
            Assert.That(configSource, Does.Contain("AppServerModelConfigurationService"));
            Assert.That(settingsSource, Does.Contain("_globalServerConfigSnapshot);"));
            Assert.That(settingsSource, Does.Contain("_sessionDefaultsService.Resolve("));
            Assert.That(sendingSource, Does.Contain("await PrepareSendSettingsAsync(targetSession, targetWorkspace)"));
            var preparationSource = RepositorySource.ReadSourceFile("MainForm.SendPreparation.cs");
            Assert.That(preparationSource, Does.Contain("await ReadSessionModelSettingsAsync(session, workspace)"));
            Assert.That(configSource, Does.Contain("string.IsNullOrWhiteSpace(resolved.ModelId)"));
            Assert.That(configSource, Does.Contain("SessionDefaultsService.IsReasoningEffortAvailable"));
            foreach (var forbidden in new[]
            {
                "OpenRouter",
                "ModelConversion",
                "AuthenticationProfile",
                "EnsureActiveGatewayRuntimeReadyForAction"
            })
            {
                Assert.That(modelSource, Does.Not.Contain(forbidden));
                Assert.That(settingsSource, Does.Not.Contain(forbidden));
                Assert.That(sendingSource, Does.Not.Contain(forbidden));
            }
        }

        [Test]
        public void NewConversation_AnnouncesFirstObservedModelFallback()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.Sessions.cs");

            Assert.That(source, Does.Contain("UsesFirstObservedModelFallback"));
            Assert.That(source, Does.Contain("MainForm.Message.FirstObservedModelFallback.Template"));
            Assert.That(source, Does.Contain("SpeakAuxiliaryTextWithScreenReader(fallbackMessage)"));
        }

        [Test]
        public void SettingsMenu_RemoteServiceEntryIsAlwaysAvailable()
        {
            var sessionSettingsSource = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");

            Assert.That(sessionSettingsSource, Does.Contain("_remoteServiceMenuItem = new MenuItem(UiLocalization.Text(\"Settings.Menu.RemoteService\", \"Phone Connection(&R)...\"));"));
            Assert.That(sessionSettingsSource, Does.Contain("_settingsMenuItem.MenuItems.Add(_remoteServiceMenuItem);"));
            Assert.That(sessionSettingsSource, Does.Contain("new RemoteServiceManagementForm("));
            Assert.That(sessionSettingsSource, Does.Not.Contain("RemoteServiceFeatureGate"));
            Assert.That(mainFormSource, Does.Not.Contain("RemoteServiceFeatureGate"));
            Assert.That(mainFormSource, Does.Contain("_remoteHostRealtimeClient = CreateRemoteHostRealtimeClient();"));
            Assert.That(mainFormSource, Does.Contain("_remoteHostServiceController = new RemoteHostServiceController("));
            Assert.That(mainFormSource, Does.Not.Contain("AuthenticationCenter"));
            Assert.That(mainFormSource, Does.Not.Contain("_accountMenuItem.MenuItems.Add(_remoteServiceMenuItem)"));
        }

        [Test]
        public void RemoteHostRealtimeClient_WiresOfficeRemoteService()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private IRelayHostRealtimeClient CreateRemoteHostRealtimeClient()");

            Assert.That(method, Does.Contain("CodingRemoteService.CreateOffice("));
            Assert.That(method, Does.Contain("AppInteractionMode.Office"));
            Assert.That(method, Does.Contain("new RemoteCommandDispatcher("));
            Assert.That(method, Does.Contain("officeRemoteService,"));
            Assert.That(method, Does.Contain("BuildHostSettingsOperator()"));
        }

        [Test]
        public void RemoteServiceManagement_RestartUsesSafeBeginInvoke()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private void OpenRemoteServiceManagement()");

            Assert.That(method, Does.Contain("this.BeginInvokeSafe("));
            Assert.That(method, Does.Not.Contain("BeginInvoke(new Action("));
        }

        [Test]
        public void RemoteServiceManagement_ComposesChannelScopedPairingWorkflow()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var formSource = RepositorySource.ReadSourceFile("RemoteServiceManagementForm.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private void OpenRemoteServiceManagement()");

            Assert.That(method, Does.Contain("var relayClient = new RelayHostClient();"));
            Assert.That(method, Does.Contain("var pairingWorkflow = new SecureRemotePairingWorkflow("));
            Assert.That(method, Does.Contain("_remoteIdentityStore,"));
            Assert.That(method, Does.Contain("_remoteBindingStore"));
            Assert.That(method, Does.Contain("pairingWorkflow,"));
            Assert.That(formSource, Does.Not.Contain("new JsonRemoteBindingStore()"));
        }

        [Test]
        public void RefreshOfficialConfigAfterConnect_ChecksStartupFastModePrompt()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "internal async Task RefreshOfficialConfigStateAfterConnectAsync");

            Assert.That(method, Does.Contain("await RefreshGlobalServerConfigAsync(false);"));
            Assert.That(method, Does.Contain("await PromptForStartupFastModeIfNeededAsync();"));
            Assert.That(
                method.IndexOf("await RefreshGlobalServerConfigAsync(false);", StringComparison.Ordinal),
                Is.LessThan(method.IndexOf("await PromptForStartupFastModeIfNeededAsync();", StringComparison.Ordinal)));
            Assert.That(method, Does.Not.Contain("WriteConfigBatchAsync("));
        }
    }
}
