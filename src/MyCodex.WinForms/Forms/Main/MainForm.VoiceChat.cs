using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.Instances;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private VoiceChatCoordinator _voiceChat;
        private readonly HashSet<string> _visibleVoiceTranscriptIds = new HashSet<string>(StringComparer.Ordinal);

        private void InitializeVoiceChatUi()
        {
            _voiceChat = new VoiceChatCoordinator(this, _appServerClient as IAppServerRealtimeClient, _appServerClient,
                Path.Combine(_appConfigService.GetAppDataDirectory(), "voice-browser"), PrepareVoiceConversationAsync,
                () => _appConfigSnapshot.VoiceChat, SaveVoiceChatSettings, SetStatus);
            _voiceChat.TranscriptReceived += (sender, message) => ShowVoiceTranscript(message);
            Disposed += (sender, args) => _voiceChat.Dispose();
        }

        private async Task<TargetInstanceReply> ExecuteVoiceControlAsync(TargetInstanceRequest request)
        {
            var threadId = request.VoiceThreadId ?? _currentSession?.ThreadId;
            if (IsDisposed || IsTargetExitPreparing || _currentSession?.ThreadId != threadId)
                return TargetInstanceReply.Failure("The calling conversation is no longer selected.");
            var result = await _voiceChat.ApplyControlAsync(VoiceControlCommand.Parse(request.VoiceArguments), threadId,
                () => !IsDisposed && !IsTargetExitPreparing && _currentSession?.ThreadId == threadId);
            return new TargetInstanceReply { Success = result.Success, VoiceThreadId = threadId,
                VoiceResult = result.StatusText, Error = result.Success ? null : result.StatusText };
        }

        private async Task<VoiceConversationBinding> PrepareVoiceConversationAsync()
        {
            if (IsTargetExitPreparing || _isExitRequested) return null;
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("VoiceMode.Accessible.Enable", "Enable voice"))) return null;
            EnsureCurrentSessionMatchesInteractionMode();
            if (_currentSession == null)
            {
                var draft = CaptureCurrentComposeDraftState();
                await CreateNewSessionAsync();
                RestoreComposeDraftState(draft);
            }
            var session = _currentSession;
            if (session == null) return null;
            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace)) throw new InvalidOperationException(UiLocalization.Text("MainForm.Message.WorkspaceUnavailable", "The workspace is unavailable."));
            await EnsureSessionThreadCreatedAsync(session);
            if (!ReferenceEquals(session, _currentSession) || IsDisposed || IsTargetExitPreparing || _isExitRequested) return null;
            var modelId = GetEffectiveModelId(session);
            var effort = GetEffectiveReasoningEffort(session);
            var backendMode = string.IsNullOrWhiteSpace(modelId) ? null :
                BuildCollaborationModeForSend(session, workspace, modelId, effort) ?? CreateCollaborationMode("default", modelId, effort);
            return new VoiceConversationBinding
            {
                ConversationId = session.Id,
                Request = new RealtimeVoiceStart
                {
                    ThreadId = session.ThreadId, WorkspacePath = workspace.RootPath, ApprovalMode = GetEffectiveApprovalMode(session),
                    BackendMode = backendMode
                }
            };
        }

        private bool SaveVoiceChatSettings(VoiceChatSettings settings)
        {
            var previous = _appConfigSnapshot.VoiceChat;
            _appConfigSnapshot.VoiceChat = settings;
            if (SaveAppConfig()) return true;
            _appConfigSnapshot.VoiceChat = previous;
            return false;
        }

        private void ShowVoiceTranscript(RealtimeVoiceEvent message)
        {
            if (string.IsNullOrWhiteSpace(message.ItemId)) return;
            _sessionHistoryStateStore.Invalidate(message.ThreadId);
            if (_currentSession?.ThreadId != message.ThreadId || !_visibleVoiceTranscriptIds.Add(message.ItemId)) return;
            var item = AddChatHistoryItem(message.Role == "user" ? "User" : "Codex", message.Text,
                message.DisplayAsProcessing ? ChatHistoryCategory.Processing : ChatHistoryCategory.Conversation);
            item.SourceItemId = message.ItemId;
            item.IsVoiceTranscript = true;
        }
    }
}
