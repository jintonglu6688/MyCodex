using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class AppServerConfigMutationSourceTests
    {
        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OfficialConfigObservation_DoesNotRepairOrWriteGeneralSettings()
        {
            var sessionSettings = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");
            var options = RepositorySource.ReadSourceFile("MainForm.OptionsSettings.cs");
            var events = RepositorySource.ReadSourceFile("MainForm.AppServerEvents.cs");

            Assert.That(sessionSettings, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(options, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(events, Does.Not.Contain("WriteConfigBatchAsync("));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void HostSettingsPath_DoesNotOwnOfficialConfigWritesOrLegacyCleanup()
        {
            var operatorSource = RepositorySource.ReadSourceFile("AppServerHostSettingsOperator.cs");
            var contractsSource = RepositorySource.ReadSourceFile("HostSettingsContracts.cs");
            var dispatcherSource = RepositorySource.ReadSourceFile("RemoteCommandDispatcher.HostSettings.cs");

            Assert.That(operatorSource, Does.Not.Contain("IAppServerConfigClient"));
            Assert.That(operatorSource, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(operatorSource, Does.Not.Contain("BuildLegacyContextOverrideCleanupEdits"));
            Assert.That(contractsSource, Does.Not.Contain("HostSettingsOfficial"));
            Assert.That(dispatcherSource, Does.Not.Contain("\"official\""));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void McpConfigMutation_ReadsFreshObservationAndSendsExpectedVersion()
        {
            var source = RepositorySource.ReadSourceFile("McpManagementForm.cs");

            Assert.That(source, Does.Contain("_configClient.ReadConfigAsync(null, true)"));
            Assert.That(source, Does.Contain("AppServerConfigWriteGuard.Evaluate(snapshot, edits)"));
            Assert.That(source, Does.Contain("expectedVersion: guard.ExpectedVersion"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OfficialSettingsMutation_IsExplicitAndUsesFreshVersionGuardedObservation()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.OfficialCodexSettings.cs");

            Assert.That(source, Does.Contain("_appServerClient.ReadConfigAsync(null, true)"));
            Assert.That(source, Does.Contain("AppServerConfigWriteGuard.EvaluateUserConfig(snapshot, edits)"));
            Assert.That(source, Does.Contain("expectedVersion: guard.ExpectedVersion"));
            Assert.That(source, Does.Contain("reloadUserConfig: true"));
        }
    }
}
