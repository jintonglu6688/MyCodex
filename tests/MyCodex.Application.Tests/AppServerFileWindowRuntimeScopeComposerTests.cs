using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileWindowRuntimeScopeComposerTests
    {
        [Test]
        public void FromSnapshot_UsesProvidedRuntimeProfileAndInteractionMode()
        {
            var scope = AppServerFileWindowRuntimeScopeComposer.FromSnapshot(
                null,
                "profile-1",
                AppInteractionMode.Office);

            Assert.That(scope.RuntimeSource, Is.Null);
            Assert.That(scope.RuntimeProfileId, Is.EqualTo("profile-1"));
            Assert.That(scope.RemoteProfileId, Is.Null);
            Assert.That(scope.InteractionMode, Is.EqualTo(AppInteractionMode.Office));
        }

        [Test]
        public void FromSnapshot_MapsRuntimeSourceAndRemoteProfile()
        {
            var snapshot = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                SelectedRemoteUnixSshProfileId = "remote-1"
            };

            var scope = AppServerFileWindowRuntimeScopeComposer.FromSnapshot(
                snapshot,
                "runtime-profile-1",
                AppInteractionMode.Coding);

            Assert.That(scope.RuntimeSource, Is.EqualTo(CodexRuntimeSources.RemoteUnixSsh));
            Assert.That(scope.RuntimeProfileId, Is.EqualTo("runtime-profile-1"));
            Assert.That(scope.RemoteProfileId, Is.EqualTo("remote-1"));
            Assert.That(scope.InteractionMode, Is.EqualTo(AppInteractionMode.Coding));
        }
    }
}
