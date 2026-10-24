using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerRealtimeClient
    {
        event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;

        // Raised after a typed message was accepted by turn/start or turn/steer; carries ThreadId and Text.
        event EventHandler<RealtimeVoiceEvent> TypedInputSubmitted;

        Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken);

        Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken);

        Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken);

        // Tells the live voice about a request typed into the composer so it answers that result aloud.
        Task AppendRealtimeTypedRequestAsync(string threadId, string text, CancellationToken cancellationToken);

        // Asks the live voice to say something without a user turn; the preview uses it for its sample.
        Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken cancellationToken);

        // A temporary thread that never appears as a session; release it after the preview.
        Task<string> CreateVoicePreviewThreadAsync(CancellationToken cancellationToken);

        Task ReleaseVoicePreviewThreadAsync(string threadId);
    }

    public interface IRealtimeVoiceAudio : IDisposable
    {
        bool? SystemEchoCancellationEnabled { get; }

        event EventHandler<VoiceAudioEvent> Failed;

        // Raised for a listen-only session once the voice has spoken and gone quiet.
        event EventHandler<VoiceAudioEvent> PlaybackFinished;

        Task<string> PrepareAsync(string sessionId, int outputVolumePercent, bool listenOnly, CancellationToken cancellationToken);

        Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken);

        // The microphone starts disabled; push-to-talk enables it only while the user is talking.
        Task SetMicrophoneEnabledAsync(string sessionId, bool enabled);

        Task StopAsync(string sessionId);
    }
}
