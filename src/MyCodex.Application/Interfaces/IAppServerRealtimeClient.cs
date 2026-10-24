using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerRealtimeClient
    {
        event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;

        Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken);

        Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken);

        Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken);
    }

    public interface IRealtimeVoiceAudio : IDisposable
    {
        bool? SystemEchoCancellationEnabled { get; }

        event EventHandler<VoiceAudioEvent> Failed;

        Task<string> PrepareAsync(string sessionId, CancellationToken cancellationToken);

        Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken);

        // The microphone starts disabled; push-to-talk enables it only while the hold key is down.
        Task SetMicrophoneEnabledAsync(string sessionId, bool enabled);

        Task StopAsync(string sessionId);
    }
}
