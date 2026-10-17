using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class AppServerConfigMutationSourceTests
    {
        [Test]
        public void OfficialConfigObservation_DoesNotRepairOrWriteGeneralSettings()
        {
            var sessionSettings = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Forms", "MainForm.SessionSettings.cs"));
            var options = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs"));
            var events = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Forms", "MainForm.AppServerEvents.cs"));

            Assert.That(sessionSettings, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(options, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(events, Does.Not.Contain("WriteConfigBatchAsync("));
        }

        [Test]
        public void HostSettingsPath_DoesNotOwnOfficialConfigWritesOrLegacyCleanup()
        {
            var operatorSource = File.ReadAllText(GetPath("src", "MyCodex.Infrastructure", "RemoteRelay", "AppServerHostSettingsOperator.cs"));
            var contractsSource = File.ReadAllText(GetPath("src", "MyCodex.Application", "CodingRemote", "HostSettingsContracts.cs"));
            var dispatcherSource = File.ReadAllText(GetPath("src", "MyCodex.Application", "CodingRemote", "RemoteCommandDispatcher.HostSettings.cs"));

            Assert.That(operatorSource, Does.Not.Contain("IAppServerConfigClient"));
            Assert.That(operatorSource, Does.Not.Contain("WriteConfigBatchAsync("));
            Assert.That(operatorSource, Does.Not.Contain("BuildLegacyContextOverrideCleanupEdits"));
            Assert.That(contractsSource, Does.Not.Contain("HostSettingsOfficial"));
            Assert.That(dispatcherSource, Does.Not.Contain("\"official\""));
        }

        [Test]
        public void McpConfigMutation_ReadsFreshObservationAndSendsExpectedVersion()
        {
            var source = File.ReadAllText(GetPath("src", "MyCodex.WinForms", "Forms", "McpManagementForm.cs"));

            Assert.That(source, Does.Contain("_configClient.ReadConfigAsync(null, true)"));
            Assert.That(source, Does.Contain("AppServerConfigWriteGuard.Evaluate(snapshot, edits)"));
            Assert.That(source, Does.Contain("expectedVersion: guard.ExpectedVersion"));
        }

        [Test]
        public void OfficialSettingsMutation_IsExplicitAndUsesFreshVersionGuardedObservation()
        {
            var source = File.ReadAllText(GetPath(
                "src",
                "MyCodex.WinForms",
                "Forms",
                "MainForm.OfficialCodexSettings.cs"));

            Assert.That(source, Does.Contain("_appServerClient.ReadConfigAsync(null, true)"));
            Assert.That(source, Does.Contain("AppServerConfigWriteGuard.EvaluateUserConfig(snapshot, edits)"));
            Assert.That(source, Does.Contain("expectedVersion: guard.ExpectedVersion"));
            Assert.That(source, Does.Contain("reloadUserConfig: true"));
        }

        private static string GetPath(params string[] parts)
        {
            var path = TestContext.CurrentContext.TestDirectory;
            while (!File.Exists(Path.Combine(path, "MyCodex.sln")))
            {
                path = Directory.GetParent(path).FullName;
            }

            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
            }

            return path;
        }
    }
}
