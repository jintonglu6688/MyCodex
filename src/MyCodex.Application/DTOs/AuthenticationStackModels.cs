using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    // A provider whose models are published to Codex while aggregation is on.
    public sealed class AuthenticationStackMember
    {
        public string ProviderId { get; set; }
        // Reserved "ccs-<key>/<model>" ids; empty for the default, whose models stay plain.
        public IReadOnlyList<string> ModelIds { get; set; } = new List<string>();
        public bool IsDefault { get; set; }
    }

    // Why a saved provider cannot join the list or be the default; null when it can.
    public sealed class AuthenticationStackEligibility
    {
        public string ProviderId { get; set; }
        public string JoinRefusal { get; set; }
        public string DefaultRefusal { get; set; }
    }

    public sealed class AuthenticationStackState
    {
        public bool Active { get; set; }
        public string DefaultProviderId { get; set; }
        // Kept while aggregation is off, so turning it on again restores the list.
        public IReadOnlyList<AuthenticationStackMember> Members { get; set; } = new List<AuthenticationStackMember>();
        public IReadOnlyList<AuthenticationStackEligibility> Providers { get; set; } = new List<AuthenticationStackEligibility>();
        // routeOwnsCatalog, officialModelsBundled, officialModelsUnavailable or officialModelsOutdated.
        public string Notice { get; set; }
        public bool ClassicSubagents { get; set; }
        public bool UnifiedSessionHistory { get; set; }
        // The client files changed; Codex reads its model catalog only when it starts.
        public bool GlobalApplied { get; set; }
    }

    public sealed class AuthenticationStackResult
    {
        public AuthenticationStackState State { get; set; }
        public bool Reconnected { get; set; }
    }
}
