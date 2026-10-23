using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerRealtimeClient
    {
        event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;
        event EventHandler<RealtimeVoiceInputEvent> RealtimeTextInputSubmitted;
        Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken);
        Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken);
        Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken);
        Task<string> CreateVoicePreviewThreadAsync(string workspacePath, CancellationToken cancellationToken);
        Task ReleaseVoicePreviewThreadAsync(string threadId, CancellationToken cancellationToken);
        Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken cancellationToken);
        Task AppendRealtimeContextAsync(string threadId, string text, CancellationToken cancellationToken);
        Task UpdateRealtimeBackendAsync(RealtimeVoiceStart request, CancellationToken cancellationToken);
    }

    public interface IRealtimeVoiceAudio : IDisposable
    {
        bool? SystemEchoCancellationEnabled { get; }
        event EventHandler<VoiceAudioEvent> Failed;
        event EventHandler<VoiceAudioEvent> PreviewCompleted;
        Task<bool> TryReplayPreviewAsync(string sessionId, VoiceAudioOptions options, CancellationToken cancellationToken);
        Task<string> PrepareAsync(string sessionId, VoiceAudioOptions options, CancellationToken cancellationToken);
        Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken);
        Task SetMutedAsync(string sessionId, bool muted);
        Task SetPlaybackMutedAsync(string sessionId, bool muted);
        Task SetOutputLevelAsync(string sessionId, VoiceOutputLevel level);
        Task StopAsync(string sessionId);
    }
}
