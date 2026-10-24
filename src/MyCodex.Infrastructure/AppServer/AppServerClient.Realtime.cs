using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient : IAppServerRealtimeClient
    {
        private readonly ConcurrentDictionary<string, bool> _voiceThreadIds = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

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

        public Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken cancellationToken)
        {
            EnsureConnected();
            return new AppServerRealtimeOperations(SendRequestAsync).AppendSpeechAsync(threadId, text, cancellationToken);
        }

        public async Task<string> CreateVoicePreviewThreadAsync(CancellationToken cancellationToken)
        {
            EnsureConnected();
            var threadId = await new AppServerRealtimeOperations(SendRequestAsync).CreatePreviewThreadAsync(cancellationToken).ConfigureAwait(false);
            RememberLoadedThreadId(threadId);
            return threadId;
        }

        public Task ReleaseVoicePreviewThreadAsync(string threadId)
        {
            return UnsubscribeThreadAsync(threadId);
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
            if (request.PreviewText == null)
            {
                _voiceThreadIds[request.ThreadId] = true;
            }
        }

        // shortcut: a voice-only chat with no task handed to Codex is recognized only in the process that held it; persist a session flag if users miss it after restart.
        private Task MergeVoiceHistoryAsync(string threadId, AppServerThreadDetails details, CancellationToken cancellationToken)
        {
            var voiceUsed = details != null && !string.IsNullOrEmpty(threadId) &&
                (_voiceThreadIds.ContainsKey(threadId) || details.Messages.Any(message => message.IsVoiceDelegation));
            return voiceUsed && ProtocolCapabilities?.SupportsRealtimeVoice == true
                ? AppServerVoiceHistory.MergeAsync(SendRequestAsync, threadId, details, cancellationToken)
                : Task.CompletedTask;
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
