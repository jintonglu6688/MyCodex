using System;
using System.Collections.Generic;
using MyCodex.Domain.Enums;
using MyCodex.Domain.Entities;

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
        public bool MicrophoneMuted { get; set; }
        public VoiceOutputLevel OutputLevel { get; set; } = VoiceOutputLevel.Original;
        public bool IsPreview { get; set; }
        public string PreviewText { get; set; }
        public AppServerCollaborationMode BackendMode { get; set; }
    }

    public sealed class VoiceAudioOptions
    {
        public bool Muted { get; set; }
        public bool Preview { get; set; }
        public string Voice { get; set; }
        public bool FasterSpeech { get; set; }
        public string PreviewText { get; set; }
        public VoiceOutputLevel OutputLevel { get; set; } = VoiceOutputLevel.Original;
    }

    public sealed class RealtimeVoiceContextItem
    {
        public string ItemId { get; set; }
        public string Role { get; set; }
        public string Text { get; set; }
        public bool IsVoice { get; set; }
        public bool IsVoiceBackend { get; set; }
        public string MessagePhase { get; set; }
        public string RealtimeSessionId { get; set; }
    }

    public sealed class RealtimeVoiceContextPage
    {
        public string ThreadId { get; set; }
        public IReadOnlyList<RealtimeVoiceContextItem> Items { get; set; } = new RealtimeVoiceContextItem[0];
        public string NextCursor { get; set; }
    }

    public enum RealtimeVoiceEventKind { Started, Sdp, Closed, Error, Transcript }

    public sealed class RealtimeVoiceInputEvent : EventArgs
    {
        public string ThreadId { get; set; }
        public string Text { get; set; }
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
        public bool DisplayAsProcessing { get; set; }
    }

    public enum VoiceChatState { Idle, Connecting, Active, Stopping, Failed }

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
