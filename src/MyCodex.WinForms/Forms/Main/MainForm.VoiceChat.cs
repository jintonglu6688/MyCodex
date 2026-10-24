using System;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private VoiceChatCoordinator _voiceChat;

        private void InitializeVoiceChatUi()
        {
            var realtimeClient = _appServerClient as IAppServerRealtimeClient;
            _voiceChat = new VoiceChatCoordinator(
                this,
                CreateActionButton(string.Empty, string.Empty, 7, null),
                realtimeClient,
                _appServerClient,
                realtimeClient == null ? null : new WebViewVoiceAudio(this, Path.Combine(_appConfigService.GetAppDataDirectory(), "voice-browser")),
                PrepareVoiceConversationAsync,
                () => _appConfigSnapshot.VoiceChat,
                SaveVoiceChatSettings,
                SetStatus);
            _voiceChat.TranscriptReceived += (sender, message) => ShowVoiceTranscript(message);
            Disposed += (sender, args) => _voiceChat.Dispose();
        }

        private void RefreshVoiceChatState(bool workspaceAvailable)
        {
            // Voice is limited to the local Windows target; WSL and remote SSH targets keep the button disabled.
            _voiceChat?.Refresh(
                _currentSession?.Id,
                workspaceAvailable && _appServerClient.ProtocolCapabilities?.SupportsRealtimeVoice == true);
        }

        private async Task<VoiceConversationBinding> PrepareVoiceConversationAsync()
        {
            if (IsTargetExitPreparing || _isExitRequested)
            {
                return null;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("VoiceChat.Accessible.Start", "Start voice")))
            {
                return null;
            }

            EnsureCurrentSessionMatchesInteractionMode();
            if (_currentSession == null)
            {
                var draft = CaptureCurrentComposeDraftState();
                await CreateNewSessionAsync();
                RestoreComposeDraftState(draft);
            }

            var session = _currentSession;
            if (session == null)
            {
                return null;
            }

            await EnsureSessionThreadCreatedAsync(session);
            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (!ReferenceEquals(session, _currentSession) || IsDisposed || IsTargetExitPreparing || _isExitRequested || !IsWorkspaceAvailable(workspace))
            {
                return null;
            }

            var modelId = GetEffectiveModelId(session);
            var effort = GetEffectiveReasoningEffort(session);
            var backendMode = string.IsNullOrWhiteSpace(modelId)
                ? null
                : BuildCollaborationModeForSend(session, workspace, modelId, effort) ?? CreateCollaborationMode("default", modelId, effort);
            return new VoiceConversationBinding
            {
                ConversationId = session.Id,
                Request = new RealtimeVoiceStart
                {
                    ThreadId = session.ThreadId,
                    WorkspacePath = workspace.RootPath,
                    ApprovalMode = GetEffectiveApprovalMode(session),
                    BackendMode = backendMode
                }
            };
        }

        private bool SaveVoiceChatSettings(VoiceChatSettings settings)
        {
            var previous = _appConfigSnapshot.VoiceChat;
            _appConfigSnapshot.VoiceChat = settings;
            var saved = SaveAppConfig();
            if (!saved)
            {
                _appConfigSnapshot.VoiceChat = previous;
            }

            return saved;
        }

        private void ShowVoiceTranscript(RealtimeVoiceEvent message)
        {
            InvalidateSessionHistoryCache(message.ThreadId);
            if (_currentSession == null || !string.Equals(_currentSession.ThreadId, message.ThreadId, StringComparison.Ordinal))
            {
                return;
            }

            var sender = string.Equals(message.Role, "user", StringComparison.Ordinal)
                ? ChatHistoryItemFactory.GetUserSenderText()
                : ChatHistoryItemFactory.GetCodexSenderText();
            AddChatHistoryItem(sender, message.Text, ChatHistoryCategory.Conversation);
        }
    }
}
