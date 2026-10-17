using System;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerNotificationEventContext
    {
        public AppServerNotificationEventContext(
            Action<AppServerEventArgs> raiseEvent,
            Action<string> rememberLoadedThreadId,
            Action<string> forgetThreadState,
            Action<string, string> rememberActiveTurnId,
            Action<string> forgetActiveTurnId,
            Func<string, string> mapServerPathToClient,
            Action<string> clearResolvedInteractionRequest)
        {
            RaiseEvent = raiseEvent ?? throw new ArgumentNullException(nameof(raiseEvent));
            RememberLoadedThreadId = rememberLoadedThreadId ?? throw new ArgumentNullException(nameof(rememberLoadedThreadId));
            ForgetThreadState = forgetThreadState ?? throw new ArgumentNullException(nameof(forgetThreadState));
            RememberActiveTurnId = rememberActiveTurnId ?? throw new ArgumentNullException(nameof(rememberActiveTurnId));
            ForgetActiveTurnId = forgetActiveTurnId ?? throw new ArgumentNullException(nameof(forgetActiveTurnId));
            MapServerPathToClient = mapServerPathToClient ?? throw new ArgumentNullException(nameof(mapServerPathToClient));
            ClearResolvedInteractionRequest = clearResolvedInteractionRequest ?? throw new ArgumentNullException(nameof(clearResolvedInteractionRequest));
        }

        public Action<AppServerEventArgs> RaiseEvent { get; private set; }

        public Action<string> RememberLoadedThreadId { get; private set; }

        public Action<string> ForgetThreadState { get; private set; }

        public Action<string, string> RememberActiveTurnId { get; private set; }

        public Action<string> ForgetActiveTurnId { get; private set; }

        public Func<string, string> MapServerPathToClient { get; private set; }

        public Action<string> ClearResolvedInteractionRequest { get; private set; }
    }
}
