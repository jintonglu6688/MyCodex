using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteSessionStopperTests
    {
        [Test]
        public async Task StopSessionAsync_OfficeModeStopsOfficeSession()
        {
            var store = CreateStore();
            var client = new RecordingInteractionClient();
            var stopper = new AppServerCodingRemoteSessionStopper(
                store,
                client,
                threadId => threadId == "thread-office",
                threadId => threadId == "thread-office" ? "turn-office" : null,
                AppInteractionMode.Office);

            var result = await stopper.StopSessionAsync(
                new CodingRemoteSessionStopRequest { SessionId = "office-session" },
                CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("office-session"));
            Assert.That(result.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(result.Stopped, Is.True);
            Assert.That(client.LastInterruptedThreadId, Is.EqualTo("thread-office"));
            Assert.That(client.LastInterruptedTurnId, Is.EqualTo("turn-office"));
        }

        private static FakeAppStateStore CreateStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "coding-session",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-coding"
                        },
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office,
                            ThreadId = "thread-office"
                        }
                    }
                }
            };
        }

        private sealed class FakeAppStateStore : IAppStateStore
        {
            public AppStateSnapshot Snapshot { get; set; }

            public AppStateSnapshot Load()
            {
                return Snapshot ?? new AppStateSnapshot();
            }

            public void Save(AppStateSnapshot snapshot)
            {
                Snapshot = snapshot;
            }
        }

        private sealed class RecordingInteractionClient : IAppServerInteractionClient
        {
            public string LastInterruptedThreadId { get; private set; }

            public string LastInterruptedTurnId { get; private set; }

            public bool HasInterruptibleTurn(string threadId)
            {
                return false;
            }

            public Task InterruptAsync(string threadId, string turnId = null)
            {
                LastInterruptedThreadId = threadId;
                LastInterruptedTurnId = turnId;
                return Task.CompletedTask;
            }

            public Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision)
            {
                return Task.CompletedTask;
            }

            public Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers)
            {
                return Task.CompletedTask;
            }

            public Task CancelToolUserInputRequestAsync(string requestId)
            {
                return Task.CompletedTask;
            }
        }
    }
}
