using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteSessionStopper : ICodingRemoteSessionStopper
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerInteractionClient _interactionClient;
        private readonly Func<string, bool> _canInterruptThread;
        private readonly Func<string, string> _turnIdResolver;
        private readonly AppInteractionMode _interactionMode;

        public AppServerCodingRemoteSessionStopper(
            IAppStateStore appStateStore,
            IAppServerInteractionClient interactionClient,
            Func<string, bool> canInterruptThread,
            Func<string, string> turnIdResolver)
            : this(appStateStore, interactionClient, canInterruptThread, turnIdResolver, AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteSessionStopper(
            IAppStateStore appStateStore,
            IAppServerInteractionClient interactionClient,
            Func<string, bool> canInterruptThread,
            Func<string, string> turnIdResolver,
            AppInteractionMode interactionMode)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _interactionClient = interactionClient ?? throw new ArgumentNullException(nameof(interactionClient));
            _canInterruptThread = canInterruptThread ?? throw new ArgumentNullException(nameof(canInterruptThread));
            _turnIdResolver = turnIdResolver ?? throw new ArgumentNullException(nameof(turnIdResolver));
            _interactionMode = interactionMode;
        }

        public async Task<CodingRemoteSessionStopResult> StopSessionAsync(
            CodingRemoteSessionStopRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteSessionStopRequest();
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidOperationException("Session ID is required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindSession(snapshot, request.SessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.OperationNotAvailable, "Session has no active thread.");
            }

            if (!_canInterruptThread(session.ThreadId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.OperationNotAvailable, "No active generation can be stopped.");
            }

            await _interactionClient.InterruptAsync(session.ThreadId, _turnIdResolver(session.ThreadId)).ConfigureAwait(false);
            return new CodingRemoteSessionStopResult
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Stopped = true,
                StatusText = "Stop requested."
            };
        }

        private ChatSession FindSession(AppStateSnapshot snapshot, string sessionId)
        {
            return (snapshot.Sessions ?? new List<ChatSession>())
                .FirstOrDefault(session =>
                    session != null &&
                    session.Mode == _interactionMode &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
