using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteService
    {
        Task<IReadOnlyList<CodingRemoteWorkspaceSummary>> ListWorkspacesAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<CodingRemoteSessionSummary>> ListSessionsAsync(string workspaceId, CancellationToken cancellationToken);

        Task<CodingRemoteSessionSummary> CreateSessionAsync(CodingRemoteSessionCreateRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteMessagePage> ListMessagesAsync(CodingRemoteMessagesListRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteMessageContentPage> ReadMessageContentAsync(CodingRemoteMessageContentRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteMessageSendResult> SendMessageAsync(CodingRemoteMessageSendRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteAttachmentCatalogResult> ListPluginAttachmentsAsync(string sessionId, CancellationToken cancellationToken);

        Task<CodingRemoteAttachmentCatalogResult> ListSkillAttachmentsAsync(string sessionId, bool forceReload, CancellationToken cancellationToken);

        Task<CodingRemoteSessionStopResult> StopSessionAsync(CodingRemoteSessionStopRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteWorkspaceOperationResult> OperateWorkspaceAsync(CodingRemoteWorkspaceOperationRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteSessionOperationResult> OperateSessionAsync(CodingRemoteSessionOperationRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteSessionThreadOperationResult> OperateSessionThreadAsync(
            CodingRemoteSessionThreadOperationRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteSessionSettings> GetSessionSettingsAsync(string sessionId, CancellationToken cancellationToken);

        Task<CodingRemoteSessionSettingsUpdateResult> UpdateSessionSettingsAsync(CodingRemoteSessionSettingsUpdateRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteApprovalResponseResult> RespondApprovalAsync(
            CodingRemoteApprovalResponseRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteToolInputResponseResult> RespondToolInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteToolInputResponseResult> CancelToolInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken);
    }
}
