using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormAuthenticationStatePlannerTests
    {
        [Test]
        public void RequiresOfficialLogin_DoesNotWarnForConfiguredThirdPartyProvider()
        {
            var state = new AppServerAuthenticationState
            {
                HasAccount = false,
                IsAuthenticated = false,
                RequiresOpenAiAuth = false
            };

            Assert.That(
                MainFormAuthenticationStatePlanner.RequiresOfficialLogin(state),
                Is.False);
        }

        [Test]
        public void RequiresOfficialLogin_WarnsWhenAppServerRequiresOpenAiAuthenticationWithoutAccount()
        {
            var state = new AppServerAuthenticationState
            {
                HasAccount = false,
                IsAuthenticated = false,
                RequiresOpenAiAuth = true
            };

            Assert.That(
                MainFormAuthenticationStatePlanner.RequiresOfficialLogin(state),
                Is.True);
        }

        [Test]
        public void RequiresOfficialLogin_DoesNotWarnWhenAccountExists()
        {
            var state = new AppServerAuthenticationState
            {
                HasAccount = true,
                IsAuthenticated = true,
                RequiresOpenAiAuth = true
            };

            Assert.That(
                MainFormAuthenticationStatePlanner.RequiresOfficialLogin(state),
                Is.False);
        }

        [Test]
        public void BuildSettingsMenuState_KeepsRuntimeSettingsAvailableInOfficeMode()
        {
            var state = MainFormAuthenticationStatePlanner.BuildSettingsMenuState(
                isOfficeMode: true,
                isWorkspaceAvailable: true);

            Assert.That(state.AppServerRuntimeSettingsVisible, Is.True);
            Assert.That(state.AppServerRuntimeSettingsEnabled, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void BuildSettingsMenuState_EnablesSkillsOnlyWhenWorkspaceIsAvailable(
            bool isWorkspaceAvailable,
            bool expected)
        {
            var state = MainFormAuthenticationStatePlanner.BuildSettingsMenuState(
                isOfficeMode: false,
                isWorkspaceAvailable: isWorkspaceAvailable);

            Assert.That(state.SkillsManagementEnabled, Is.EqualTo(expected));
        }

        [Test]
        public void BuildSessionSettingsContextMenuState_DisablesSessionItemsWithoutSession()
        {
            var state = MainFormAuthenticationStatePlanner.BuildSessionSettingsContextMenuState(
                hasSession: false,
                hasModelOverride: true,
                hasApprovalOverride: true,
                hasPlanModeOverride: true);

            Assert.That(state.SessionModelEnabled, Is.False);
            Assert.That(state.SessionPermissionEnabled, Is.False);
            Assert.That(state.SessionResetSettingsEnabled, Is.False);
        }

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        public void BuildSessionSettingsContextMenuState_EnablesResetOnlyWhenOverrideExists(
            bool hasModelOverride,
            bool hasApprovalOverride,
            bool hasPlanModeOverride,
            bool expectedResetEnabled)
        {
            var state = MainFormAuthenticationStatePlanner.BuildSessionSettingsContextMenuState(
                hasSession: true,
                hasModelOverride: hasModelOverride,
                hasApprovalOverride: hasApprovalOverride,
                hasPlanModeOverride: hasPlanModeOverride);

            Assert.That(state.SessionModelEnabled, Is.True);
            Assert.That(state.SessionPermissionEnabled, Is.True);
            Assert.That(state.SessionResetSettingsEnabled, Is.EqualTo(expectedResetEnabled));
        }

        [Test]
        public void BuildSessionSettingsMenuText_UsesBaseSessionTextWithoutSession()
        {
            var text = MainFormAuthenticationStatePlanner.BuildSessionSettingsMenuText(
                defaultModelTemplate: "Default Model: {0}",
                defaultModelSummary: "gpt-5",
                defaultPermissionTemplate: "Default Permission: {0}",
                defaultPermissionSummary: "Auto",
                hasSession: false,
                sessionModelBaseText: "Model",
                sessionModelTemplate: "Model: {0}",
                sessionModelSummary: "Follow default - gpt-5",
                sessionPermissionBaseText: "Permission",
                sessionPermissionTemplate: "Permission: {0}",
                sessionPermissionSummary: "Follow default - Auto");

            Assert.That(text.DefaultModelText, Is.EqualTo("Default Model: gpt-5"));
            Assert.That(text.DefaultPermissionText, Is.EqualTo("Default Permission: Auto"));
            Assert.That(text.SessionModelText, Is.EqualTo("Model"));
            Assert.That(text.SessionPermissionText, Is.EqualTo("Permission"));
        }

        [Test]
        public void BuildSessionSettingsMenuText_UsesTemplatesAndSummariesWithSession()
        {
            var text = MainFormAuthenticationStatePlanner.BuildSessionSettingsMenuText(
                defaultModelTemplate: "Default Model: {0}",
                defaultModelSummary: "gpt-5",
                defaultPermissionTemplate: "Default Permission: {0}",
                defaultPermissionSummary: "Auto",
                hasSession: true,
                sessionModelBaseText: "Model",
                sessionModelTemplate: "Model: {0}",
                sessionModelSummary: "gpt-5 mini high",
                sessionPermissionBaseText: "Permission",
                sessionPermissionTemplate: "Permission: {0}",
                sessionPermissionSummary: "Read only");

            Assert.That(text.SessionModelText, Is.EqualTo("Model: gpt-5 mini high"));
            Assert.That(text.SessionPermissionText, Is.EqualTo("Permission: Read only"));
        }
    }
}
