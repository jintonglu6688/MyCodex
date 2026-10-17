using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private RemoteRelayEvent DispatchPendingInteractions(RemoteRelayCommand command)
        {
            var pending = _pendingInteractions == null ? new RemoteRelayEvent[0] : _pendingInteractions() ?? new RemoteRelayEvent[0];
            return CreateEvent(command, RemoteEventTypes.InteractionPendingListResult, new Dictionary<string, object>
            {
                { "events", pending.Where(item => item != null).Select(item => new Dictionary<string, object>
                    {
                        { "eventType", item.EventType }, { "requestId", item.RequestId },
                        { "operationId", item.OperationId }, { "payload", item.Payload }
                    }).ToArray() }
            });
        }
    }
}
