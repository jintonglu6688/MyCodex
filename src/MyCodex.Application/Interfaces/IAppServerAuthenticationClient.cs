using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerAuthenticationClient
    {
        Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken);

        Task<AppServerLoginResult> StartChatGptLoginAsync();

        Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync();

        Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey);

        Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId);

        Task LogoutAsync();

        Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync();

        Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            string creditId,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
