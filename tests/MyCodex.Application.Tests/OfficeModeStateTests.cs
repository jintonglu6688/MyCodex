using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization.Json;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeModeStateTests
    {
        [Test]
        public void ModeSettingsAndAppConfig_HaveSeparatedDefaults()
        {
            var modeSettings = new ModeSettingsSnapshot();
            var appConfig = new AppConfigSnapshot();

            Assert.That(modeSettings.DefaultStartupMode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(modeSettings.OfficeRootPath, Is.Null);
            Assert.That(appConfig.OfficeShowActivityPane, Is.True);
        }

        [Test]
        public void AppStateSnapshot_DefaultsToCodingModeAndIncompleteStartupModeOnboarding()
        {
            var snapshot = new AppStateSnapshot();

            Assert.That(snapshot.HasCompletedStartupModeOnboarding, Is.False);
            Assert.That(snapshot.HasCompletedOfficeCliOnboarding, Is.False);
            Assert.That(snapshot.CurrentInteractionMode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(snapshot.CurrentCodingWorkspaceId, Is.Null);
            Assert.That(snapshot.CurrentCodingSessionId, Is.Null);
            Assert.That(snapshot.CurrentOfficeWorkspaceId, Is.Null);
            Assert.That(snapshot.CurrentOfficeSessionId, Is.Null);
            Assert.That(snapshot.OfficeRuntimeStates, Is.Empty);
        }

        [Test]
        public void WorkspaceAndSession_DefaultToCodingForLegacySnapshots()
        {
            var workspace = new WorkspaceInfo();
            var session = new ChatSession();

            Assert.That(workspace.Kind, Is.EqualTo(WorkspaceKind.Coding));
            Assert.That(session.Mode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(session.OfficeRuntimeScopeId, Is.Null);
            Assert.That(new AutomationContextSnapshot().OfficeRuntimeScopeId, Is.Null);
        }

        [Test]
        public void AppStateSnapshot_RoundTripsOfficeModeFields()
        {
            var snapshot = new AppStateSnapshot
            {
                HasCompletedStartupModeOnboarding = true,
                HasCompletedOfficeCliOnboarding = true,
                CurrentInteractionMode = AppInteractionMode.Office,
                CurrentCodingWorkspaceId = "coding-workspace",
                CurrentCodingSessionId = "coding-session",
                CurrentOfficeWorkspaceId = "office-workspace",
                CurrentOfficeSessionId = "office-session",
                OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                {
                    new OfficeRuntimeStateSnapshot
                    {
                        RuntimeScopeId = "wsl|ubuntu",
                        RecentWorkspaces = new List<WorkspaceInfo>
                        {
                            new WorkspaceInfo
                            {
                                Id = "office-workspace",
                                RootPath = "/home/alice/MyCodex-office",
                                LastOpenedAt = System.DateTime.UtcNow
                            }
                        },
                        CurrentWorkspaceId = "office-workspace",
                        CurrentSessionId = "office-session"
                    }
                },
                Sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "office-session",
                        OfficeRuntimeScopeId = "wsl|ubuntu",
                        Mode = AppInteractionMode.Office,
                        CreatedAt = System.DateTime.UtcNow,
                        LastActiveAt = System.DateTime.UtcNow
                    }
                }
            };

            var reloaded = RoundTrip(snapshot);

            Assert.That(reloaded.HasCompletedStartupModeOnboarding, Is.True);
            Assert.That(reloaded.HasCompletedOfficeCliOnboarding, Is.True);
            Assert.That(reloaded.CurrentInteractionMode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(reloaded.CurrentCodingWorkspaceId, Is.EqualTo("coding-workspace"));
            Assert.That(reloaded.CurrentCodingSessionId, Is.EqualTo("coding-session"));
            Assert.That(reloaded.CurrentOfficeWorkspaceId, Is.EqualTo("office-workspace"));
            Assert.That(reloaded.CurrentOfficeSessionId, Is.EqualTo("office-session"));
            Assert.That(reloaded.OfficeRuntimeStates, Has.Count.EqualTo(1));
            Assert.That(reloaded.OfficeRuntimeStates[0].RuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(reloaded.OfficeRuntimeStates[0].RecentWorkspaces[0].RootPath,
                Is.EqualTo("/home/alice/MyCodex-office"));
            Assert.That(reloaded.Sessions[0].OfficeRuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
        }

        [Test]
        public void ModeSettingsAndAutomationScope_RoundTripIndependently()
        {
            var modeSettings = new ModeSettingsSnapshot
            {
                DefaultStartupMode = AppInteractionMode.Office,
                OfficeRootPath = "~/MyCodex-office"
            };
            var automation = new AutomationContextSnapshot
            {
                OfficeRuntimeScopeId = "remote-unix|profile-a"
            };

            var reloadedModeSettings = RoundTrip(modeSettings);
            var reloadedAutomation = RoundTrip(automation);

            Assert.That(reloadedModeSettings.DefaultStartupMode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(reloadedModeSettings.OfficeRootPath, Is.EqualTo("~/MyCodex-office"));
            Assert.That(reloadedAutomation.OfficeRuntimeScopeId, Is.EqualTo("remote-unix|profile-a"));
        }

        private static AppStateSnapshot RoundTrip(AppStateSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(AppStateSnapshot));
                serializer.WriteObject(stream, snapshot);
                stream.Position = 0;
                return (AppStateSnapshot)serializer.ReadObject(stream);
            }
        }

        private static ModeSettingsSnapshot RoundTrip(ModeSettingsSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(ModeSettingsSnapshot));
                serializer.WriteObject(stream, snapshot);
                stream.Position = 0;
                return (ModeSettingsSnapshot)serializer.ReadObject(stream);
            }
        }

        private static AutomationContextSnapshot RoundTrip(AutomationContextSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(AutomationContextSnapshot));
                serializer.WriteObject(stream, snapshot);
                stream.Position = 0;
                return (AutomationContextSnapshot)serializer.ReadObject(stream);
            }
        }
    }
}
