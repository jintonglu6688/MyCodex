using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.GitHosting
{
    public sealed class GitHostingProviderService : IGitHostingProviderService
    {
        private readonly Dictionary<string, IGitHostingProvider> _providers;

        public GitHostingProviderService()
            : this(new IGitHostingProvider[] { new GitHubHostingProvider() })
        {
        }

        public GitHostingProviderService(IEnumerable<IGitHostingProvider> providers)
        {
            if (providers == null)
            {
                providers = new IGitHostingProvider[0];
            }

            _providers = new Dictionary<string, IGitHostingProvider>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in providers)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                {
                    continue;
                }

                _providers[provider.Id.Trim()] = provider;
            }

            if (!_providers.ContainsKey(DefaultProviderId))
            {
                _providers[DefaultProviderId] = new GitHubHostingProvider();
            }
        }

        public string DefaultProviderId
        {
            get { return AppConfigSnapshot.DefaultGitHostingProviderId; }
        }

        public IGitHostingProvider DefaultProvider
        {
            get { return ResolveProvider(DefaultProviderId); }
        }

        public IGitHostingProvider ResolveProvider(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return DefaultProvider;
            }

            if (_providers.TryGetValue(providerId.Trim(), out var provider) && provider != null)
            {
                return provider;
            }

            return DefaultProvider;
        }

        public IReadOnlyList<IGitHostingProvider> GetProviders()
        {
            return _providers.Values
                .OrderBy(p => p == null ? string.Empty : p.Id)
                .ToList();
        }
    }
}
