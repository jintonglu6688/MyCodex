using System;
using System.Collections.Generic;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class RealtimeVoiceCatalog
    {
        public IReadOnlyList<string> Voices { get; set; } = new string[0];

        public string DefaultVoice { get; set; }
    }

    public sealed class RealtimeVoiceStart
    {
        public string ThreadId { get; set; }

        public string WorkspacePath { get; set; }

        public ApprovalMode? ApprovalMode { get; set; }

        public string SessionId { get; set; }

        public string OfferSdp { get; set; }

        public string Voice { get; set; }

        public bool FasterSpeech { get; set; }

        // Local playback only; 100 leaves the official volume unchanged.
        public int OutputVolumePercent { get; set; } = 100;

        // Applied with thread/settings/update so delegated work uses the conversation's model, effort and mode.
        public AppServerCollaborationMode BackendMode { get; set; }
    }

    public enum RealtimeVoiceEventKind
    {
        Started,
        Sdp,
        Closed,
        Error,
        Transcript
    }

    public sealed class RealtimeVoiceEvent : EventArgs
    {
        public RealtimeVoiceEventKind Kind { get; set; }

        public string ThreadId { get; set; }

        public string SessionId { get; set; }

        public string Sdp { get; set; }

        public string Text { get; set; }

        public string Role { get; set; }

        public string ItemId { get; set; }
    }

    public enum VoiceChatState
    {
        Idle,
        Connecting,
        Active,
        Stopping,
        Failed
    }

    public sealed class VoiceChatStateEvent : EventArgs
    {
        public VoiceChatState State { get; set; }

        public string Error { get; set; }
    }

    public sealed class VoiceAudioEvent : EventArgs
    {
        public string SessionId { get; set; }

        public string Error { get; set; }
    }
}
