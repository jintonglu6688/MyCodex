using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient : IAppServerRealtimeClient
    {
        public event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;

        public event EventHandler<RealtimeVoiceEvent> TypedInputSubmitted;

        public Task AppendRealtimeTypedRequestAsync(string threadId, string text, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).AppendTypedRequestAsync(threadId, text, cancellationToken);
        }

        private void NotifyTypedInputSubmitted(string threadId, IEnumerable<AppServerUserInputItem> inputItems)
        {
            var observer = TypedInputSubmitted;
            var text = observer == null ? null : AppServerRealtimeOperations.BuildTypedRequest(inputItems);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // A voice observer must never turn an accepted message into a failed send.
            try
            {
                observer(this, new RealtimeVoiceEvent { ThreadId = threadId, Text = text });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Voice typed input observer failed: {0}", ex.GetType().Name);
            }
        }

        public Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).ListVoicesAsync(cancellationToken);
        }

        public async Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            EnsureConnected();
            var epoch = GetConnectionEpoch();
            await EnsureThreadLoadedAsync(request.ThreadId, request.WorkspacePath, request.ApprovalMode).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != GetConnectionEpoch())
            {
                throw new OperationCanceledException();
            }

            await new AppServerRealtimeOperations(SendRequestAsync).StartAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).StopAsync(threadId, cancellationToken);
        }

        // Realtime notifications go only to voice listeners; the chat event pipeline ignores them.
        private bool HandleRealtimeVoiceNotification(string method, IDictionary<string, object> parameters)
        {
            if (method == null || !method.StartsWith("thread/realtime/", StringComparison.Ordinal))
            {
                return false;
            }

            var notification = AppServerRealtimeOperations.MapNotification(method, parameters);
            if (notification != null)
            {
                RealtimeVoiceEventReceived?.Invoke(this, notification);
            }

            return true;
        }
    }
}
