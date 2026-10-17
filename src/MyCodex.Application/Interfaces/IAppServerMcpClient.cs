using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerMcpClient
    {
        Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync();

        Task ReloadMcpServerConfigAsync();

        Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(
            string serverName,
            int? timeoutSecs = null);

        Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(
            string server,
            string uri,
            string threadId = null,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerMcpToolCallResult> CallMcpToolAsync(
            AppServerMcpToolCallRequest request,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
