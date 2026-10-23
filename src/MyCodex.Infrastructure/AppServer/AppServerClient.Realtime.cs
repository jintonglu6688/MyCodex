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
        public event EventHandler<RealtimeVoiceInputEvent> RealtimeTextInputSubmitted;

        private void NotifyRealtimeTextInputSubmitted(string threadId, IReadOnlyCollection<AppServerUserInputItem> items)
        {
            var observer = RealtimeTextInputSubmitted;
            if (observer == null) return;
            var text = AppServerVoiceContext.BuildTypedInput(items);
            if (string.IsNullOrWhiteSpace(text)) return;
            // A voice observer must never turn an accepted text request into a failed send/retry.
            try { observer(this, new RealtimeVoiceInputEvent { ThreadId = threadId, Text = text }); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Voice input observer failed: {0}", ex.GetType().Name); }
        }

        public Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).ListVoicesAsync(cancellationToken);
        }

        public async Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var epoch = GetConnectionEpoch();
            await EnsureThreadLoadedAsync(request.ThreadId, request.WorkspacePath, request.ApprovalMode).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != GetConnectionEpoch()) throw new OperationCanceledException();
            await new AppServerRealtimeOperations(SendRequestAsync).StartAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).StopAsync(threadId, cancellationToken);
        }

        public async Task<string> CreateVoicePreviewThreadAsync(string workspacePath, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var id = await new AppServerRealtimeOperations(SendRequestAsync).CreatePreviewThreadAsync(
                MapRequiredClientPathToServer(workspacePath, "workspace"), cancellationToken).ConfigureAwait(false);
            lock (_syncRoot) _loadedThreadIds.Add(id);
            return id;
        }

        public Task ReleaseVoicePreviewThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UnsubscribeThreadAsync(threadId);
        }

        public Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).AppendSpeechAsync(threadId, text, cancellationToken);
        }

        public Task AppendRealtimeContextAsync(string threadId, string text, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).AppendContextAsync(threadId, text, cancellationToken);
        }

        public Task UpdateRealtimeBackendAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).UpdateBackendAsync(request, cancellationToken);
        }

        private bool HandleRealtimeVoiceNotification(string method, IDictionary<string, object> parameters)
        {
            if (method == null || !method.StartsWith("thread/realtime/", StringComparison.Ordinal)) return false;
            var notification = AppServerRealtimeOperations.MapNotification(method, parameters);
            if (notification != null) RealtimeVoiceEventReceived?.Invoke(this, notification);
            return true;
        }
    }
}
