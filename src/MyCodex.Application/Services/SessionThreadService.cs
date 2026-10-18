using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed partial class SessionThreadService
    {
        private readonly IAppServerThreadClient _appServerClient;
        private readonly WorkspaceService _workspaceService;
        private readonly Func<string, WorkspaceGitInfoSnapshot> _readGitInfo;
        private readonly IThreadHistoryRepairService _threadHistoryRepairService;

        public Func<ChatSession, WorkspaceInfo, Task<SessionDefaultsResolution>> ModelSettingsResolver { get; set; }

        public SessionThreadService(IAppServerThreadClient appServerClient, WorkspaceService workspaceService)
            : this(appServerClient, workspaceService, null)
        {
        }

        public SessionThreadService(
            IAppServerThreadClient appServerClient,
            WorkspaceService workspaceService,
            Func<string, WorkspaceGitInfoSnapshot> readGitInfo)
            : this(appServerClient, workspaceService, readGitInfo, null)
        {
        }

        public SessionThreadService(
            IAppServerThreadClient appServerClient,
            WorkspaceService workspaceService,
            Func<string, WorkspaceGitInfoSnapshot> readGitInfo,
            IThreadHistoryRepairService threadHistoryRepairService)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
            _readGitInfo = readGitInfo ?? _workspaceService.ReadGitInfo;
            _threadHistoryRepairService = threadHistoryRepairService;
        }

        public async Task<SessionThreadRecreationResult> RecreateThreadAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerThreadToolContext toolContext = null,
            bool deferMetadata = false,
            SessionDefaultsResolution preparedSettings = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            EnsureWorkspaceAvailable(workspace);

            var previousThreadId = session.ThreadId;
            if (preparedSettings != null || ModelSettingsResolver != null)
            {
                var settings = preparedSettings ?? await ModelSettingsResolver(session, workspace);
                modelId = settings.ModelId;
                reasoningEffort = settings.ReasoningEffort;
            }
            var newThreadId = await _appServerClient.CreateThreadAsync(
                workspace.RootPath,
                approvalMode,
                modelId,
                reasoningEffort,
                toolContext);
            if (string.IsNullOrWhiteSpace(newThreadId))
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text(
                        "SessionThreadService.Error.EmptyThreadIdReturned",
                        "App Server did not return a valid thread ID."));
            }

            session.ThreadId = newThreadId;
            if (!deferMetadata && !string.IsNullOrWhiteSpace(session.Title))
            {
                try
                {
                    await _appServerClient.SetThreadNameAsync(session.ThreadId, session.Title);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.TraceWarning("[SessionThread] Setting the recreated thread name failed. ExceptionType={0}", ex.GetType().Name);
                }
            }

            session.SessionPath = null;
            session.LastActiveAt = DateTime.Now;
            if (!deferMetadata) await TryUpdateThreadGitMetadataAsync(session.ThreadId, workspace);

            return new SessionThreadRecreationResult
            {
                PreviousThreadId = previousThreadId,
                CurrentThreadId = session.ThreadId
            };
        }

        public async Task<SessionSendResult> SendUserMessageAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerCollaborationMode collaborationMode = null,
            AppServerThreadToolContext toolContext = null,
            SessionDefaultsResolution preparedSettings = null,
            bool deferMetadata = false)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            EnsureWorkspaceAvailable(workspace);
            if (inputItems == null)
            {
                throw new ArgumentNullException(nameof(inputItems));
            }

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text(
                        "SessionThreadService.Error.ThreadRequiredBeforeSend",
                        "A valid session thread is required before sending a message."));
            }

            if (preparedSettings != null || ModelSettingsResolver != null)
            {
                var settings = preparedSettings ?? await ModelSettingsResolver(session, workspace);
                modelId = settings.ModelId;
                reasoningEffort = settings.ReasoningEffort;
                if (collaborationMode != null)
                {
                    collaborationMode = new AppServerCollaborationMode
                    {
                        Mode = collaborationMode.Mode,
                        DeveloperInstructions = collaborationMode.DeveloperInstructions,
                        ModelId = modelId,
                        ReasoningEffort = reasoningEffort
                    };
                }
            }

            try
            {
                await _appServerClient.SendUserMessageAsync(
                    session.ThreadId,
                    workspace.RootPath,
                    inputItems,
                    approvalMode,
                    modelId,
                    reasoningEffort,
                    collaborationMode);
                session.LastActiveAt = DateTime.Now;
                return new SessionSendResult();
            }
            catch (Exception ex)
            {
                if (IsMissingThreadRolloutError(ex))
                {
                    var recreation = await RecreateThreadAsync(session, workspace, approvalMode, modelId, reasoningEffort, toolContext,
                        deferMetadata, new SessionDefaultsResolution { ModelId = modelId, ReasoningEffort = reasoningEffort });
                    await _appServerClient.SendUserMessageAsync(
                        session.ThreadId,
                        workspace.RootPath,
                        inputItems,
                        approvalMode,
                        modelId,
                        reasoningEffort,
                        collaborationMode);
                    session.LastActiveAt = DateTime.Now;

                    return new SessionSendResult
                    {
                        RetryThreadRecreation = recreation
                    };
                }

                if (IsInvalidEncryptedContentError(ex))
                {
                    await TryUnsubscribeThreadAsync(session.ThreadId);
                    var repairResult = await TryRepairInvalidEncryptedContentAsync(session);
                    if (repairResult != null && repairResult.Repaired)
                    {
                        await _appServerClient.SendUserMessageAsync(
                            session.ThreadId,
                            workspace.RootPath,
                            inputItems,
                            approvalMode,
                            modelId,
                            reasoningEffort,
                            collaborationMode);
                        session.LastActiveAt = DateTime.Now;

                        return new SessionSendResult
                        {
                            InvalidEncryptedContentRepairResult = repairResult
                        };
                    }
                }

                throw;
            }
        }

        private async Task TryUnsubscribeThreadAsync(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            try
            {
                await _appServerClient.UnsubscribeThreadAsync(threadId);
            }
            catch
            {
            }
        }

        public async Task TryUpdateThreadGitMetadataAsync(ChatSession session, WorkspaceInfo workspace)
        {
            if (session == null)
            {
                return;
            }

            await TryUpdateThreadGitMetadataAsync(session.ThreadId, workspace);
        }

        public async Task TryUpdateThreadGitMetadataAsync(string threadId, WorkspaceInfo workspace)
        {
            if (string.IsNullOrWhiteSpace(threadId) ||
                workspace == null ||
                string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                return;
            }

            AppServerThreadGitInfoUpdate gitInfo;
            try
            {
                gitInfo = await Task.Run(() => BuildThreadGitInfoUpdate(workspace.RootPath)).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (gitInfo == null || !gitInfo.HasAnyUpdates)
            {
                return;
            }

            try
            {
                await _appServerClient.UpdateThreadGitMetadataAsync(threadId, gitInfo);
            }
            catch
            {
            }
        }

        public static bool IsMissingThreadRolloutError(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                var message = current.Message ?? string.Empty;
                if (message.IndexOf("no rollout found for thread id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("failed to locate rollout for thread", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        internal static bool IsInvalidEncryptedContentError(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                if (MessageLooksLikeInvalidEncryptedContentError(current.Message))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private async Task<ThreadHistoryRepairResult> TryRepairInvalidEncryptedContentAsync(ChatSession session)
        {
            if (_threadHistoryRepairService == null)
            {
                return ThreadHistoryRepairResult.NotRepaired("repair service unavailable");
            }

            try
            {
                return await _threadHistoryRepairService.TryRepairInvalidEncryptedContentAsync(session).ConfigureAwait(false);
            }
            catch
            {
                return ThreadHistoryRepairResult.NotRepaired("repair failed");
            }
        }

        private static bool MessageLooksLikeInvalidEncryptedContentError(string message)
        {
            return ThreadHistoryRejectionClassifier.IsRepairableRejection(message);
        }

        private void EnsureWorkspaceAvailable(WorkspaceInfo workspace)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text(
                        "SessionThreadService.Error.WorkspaceUnavailable",
                        "Current workspace is unavailable."));
            }
        }

        private AppServerThreadGitInfoUpdate BuildThreadGitInfoUpdate(string workspacePath)
        {
            var gitInfoSnapshot = _readGitInfo(workspacePath);
            return new AppServerThreadGitInfoUpdate
            {
                HasOriginUrl = true,
                OriginUrl = gitInfoSnapshot != null && gitInfoSnapshot.IsGitRepository ? gitInfoSnapshot.OriginUrl : null,
                HasBranch = true,
                Branch = gitInfoSnapshot != null && gitInfoSnapshot.IsGitRepository ? gitInfoSnapshot.Branch : null,
                HasSha = true,
                Sha = gitInfoSnapshot != null && gitInfoSnapshot.IsGitRepository ? gitInfoSnapshot.Sha : null
            };
        }
    }
}
