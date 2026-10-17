using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitHubCliService
    {
        Task<GitHubCliAccountSnapshot> ReadAccountAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubRepositoryEntry>> ListRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken);

        Task<GitHubRepositorySearchResult> SearchRepositoriesAsync(
            GitHubRepositorySearchRequest request,
            CancellationToken cancellationToken);

        Task<GitHubRepositoryEntry> ForkRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubRepositoryEntry>> ListEmptyRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken);

        Task<GitHubRepositoryEntry> GetRepositoryDetailsAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubBranchEntry>> ListBranchesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task DeleteRepositoryBranchAsync(
            GitHubRepositoryEntry repository,
            string branchName,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubTagEntry>> ListTagsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubReleaseEntry>> ListReleasesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task<GitHubReleaseDetailEntry> GetReleaseDetailsAsync(
            GitHubRepositoryEntry repository,
            string tagName,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            string state,
            string scope,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            string state,
            string scope,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<GitHubContributorEntry>> ListContributorsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken);

        Task<GitHubDiscussionEntry> GetIssueDiscussionAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task<GitHubDiscussionEntry> GetPullRequestDiscussionAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task AddPullRequestCommentAsync(
            GitHubRepositoryEntry repository,
            int number,
            string body,
            CancellationToken cancellationToken);

        Task AddIssueCommentAsync(
            GitHubRepositoryEntry repository,
            int number,
            string body,
            CancellationToken cancellationToken);

        Task CloseIssueAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task ReopenIssueAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task<string> CreateIssueAsync(
            GitHubRepositoryEntry repository,
            string title,
            string body,
            CancellationToken cancellationToken);

        Task ClosePullRequestAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task ReopenPullRequestAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken);

        Task<string> CreatePullRequestAsync(
            GitHubRepositoryEntry repository,
            string baseBranch,
            string headBranch,
            string title,
            string body,
            bool draft,
            CancellationToken cancellationToken);

        Task<GitHubCreateRepositoryResult> CreateRepositoryAsync(
            GitHubCreateRepositoryRequest request,
            CancellationToken cancellationToken);

        Task ArchiveRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken);

        Task UnarchiveRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken);

        Task DeleteRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken);

        Task RenameRepositoryAsync(
            GitHubRepositoryEntry repository,
            string newName,
            CancellationToken cancellationToken);

        Task EditRepositoryMetadataAsync(
            GitHubRepositoryEntry repository,
            string description,
            string homepageUrl,
            CancellationToken cancellationToken);

        Task SetDefaultBranchAsync(
            GitHubRepositoryEntry repository,
            string branchName,
            CancellationToken cancellationToken);

        Task ChangeRepositoryVisibilityAsync(
            GitHubRepositoryEntry repository,
            string visibility,
            CancellationToken cancellationToken);

        Task<GitCloneResult> CloneRepositoryAsync(
            GitHubRepositoryEntry repository,
            string targetParentDirectory,
            string targetFolderName,
            bool recurseSubmodules,
            bool useSshUrl,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken);

        void StartInteractiveLogin();
    }
}
