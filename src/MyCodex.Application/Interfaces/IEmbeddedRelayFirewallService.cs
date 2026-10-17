using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.Interfaces
{
    public enum EmbeddedRelayFirewallState
    {
        Unknown,
        Allowed,
        RuleMissing,
        NonPrivateNetwork,
        Unavailable
    }

    public sealed class EmbeddedRelayFirewallStatus
    {
        public EmbeddedRelayFirewallState State { get; set; }

        public string ExecutablePath { get; set; }

        public string ErrorMessage { get; set; }
    }

    public sealed class EmbeddedRelayFirewallAllowResult
    {
        public bool Success { get; set; }

        public bool Canceled { get; set; }

        public EmbeddedRelayFirewallStatus Status { get; set; }

        public string ErrorMessage { get; set; }
    }

    public interface IEmbeddedRelayFirewallService
    {
        EmbeddedRelayFirewallStatus GetStatus();

        Task<EmbeddedRelayFirewallAllowResult> AllowPrivateLocalSubnetAsync(
            CancellationToken cancellationToken);
    }
}
