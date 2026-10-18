using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class AuthenticationBootstrapSourceTests
    {
        [Test]
        public void MainForm_RoutesLocalLoginThroughCenterAndKeepsRemoteOfficialBootstrap()
        {
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var runtimeConnectionSource = RepositorySource.ReadSourceFile("MainForm.RuntimeConnection.cs");
            var bootstrapSource = RepositorySource.ReadSourceFile("MainForm.AuthenticationBootstrap.cs");

            Assert.That(mainFormSource, Does.Not.Contain("AuthenticationCenter"));
            Assert.That(runtimeConnectionSource, Does.Contain("ShowAuthenticationBootstrapIfRequired();"));
            Assert.That(
                bootstrapSource,
                Does.Contain("MainFormAuthenticationStatePlanner.RequiresOfficialLogin(_authenticationState)"));
            Assert.That(bootstrapSource, Does.Not.Contain("!_authenticationState.IsAuthenticated"));
            Assert.That(bootstrapSource, Does.Not.Contain("CodexRuntimeSources"));
            Assert.That(bootstrapSource, Does.Contain("IsCurrentExecutionTargetWindows()"));
            Assert.That(bootstrapSource, Does.Contain("OnAuthenticationCenterMenuItemClick(this, EventArgs.Empty)"));
            Assert.That(bootstrapSource, Does.Contain("_isAuthenticationCenterOpen"));
            Assert.That(bootstrapSource.IndexOf("OnAuthenticationCenterMenuItemClick", StringComparison.Ordinal),
                Is.LessThan(bootstrapSource.IndexOf("new AuthenticationBootstrapForm", StringComparison.Ordinal)));
            Assert.That(bootstrapSource, Does.Not.Contain("RemoteUnixSsh"));
            Assert.That(bootstrapSource, Does.Not.Contain("AuthenticationSource"));
            Assert.That(bootstrapSource, Does.Not.Contain("ChatGptAuthBackup"));
        }

        [Test]
        public void UserCenter_UsesOfficialObservationsAndOnlyTheOfficialResetCreditMutation()
        {
            var source = string.Concat(
                RepositorySource.ReadSourceFile("UserCenterForm.cs"),
                RepositorySource.ReadSourceFile("UserCenterForm.QuotaReset.cs"));

            Assert.That(source, Does.Not.Contain("IChatGptAuthBackupStore"));
            Assert.That(source, Does.Not.Contain("UserCenterAuthenticationDisplayMerger"));
            Assert.That(source, Does.Not.Contain("StartChatGptLoginAsync"));
            Assert.That(source, Does.Not.Contain("StartOpenAiApiKeyLoginAsync"));
            Assert.That(source, Does.Not.Contain("LogoutAsync"));
            Assert.That(source, Does.Contain("GetAuthenticationStateAsync"));
            Assert.That(source, Does.Contain("GetRateLimitsAsync"));
            Assert.That(source, Does.Contain("ConsumeRateLimitResetCreditAsync"));
        }

        [Test]
        public void ClosingUserCenter_DoesNotRefreshAuthenticationOrStartLoginBootstrap()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.cs");
            var handlerStart = source.IndexOf(
                "private async void OnUserCenterMenuItemClick",
                StringComparison.Ordinal);
            var nextHandlerStart = source.IndexOf(
                "private async void OnGoalSetMenuItemClick",
                handlerStart,
                StringComparison.Ordinal);

            Assert.That(handlerStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHandlerStart, Is.GreaterThan(handlerStart));

            var handler = source.Substring(handlerStart, nextHandlerStart - handlerStart);
            var closeBoundary = handler.IndexOf("form.ShowDialog(this);", StringComparison.Ordinal);

            Assert.That(closeBoundary, Is.GreaterThanOrEqualTo(0));
            var afterClose = handler.Substring(closeBoundary);
            Assert.That(afterClose, Does.Not.Contain("RefreshAuthenticationStateAsync"));
            Assert.That(afterClose, Does.Not.Contain("ShowAuthenticationBootstrapIfRequired"));
        }
    }
}
