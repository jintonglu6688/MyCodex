using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public enum AuthenticationStackRole
    {
        None,
        Default,
        Member,
        Unavailable
    }

    // What the aggregation controls may offer for a provider, decided from the backend's view only.
    public static class AuthenticationStackPlanner
    {
        public static AuthenticationStackRole RoleOf(AuthenticationStackState state, string providerId)
        {
            if (state == null || string.IsNullOrEmpty(providerId)) return AuthenticationStackRole.None;
            var member = Member(state, providerId);
            if (member != null) return state.Active && member.IsDefault ? AuthenticationStackRole.Default : AuthenticationStackRole.Member;
            var eligibility = Eligibility(state, providerId);
            if (eligibility != null && eligibility.JoinRefusal != null && eligibility.DefaultRefusal != null)
                return AuthenticationStackRole.Unavailable;
            return state.Active && state.DefaultProviderId == providerId ? AuthenticationStackRole.Default : AuthenticationStackRole.None;
        }

        public static bool CanJoin(AuthenticationStackState state, string providerId)
        {
            if (state == null || Member(state, providerId) != null) return false;
            var eligibility = Eligibility(state, providerId);
            return eligibility != null && eligibility.JoinRefusal == null;
        }

        // The default stays listed while aggregation is on; another default must be chosen first.
        public static bool CanLeave(AuthenticationStackState state, string providerId)
        {
            var member = state == null ? null : Member(state, providerId);
            return member != null && !(state.Active && member.IsDefault);
        }

        public static bool CanBeDefault(AuthenticationStackState state, string providerId)
        {
            var eligibility = state == null ? null : Eligibility(state, providerId);
            return eligibility != null && eligibility.DefaultRefusal == null;
        }

        public static string DefaultRefusal(AuthenticationStackState state, string providerId)
        {
            return state == null ? null : Eligibility(state, providerId)?.DefaultRefusal;
        }

        public static IReadOnlyList<string> DefaultCandidates(AuthenticationStackState state)
        {
            return state == null ? new List<string>()
                : state.Providers.Where(provider => provider.DefaultRefusal == null).Select(provider => provider.ProviderId).ToList();
        }

        public static IReadOnlyList<string> MemberCandidates(AuthenticationStackState state, string defaultProviderId)
        {
            return state == null ? new List<string>()
                : state.Providers.Where(provider => provider.JoinRefusal == null &&
                    !string.Equals(provider.ProviderId, defaultProviderId, StringComparison.Ordinal))
                    .Select(provider => provider.ProviderId).ToList();
        }

        // The members that publish models besides the default, and how many models they add.
        public static int PublishingProviders(AuthenticationStackState state)
        {
            return state == null ? 0 : state.Members.Count(member => !member.IsDefault && member.ModelIds.Count > 0);
        }

        public static int PublishedModels(AuthenticationStackState state)
        {
            return state == null ? 0 : state.Members.Where(member => !member.IsDefault).Sum(member => member.ModelIds.Count);
        }

        public static bool IsMember(AuthenticationStackState state, string providerId)
        {
            return state != null && Member(state, providerId) != null;
        }

        private static AuthenticationStackMember Member(AuthenticationStackState state, string providerId)
        {
            return state.Members.FirstOrDefault(member => string.Equals(member.ProviderId, providerId, StringComparison.Ordinal));
        }

        private static AuthenticationStackEligibility Eligibility(AuthenticationStackState state, string providerId)
        {
            return state.Providers.FirstOrDefault(provider => string.Equals(provider.ProviderId, providerId, StringComparison.Ordinal));
        }
    }
}
