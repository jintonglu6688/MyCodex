using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient : ISessionProviderMigrationClient
    {
        public async Task RecoverSessionProvidersAsync(CancellationToken cancellationToken)
        {
            IDictionary<string, object> status;
            try { status = await _request("status", Empty(), cancellationToken).ConfigureAwait(false); }
            catch (AuthenticationCenterException error) when (error.Code == "backend_unavailable") { return; }
            if (Strings(status, "capabilities")?.Contains("sessionProviderMigration") != true) return;
            try { await _request("sessionHistory/providerRecovery", Empty(), cancellationToken).ConfigureAwait(false); }
            catch (System.Exception) { throw new AuthenticationCenterException("provider_recovery_required"); }
        }

        public async Task<SessionProviderMigrationResult> PrepareSessionProviderAsync(string threadId,
            string modelProvider, bool apply, CancellationToken cancellationToken)
        {
            var status = await _request("status", Empty(), cancellationToken).ConfigureAwait(false);
            if (Strings(status, "capabilities")?.Contains("sessionProviderMigration") != true)
                return new SessionProviderMigrationResult { Supported = false };
            var result = await _request("sessionHistory/provider", new Dictionary<string, object>
            {
                { "threadId", threadId }, { "modelProvider", modelProvider }, { "apply", apply }
            }, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("needsMigration", out var needs) || !(needs is bool) ||
                !result.TryGetValue("changed", out var changed) || !(changed is bool))
                throw new AuthenticationCenterException("invalid_response");
            return new SessionProviderMigrationResult { NeedsMigration = (bool)needs, Changed = (bool)changed };
        }
    }
}
