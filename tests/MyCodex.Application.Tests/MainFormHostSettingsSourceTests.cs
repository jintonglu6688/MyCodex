using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormHostSettingsSourceTests
    {
        [Test]
        public void HostSettingsComposition_UsesExistingRuntimeDefaultsAndSafeScopeData()
        {
            var source = ReadFormSource("MainForm.HostSettings.cs");

            Assert.That(source, Does.Contain("GetRemoteAvailableModelsAsync"));
            Assert.That(source, Does.Contain("SaveRemoteOfficialModelDefaultsAsync"));
            Assert.That(source, Does.Contain("new AppServerOfficialHostSettingsOperator(settingsOperator, _appServerClient)"));
            Assert.That(source, Does.Not.Contain("SetConfiguredDefaultModel"));
            Assert.That(source, Does.Contain("SetConfiguredDefaultApprovalMode"));
            Assert.That(source, Does.Contain("() => System.Threading.Tasks.Task.CompletedTask"));
            Assert.That(source, Does.Contain("Environment.MachineName"));
            Assert.That(source, Does.Not.Contain("AuthenticationSourceKind"));
            Assert.That(source, Does.Not.Contain("LoadCurrentAuthenticationSourceSelectionForDiagnostics"));
            Assert.That(source, Does.Not.Contain("Password"));
            Assert.That(source, Does.Not.Contain("ApiKey"));
        }

        [Test]
        public void PermissionLabels_AreSharedByOptionsAndRemoteSettings()
        {
            var labels = ReadFormSource("CustomApprovalPermissionDisplayText.cs");
            var options = ReadFormSource("OptionsForm.cs");
            var host = ReadFormSource("MainForm.HostSettings.cs");

            Assert.That(labels, Does.Contain("CustomApprovalPermission.GitInspect"));
            Assert.That(options, Does.Contain("CustomApprovalPermissionDisplayText.Get(permission)"));
            Assert.That(host, Does.Contain("CustomApprovalPermissionDisplayText.Get"));
        }

        [Test]
        public void HostSettingsMutation_RefreshesWithoutReloadingSessionHistory()
        {
            var host = ReadFormSource("MainForm.HostSettings.cs");
            var mutations = ReadFormSource("MainForm.RemoteHostMutations.cs");

            Assert.That(mutations, Does.Contain("RefreshAfterRemoteHostSettingsUpdateAsync"));
            Assert.That(host, Does.Contain("RefreshGlobalServerConfigAsync(false)"));
            Assert.That(host, Does.Contain("LoadAvailableModelsAsync(false)"));
            Assert.That(host, Does.Not.Contain("LoadPersistedState"));
            Assert.That(host, Does.Not.Contain("LoadSessionHistory"));
        }

        private static string ReadFormSource(string fileName)
        {
            return File.ReadAllText(Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms",
                fileName)));
        }
    }
}
