using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.GitHosting;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitHostingProviderContractTests
    {
        [Test]
        public void GitHubHostingProvider_ExposesGenericCapabilitiesAndGitHubCapability()
        {
            var provider = new GitHubHostingProvider();

            Assert.That(provider.Supports(GitHostingProviderCapabilities.RepositoryList), Is.True);
            Assert.That(provider.Supports(GitHostingProviderCapabilities.PullRequests), Is.True);
            Assert.That(provider.Supports(GitHostingProviderCapabilities.Issues), Is.True);
            Assert.That(provider.GetCapability<IGitHubHostingProviderCapability>(), Is.Not.Null);
        }

        [Test]
        public async Task GitHubHostingProvider_ListRepositories_ReturnsGenericRepositoryEntries()
        {
            var source = new GitHubRepositoryEntry
            {
                NameWithOwner = "owner/repo",
                Description = "description",
                Visibility = "PRIVATE",
                IsPrivate = true,
                DefaultBranch = "main",
                HttpsUrl = "https://github.com/owner/repo.git",
                SshUrl = "git@github.com:owner/repo.git",
                PrimaryLanguage = "C#",
                Stars = 5,
                UpdatedAt = DateTimeOffset.Parse("2026-05-01T10:00:00+08:00")
            };
            var provider = new GitHubHostingProvider(new FakeGitHubCliService(new[] { source }));

            var repositories = await provider.ListRepositoriesAsync("repo", 20, CancellationToken.None);

            Assert.That(repositories, Has.Count.EqualTo(1));
            Assert.That(repositories[0].ProviderId, Is.EqualTo(GitHubHostingProvider.ProviderId));
            Assert.That(repositories[0].ProviderDisplayName, Is.EqualTo("GitHub"));
            Assert.That(repositories[0].NameWithOwner, Is.EqualTo(source.NameWithOwner));
            Assert.That(repositories[0].ProviderData, Is.SameAs(source));
        }

        [Test]
        public void GitHostingProviderService_KeepsCustomProviderRegistrable()
        {
            var customProvider = new FakeGitHostingProvider("custom", "Custom");
            var service = new GitHostingProviderService(new IGitHostingProvider[] { customProvider });

            var providers = service.GetProviders();

            Assert.That(providers, Does.Contain(customProvider));
            Assert.That(service.ResolveProvider("custom"), Is.SameAs(customProvider));
        }

        private sealed class FakeGitHostingProvider : IGitHostingProvider
        {
            public FakeGitHostingProvider(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public GitHostingProviderCapabilities Capabilities
            {
                get { return GitHostingProviderCapabilities.RepositoryList; }
            }

            public bool Supports(GitHostingProviderCapabilities capability)
            {
                return capability == GitHostingProviderCapabilities.None ||
                       (Capabilities & capability) == capability;
            }

            public Task<IReadOnlyList<RemoteRepositoryEntry>> ListRepositoriesAsync(
                string keyword,
                int limit,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RemoteRepositoryEntry>>(new[]
                {
                    new RemoteRepositoryEntry
                    {
                        ProviderId = Id,
                        ProviderDisplayName = DisplayName,
                        NameWithOwner = "custom/repo"
                    }
                });
            }

            public TCapability GetCapability<TCapability>() where TCapability : class
            {
                return this as TCapability;
            }
        }

        private sealed class FakeGitHubCliService : IGitHubCliService
        {
            private readonly IReadOnlyList<GitHubRepositoryEntry> _repositories;

            public FakeGitHubCliService(IReadOnlyList<GitHubRepositoryEntry> repositories)
            {
                _repositories = repositories;
            }

            public Task<GitHubCliAccountSnapshot> ReadAccountAsync(CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubRepositoryEntry>> ListRepositoriesAsync(string keyword, int limit, CancellationToken cancellationToken)
            {
                return Task.FromResult(_repositories);
            }

            public Task<IReadOnlyList<GitHubRepositoryEntry>> ListEmptyRepositoriesAsync(string keyword, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubRepositoryEntry> GetRepositoryDetailsAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubRepositorySearchResult> SearchRepositoriesAsync(
                GitHubRepositorySearchRequest request,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubBranchEntry>> ListBranchesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task DeleteRepositoryBranchAsync(GitHubRepositoryEntry repository, string branchName, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubTagEntry>> ListTagsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubReleaseEntry>> ListReleasesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubReleaseDetailEntry> GetReleaseDetailsAsync(GitHubRepositoryEntry repository, string tagName, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(GitHubRepositoryEntry repository, int limit, string state, string scope, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(GitHubRepositoryEntry repository, int limit, string state, string scope, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyList<GitHubContributorEntry>> ListContributorsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubDiscussionEntry> GetIssueDiscussionAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubDiscussionEntry> GetPullRequestDiscussionAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task AddPullRequestCommentAsync(GitHubRepositoryEntry repository, int number, string body, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task AddIssueCommentAsync(GitHubRepositoryEntry repository, int number, string body, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task CloseIssueAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ReopenIssueAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<string> CreateIssueAsync(GitHubRepositoryEntry repository, string title, string body, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ClosePullRequestAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ReopenPullRequestAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<string> CreatePullRequestAsync(GitHubRepositoryEntry repository, string baseBranch, string headBranch, string title, string body, bool draft, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubCreateRepositoryResult> CreateRepositoryAsync(GitHubCreateRepositoryRequest request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ArchiveRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task UnarchiveRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task DeleteRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task RenameRepositoryAsync(GitHubRepositoryEntry repository, string newName, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task EditRepositoryMetadataAsync(GitHubRepositoryEntry repository, string description, string homepageUrl, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task SetDefaultBranchAsync(GitHubRepositoryEntry repository, string branchName, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task ChangeRepositoryVisibilityAsync(GitHubRepositoryEntry repository, string visibility, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitCloneResult> CloneRepositoryAsync(
                GitHubRepositoryEntry repository,
                string targetParentDirectory,
                string targetFolderName,
                bool recurseSubmodules,
                bool useSshUrl,
                IProgress<GitCloneProgress> progress,
                CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<GitHubRepositoryEntry> ForkRepositoryAsync(
                GitHubRepositoryEntry repository,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(repository);
            }

            public void StartInteractiveLogin()
            {
                throw new NotImplementedException();
            }
        }
    }
}
