using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.GitHosting
{
    public sealed class GitHubHostingProvider : IGitHostingProvider, IGitHubHostingProviderCapability
    {
        public const string ProviderId = AppConfigSnapshot.DefaultGitHostingProviderId;

        private readonly IGitHubCliService _gitHubCliService;

        public GitHubHostingProvider()
            : this(new GitHubCliService())
        {
        }

        public GitHubHostingProvider(IGitHubCliService gitHubCliService)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
        }

        public string Id
        {
            get { return ProviderId; }
        }

        public string DisplayName
        {
            get { return "GitHub"; }
        }

        public GitHostingProviderCapabilities Capabilities
        {
            get
            {
                return GitHostingProviderCapabilities.RepositoryList |
                       GitHostingProviderCapabilities.RepositoryCreate |
                       GitHostingProviderCapabilities.RepositoryDetails |
                       GitHostingProviderCapabilities.RepositoryMetadata |
                       GitHostingProviderCapabilities.RepositoryVisibility |
                       GitHostingProviderCapabilities.RepositoryArchive |
                       GitHostingProviderCapabilities.RepositoryDelete |
                       GitHostingProviderCapabilities.Branches |
                       GitHostingProviderCapabilities.Tags |
                       GitHostingProviderCapabilities.Releases |
                       GitHostingProviderCapabilities.PullRequests |
                       GitHostingProviderCapabilities.Issues |
                       GitHostingProviderCapabilities.Contributors |
                       GitHostingProviderCapabilities.DefaultBranch |
                       GitHostingProviderCapabilities.Clone;
            }
        }

        public IGitHubCliService GitHubCliService
        {
            get { return _gitHubCliService; }
        }

        public bool Supports(GitHostingProviderCapabilities capability)
        {
            return capability == GitHostingProviderCapabilities.None ||
                   (Capabilities & capability) == capability;
        }

        public async Task<IReadOnlyList<RemoteRepositoryEntry>> ListRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            var repositories = await _gitHubCliService.ListRepositoriesAsync(keyword, limit, cancellationToken)
                .ConfigureAwait(false);
            return (repositories ?? new GitHubRepositoryEntry[0])
                .Where(repository => repository != null)
                .Select(repository => RemoteRepositoryEntryMapper.FromGitHub(repository, Id, DisplayName))
                .ToList();
        }

        public TCapability GetCapability<TCapability>() where TCapability : class
        {
            return this as TCapability;
        }
    }
}
