using System;

namespace MyCodex.Application.RemoteRelay
{
    public enum RelayRealtimeConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    public sealed class RelayRealtimeConnectionStatusEventArgs : EventArgs
    {
        public RelayRealtimeConnectionStatusEventArgs(RelayRealtimeConnectionState state, string message)
        {
            State = state;
            Message = message;
        }

        public RelayRealtimeConnectionState State { get; private set; }

        public string Message { get; private set; }
    }
}
