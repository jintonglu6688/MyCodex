using System;
using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PersistedSessionServiceTests
    {
        [Test]
        public void BuildPersistedSessions_AssignsCurrentRuntimeProfileToActiveSessions()
        {
            var service = new PersistedSessionService();
            var activeSession = new ChatSession
            {
                Id = "active-1",
                WorkspaceId = "workspace-1"
            };
            var inactiveSession = new ChatSession
            {
                Id = "inactive-1",
                WorkspaceId = "workspace-1",
                RuntimeProfileId = "wsl|ubuntu|wsldefault|-"
            };

            var persistedSessions = service.BuildPersistedSessions(
                new[] { activeSession },
                new[] { inactiveSession },
                RuntimeProfileService.WindowsRuntimeProfileId,
                null);

            Assert.That(persistedSessions, Has.Count.EqualTo(2));
            Assert.That(activeSession.RuntimeProfileId, Is.EqualTo(RuntimeProfileService.WindowsRuntimeProfileId));
            Assert.That(inactiveSession.RuntimeProfileId, Is.EqualTo("wsl|ubuntu|wsldefault|-"));
        }

        [Test]
        public void AddOrReplacePersistedSession_PrefersPreferredSession_WhenKeysMatch()
        {
            var service = new PersistedSessionService();
            var sessions = new List<ChatSession>();
            var sessionIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var existing = new ChatSession
            {
                Id = "existing",
                WorkspaceId = "workspace-1",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                ThreadId = "thread-1",
                LastActiveAt = new DateTime(2026, 4, 1, 10, 0, 0),
                CreatedAt = new DateTime(2026, 4, 1, 9, 0, 0)
            };
            var preferred = new ChatSession
            {
                Id = "preferred",
                WorkspaceId = "workspace-1",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                ThreadId = "thread-1",
                LastActiveAt = new DateTime(2026, 3, 31, 10, 0, 0),
                CreatedAt = new DateTime(2026, 3, 31, 9, 0, 0)
            };

            service.AddOrReplacePersistedSession(sessions, sessionIndexes, existing, preferred.Id);
            service.AddOrReplacePersistedSession(sessions, sessionIndexes, preferred, preferred.Id);

            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0].Id, Is.EqualTo(preferred.Id));
        }

        [Test]
        public void NormalizeRestoredSessionTimestamps_ConvertsUtcValuesToLocalKind()
        {
            var service = new PersistedSessionService();
            var session = new ChatSession
            {
                CreatedAt = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
                LastActiveAt = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc)
            };

            service.NormalizeRestoredSessionTimestamps(session);

            Assert.That(session.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Local));
            Assert.That(session.LastActiveAt.Kind, Is.EqualTo(DateTimeKind.Local));
        }

        [Test]
        public void BuildPersistedSessionKey_SeparatesOfficeSessionsByOfficeRuntimeScope()
        {
            var service = new PersistedSessionService();
            var windows = new ChatSession
            {
                Id = "session-windows",
                WorkspaceId = "same-workspace",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "windows",
                Mode = AppInteractionMode.Office,
                ThreadId = "same-thread"
            };
            var wsl = new ChatSession
            {
                Id = "session-wsl",
                WorkspaceId = "same-workspace",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "wsl|ubuntu",
                Mode = AppInteractionMode.Office,
                ThreadId = "same-thread"
            };

            Assert.That(service.BuildPersistedSessionKey(windows),
                Is.Not.EqualTo(service.BuildPersistedSessionKey(wsl)));
        }

        [Test]
        public void BuildPersistedSessionKey_OfficeBehaviorIgnoresCodingRuntimeProfile()
        {
            var service = new PersistedSessionService();
            var first = new ChatSession
            {
                Id = "session-first",
                WorkspaceId = "same-workspace",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "wsl|ubuntu",
                Mode = AppInteractionMode.Office,
                ThreadId = "same-thread"
            };
            var second = new ChatSession
            {
                Id = "session-second",
                WorkspaceId = "same-workspace",
                RuntimeProfileId = "wsl|ubuntu|wsldefault|-",
                OfficeRuntimeScopeId = "wsl|ubuntu",
                Mode = AppInteractionMode.Office,
                ThreadId = "same-thread"
            };

            Assert.That(service.BuildPersistedSessionKey(first),
                Is.EqualTo(service.BuildPersistedSessionKey(second)));
        }

        [Test]
        public void BuildPersistedSessionKey_CodingBehaviorIgnoresOfficeRuntimeScope()
        {
            var service = new PersistedSessionService();
            var first = new ChatSession
            {
                Id = "session",
                WorkspaceId = "workspace",
                RuntimeProfileId = "windows",
                OfficeRuntimeScopeId = "windows",
                Mode = AppInteractionMode.Coding,
                ThreadId = "thread"
            };
            var second = new ChatSession
            {
                Id = "session",
                WorkspaceId = "workspace",
                RuntimeProfileId = "windows",
                OfficeRuntimeScopeId = "wsl|ubuntu",
                Mode = AppInteractionMode.Coding,
                ThreadId = "thread"
            };

            Assert.That(service.BuildPersistedSessionKey(first),
                Is.EqualTo(service.BuildPersistedSessionKey(second)));
        }

        [Test]
        public void BuildPersistedSessionKey_CodingBehaviorSeparatesRuntimeProfiles()
        {
            var service = new PersistedSessionService();
            var windows = new ChatSession
            {
                Id = "session",
                WorkspaceId = "workspace",
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                Mode = AppInteractionMode.Coding,
                ThreadId = "thread"
            };
            var wsl = new ChatSession
            {
                Id = "session",
                WorkspaceId = "workspace",
                RuntimeProfileId = "wsl|ubuntu|wsldefault|-",
                Mode = AppInteractionMode.Coding,
                ThreadId = "thread"
            };

            Assert.That(service.BuildPersistedSessionKey(windows),
                Is.Not.EqualTo(service.BuildPersistedSessionKey(wsl)));
        }
    }
}
