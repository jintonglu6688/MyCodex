using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    /// <summary>
    /// Publishes the Windows pending questions queue to the phone: a question is offered once it
    /// is queued and withdrawn once it is answered or ignored on either side.
    /// </summary>
    internal static class RemoteAsyncInputEvents
    {
        internal static void PublishQueued(RemoteAppServerEventForwarder forwarder, PendingAsyncInputRequest pending)
        {
            _ = PublishAsync(forwarder, pending, RemoteAsyncInputRequests.QueuedEventType);
        }

        internal static void PublishResolved(RemoteAppServerEventForwarder forwarder, PendingAsyncInputRequest pending)
        {
            _ = PublishAsync(forwarder, pending, RemoteAsyncInputRequests.ResolvedEventType);
        }

        private static async Task PublishAsync(RemoteAppServerEventForwarder forwarder, PendingAsyncInputRequest pending, string eventType)
        {
            if (forwarder == null || pending == null)
            {
                return;
            }

            var source = pending.Source;
            try
            {
                await forwarder.ForwardAsync(
                    new AppServerEventArgs(
                        eventType,
                        source.Content,
                        source.ThreadId,
                        source.TurnId,
                        source.ItemId,
                        RemoteAsyncInputRequests.CreateRequestId(source),
                        details: pending.SourceName,
                        toolQuestions: source.ToolQuestions),
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // The Windows pending questions window remains available if the phone channel cannot publish.
            }
        }
    }
}
