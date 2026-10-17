using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ReviewSessionResolution
    {
        public ReviewSessionResolution(ChatSession session, bool isNewSession)
        {
            Session = session;
            IsNewSession = isNewSession;
        }

        public ChatSession Session { get; }

        public bool IsNewSession { get; }
    }

    internal sealed class SessionThreadOperationWorkflow
    {
        private readonly IAppServerThreadClient _appServerClient;
        private readonly SessionThreadService _sessionThreadService;
        private readonly SessionService _sessionService;

        internal SessionThreadOperationWorkflow(
            IAppServerThreadClient appServerClient,
            SessionThreadService sessionThreadService,
            SessionService sessionService)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _sessionThreadService = sessionThreadService ?? throw new ArgumentNullException(nameof(sessionThreadService));
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        }

        internal async Task<AppServerReviewStartResult> StartInlineReviewAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort = null,
            string reviewModelId = null,
            AppServerReviewTarget target = null)
        {
            if (session == null || workspace == null)
            {
                return null;
            }

            return await _appServerClient.StartReviewAsync(
                session.ThreadId,
                workspace.RootPath,
                approvalMode,
                modelId,
                target ?? AppServerReviewTarget.CreateUncommittedChanges(),
                false, reasoningEffort, reviewModelId);
        }

        internal async Task<AppServerReviewStartResult> StartDetachedReviewAsync(
            ChatSession sourceSession,
            WorkspaceInfo workspace,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort = null,
            string reviewModelId = null,
            AppServerReviewTarget target = null)
        {
            if (sourceSession == null || workspace == null)
            {
                return null;
            }

            return await _appServerClient.StartReviewAsync(
                sourceSession.ThreadId,
                workspace.RootPath,
                approvalMode,
                modelId,
                target ?? AppServerReviewTarget.CreateUncommittedChanges(),
                true, reasoningEffort, reviewModelId);
        }

        internal async Task<ReviewSessionResolution> ResolveReviewSessionAsync(
            ChatSession sourceSession,
            WorkspaceInfo workspace,
            AppServerReviewStartResult reviewStartResult,
            Func<string, ChatSession> findSessionByThreadId,
            ApprovalMode? sessionApprovalMode,
            string sessionModelId,
            string sessionReasoningEffort)
        {
            if (sourceSession == null ||
                workspace == null ||
                reviewStartResult == null ||
                string.IsNullOrWhiteSpace(reviewStartResult.ReviewThreadId))
            {
                return new ReviewSessionResolution(null, false);
            }

            var existingSession = findSessionByThreadId != null
                ? findSessionByThreadId(reviewStartResult.ReviewThreadId)
                : null;
            if (existingSession != null)
            {
                ApplyReviewSource(existingSession, sourceSession, workspace);
                return new ReviewSessionResolution(existingSession, false);
            }

            var reviewTitle = SessionDisplayFormatter.BuildReviewSessionTitle(sourceSession);
            var reviewSummary = reviewStartResult.CreatedThread;
            if (reviewSummary == null)
            {
                var details = await _appServerClient.ReadThreadAsync(reviewStartResult.ReviewThreadId);
                existingSession = findSessionByThreadId != null ? findSessionByThreadId(reviewStartResult.ReviewThreadId) : null;
                if (existingSession != null)
                {
                    ApplyReviewSource(existingSession, sourceSession, workspace);
                    return new ReviewSessionResolution(existingSession, false);
                }
                if (details == null || !string.Equals(details.ThreadId, reviewStartResult.ReviewThreadId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.CreateReviewSessionFailed", "Failed to create review session: new review thread was not loaded."));
                }
                reviewSummary = new AppServerThreadSummary
                {
                    ThreadId = details.ThreadId,
                    Title = string.IsNullOrWhiteSpace(details.Title) ? reviewTitle : details.Title,
                    Preview = details.Preview,
                    WorkingDirectory = string.IsNullOrWhiteSpace(details.WorkingDirectory) ? workspace.RootPath : details.WorkingDirectory,
                    Source = details.Source,
                    CreatedAt = details.CreatedAt,
                    UpdatedAt = details.UpdatedAt,
                    SessionPath = details.SessionPath
                };
            }

            var reviewSession = _sessionService.CreateReviewSession(
                workspace,
                sourceSession,
                reviewSummary,
                reviewTitle,
                sourceSession.ApprovalMode,
                sourceSession.ModelId,
                sourceSession.ReasoningEffort);

            return new ReviewSessionResolution(reviewSession, true);
        }

        private static void ApplyReviewSource(ChatSession session, ChatSession source, WorkspaceInfo workspace)
        {
            session.IsReview = true;
            session.Title = SessionDisplayFormatter.BuildReviewSessionTitle(source);
            session.WorkspaceId = workspace.Id;
            session.ReviewSourceThreadId = source.ThreadId;
            session.ReviewSourceTitle = source.Title;
            session.ApprovalMode = source.ApprovalMode;
            session.ModelId = source.ModelId;
            session.ReasoningEffort = source.ReasoningEffort;
        }

        internal async Task SubmitCompactionAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            ApprovalMode? approvalMode,
            string modelId)
        {
            await _appServerClient.CompactThreadAsync(session.ThreadId, workspace.RootPath, approvalMode, modelId);
        }

        internal async Task<ChatSession> CreateForkSessionAsync(
            ChatSession sourceSession,
            WorkspaceInfo workspace,
            string title,
            int forkIndex,
            string lastTurnId,
            ApprovalMode? operationApprovalMode,
            string operationModelId,
            string operationReasoningEffort,
            Action<string> reportWarning = null)
        {
            var service = new SessionForkService(_appServerClient);
            var summary = await service.ForkAsync(sourceSession, workspace, lastTurnId,
                operationApprovalMode, operationModelId, operationReasoningEffort);
            var session = _sessionService.CreateForkSession(workspace, sourceSession, summary, title,
                forkIndex, sourceSession.ApprovalMode, sourceSession.ModelId, sourceSession.ReasoningEffort);
            var warning = await service.TrySetNameAsync(summary.ThreadId, title);
            if (warning != null && reportWarning != null)
            {
                reportWarning(warning);
            }
            await _sessionThreadService.TryUpdateThreadGitMetadataAsync(summary.ThreadId, workspace);
            return session;
        }
    }
}
