using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAuthenticationCenterClient
    {
        Task<AuthenticationConfigDocument> PreviewProviderConfigAsync(AuthenticationProviderEdit edit, string editedText, CancellationToken cancellationToken);
        Task<AuthenticationConfigDocument> ViewProviderConfigAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken);
        Task<AuthenticationMcpSnapshot> ReadMcpAsync(CancellationToken cancellationToken);
        Task SaveMcpAsync(string serverId, IDictionary<string, object> config, bool enabled, string expectedVersion, CancellationToken cancellationToken);
        Task DeleteMcpAsync(string serverId, string expectedVersion, CancellationToken cancellationToken);
        Task<int> ImportMcpAsync(string expectedVersion, CancellationToken cancellationToken);
        Task<AuthenticationProvider> ReadForEditAsync(AuthenticationProvider provider, CancellationToken cancellationToken);
        Task<IReadOnlyList<AuthenticationProviderPreset>> ReadPresetsAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<AuthenticationCatalogModel>> FetchModelsAsync(AuthenticationModelFetchRequest request, CancellationToken cancellationToken);
        Task<AuthenticationCenterSnapshot> ReadAsync(CancellationToken cancellationToken);
        Task<AuthenticationProvider> SaveAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken);
        Task<AuthenticationProvider> CopyAsync(AuthenticationProvider provider, string name, CancellationToken cancellationToken);
        Task<bool> DeleteAsync(AuthenticationProvider provider, CancellationToken cancellationToken);
        Task<AuthenticationApplyPlan> PreflightAsync(AuthenticationProvider provider, CancellationToken cancellationToken);
        Task<AuthenticationOperation> ApplyAsync(AuthenticationApplyPlan plan, string operationId, CancellationToken cancellationToken);
        Task<AuthenticationOperation> GetOperationAsync(string operationId, CancellationToken cancellationToken);
        Task<AuthenticationAccountsSnapshot> ReadAccountsAsync(CancellationToken cancellationToken);
        Task<AuthenticationDeviceLogin> StartBrowserLoginAsync(string targetAccountId, string loginId, CancellationToken cancellationToken);
        Task<AuthenticationDeviceLogin> StartDeviceLoginAsync(string targetAccountId, string loginId, CancellationToken cancellationToken);
        Task<AuthenticationDeviceLogin> ReadDeviceLoginAsync(string loginId, CancellationToken cancellationToken);
        Task<AuthenticationDeviceLogin> CancelDeviceLoginAsync(string loginId, CancellationToken cancellationToken);
        Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken);
        Task<AuthenticationSessionHistoryState> ReadSessionHistoryAsync(CancellationToken cancellationToken);
        Task<AuthenticationSessionHistoryPreview> PreviewSessionHistoryAsync(CancellationToken cancellationToken);
        Task<AuthenticationSessionHistoryResult> SetSessionHistoryAsync(bool unified, CancellationToken cancellationToken);
        Task<AuthenticationSessionRepairResult> RepairSessionHistoryAsync(string threadId, AuthenticationSessionRepairMode mode,
            CancellationToken cancellationToken);
        Task<AuthenticationStackState> ReadStackAsync(CancellationToken cancellationToken);
        Task<AuthenticationStackState> EnterStackAsync(string defaultProviderId, IReadOnlyList<string> memberIds, CancellationToken cancellationToken);
        Task<AuthenticationStackState> ExitStackAsync(CancellationToken cancellationToken);
        Task<AuthenticationStackState> SetStackMemberAsync(string providerId, bool enabled, CancellationToken cancellationToken);
        Task<AuthenticationStackState> AdoptStackCatalogAsync(CancellationToken cancellationToken);
        Task<AuthenticationStackState> SetStackClassicSubagentsAsync(bool enabled, CancellationToken cancellationToken);
    }

    public sealed class AuthenticationCenterException : Exception
    {
        public AuthenticationCenterException(string code) : base(code)
        {
            Code = code;
        }

        public AuthenticationCenterException(string code, IReadOnlyList<string> keys) : this(code)
        {
            Keys = keys ?? new List<string>();
        }

        public string Code { get; private set; }

        // Settings named by an editor_conflict; empty for other failures.
        public IReadOnlyList<string> Keys { get; private set; } = new List<string>();
    }
}
