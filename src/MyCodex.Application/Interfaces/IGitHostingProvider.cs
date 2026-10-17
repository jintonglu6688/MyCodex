using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    [System.Flags]
    public enum GitHostingProviderCapabilities
    {
        None = 0,
        RepositoryList = 1,
        RepositoryCreate = 1 << 1,
        RepositoryDetails = 1 << 2,
        RepositoryMetadata = 1 << 3,
        RepositoryVisibility = 1 << 4,
        RepositoryArchive = 1 << 5,
        RepositoryDelete = 1 << 6,
        Branches = 1 << 7,
        Tags = 1 << 8,
        Releases = 1 << 9,
        PullRequests = 1 << 10,
        Issues = 1 << 11,
        Contributors = 1 << 12,
        DefaultBranch = 1 << 13,
        Clone = 1 << 14
    }

    public interface IGitHostingProvider
    {
        string Id { get; }

        string DisplayName { get; }

        GitHostingProviderCapabilities Capabilities { get; }

        bool Supports(GitHostingProviderCapabilities capability);

        Task<IReadOnlyList<RemoteRepositoryEntry>> ListRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken);

        TCapability GetCapability<TCapability>() where TCapability : class;
    }

    public interface IGitHubHostingProviderCapability
    {
        IGitHubCliService GitHubCliService { get; }
    }

    public interface IGitHostingProviderService
    {
        string DefaultProviderId { get; }

        IGitHostingProvider DefaultProvider { get; }

        IGitHostingProvider ResolveProvider(string providerId);

        IReadOnlyList<IGitHostingProvider> GetProviders();
    }
}
