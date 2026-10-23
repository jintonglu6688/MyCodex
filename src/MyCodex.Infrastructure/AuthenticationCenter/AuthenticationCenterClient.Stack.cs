using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    // Aggregation (the core's Stack mode): several providers' models in one Codex model list.
    public sealed partial class AuthenticationCenterClient
    {
        public async Task<AuthenticationStackState> ReadStackAsync(CancellationToken cancellationToken)
        {
            return ReadStackState(await _request("stack/get", Empty(), cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationStackState> EnterStackAsync(string defaultProviderId, IReadOnlyList<string> memberIds,
            CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object>
            {
                { "defaultProviderId", defaultProviderId },
                { "memberIds", (memberIds ?? new List<string>()).ToArray() }
            };
            var state = ReadStackState(await _request("stack/enter", parameters, cancellationToken).ConfigureAwait(false));
            if (!state.Active || state.DefaultProviderId != defaultProviderId) throw new AuthenticationCenterException("operation_unknown");
            return state;
        }

        public async Task<AuthenticationStackState> ExitStackAsync(CancellationToken cancellationToken)
        {
            var state = ReadStackState(await _request("stack/exit", Empty(), cancellationToken).ConfigureAwait(false));
            if (state.Active) throw new AuthenticationCenterException("operation_unknown");
            return state;
        }

        public async Task<AuthenticationStackState> SetStackMemberAsync(string providerId, bool enabled, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "providerId", providerId }, { "enabled", enabled } };
            var state = ReadStackState(await _request("stack/member/set", parameters, cancellationToken).ConfigureAwait(false));
            if (state.Members.Any(member => member.ProviderId == providerId) != enabled) throw new AuthenticationCenterException("operation_unknown");
            return state;
        }

        public async Task<AuthenticationStackState> AdoptStackCatalogAsync(CancellationToken cancellationToken)
        {
            return ReadStackState(await _request("stack/catalog/adopt", Empty(), cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationStackState> SetStackClassicSubagentsAsync(bool enabled, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "classicSubagents", enabled } };
            var state = ReadStackState(await _request("stack/settings/set", parameters, cancellationToken).ConfigureAwait(false));
            if (state.ClassicSubagents != enabled) throw new AuthenticationCenterException("operation_unknown");
            return state;
        }

        private static AuthenticationStackState ReadStackState(IDictionary<string, object> value)
        {
            if (!value.TryGetValue("active", out var active) || !(active is bool))
                throw new AuthenticationCenterException("invalid_response");
            var members = Objects(value, "members").Select(member => new AuthenticationStackMember
            {
                ProviderId = Text(member, "providerId") ?? throw new AuthenticationCenterException("invalid_response"),
                ModelIds = Strings(member, "modelIds") ?? new List<string>(),
                IsDefault = Flag(member, "isDefault")
            }).ToList();
            var providers = Objects(value, "providers").Select(provider => new AuthenticationStackEligibility
            {
                ProviderId = Text(provider, "providerId") ?? throw new AuthenticationCenterException("invalid_response"),
                JoinRefusal = Text(provider, "joinRefusal"),
                DefaultRefusal = Text(provider, "defaultRefusal")
            }).ToList();
            return new AuthenticationStackState
            {
                Active = (bool)active,
                DefaultProviderId = Text(value, "defaultProviderId"),
                Members = members,
                Providers = providers,
                Notice = Text(value, "notice"),
                ClassicSubagents = Flag(value, "classicSubagents"),
                UnifiedSessionHistory = Flag(value, "unifiedSessionHistory"),
                GlobalApplied = Flag(value, "globalApplied")
            };
        }
    }
}
