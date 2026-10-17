namespace MyCodex.Application.DTOs
{
    public static class RemoteRepositoryEntryMapper
    {
        public static RemoteRepositoryEntry FromGitHub(
            GitHubRepositoryEntry repository,
            string providerId,
            string providerDisplayName)
        {
            if (repository == null)
            {
                return null;
            }

            return new RemoteRepositoryEntry
            {
                ProviderId = providerId,
                ProviderDisplayName = providerDisplayName,
                NameWithOwner = repository.NameWithOwner,
                Description = repository.Description,
                Visibility = repository.Visibility,
                IsPrivate = repository.IsPrivate,
                DefaultBranch = repository.DefaultBranch,
                UpdatedAt = repository.UpdatedAt,
                CreatedAt = repository.CreatedAt,
                PushedAt = repository.PushedAt,
                DiskUsage = repository.DiskUsage,
                Stars = repository.Stars,
                Forks = repository.Forks,
                Watchers = repository.Watchers,
                HomepageUrl = repository.HomepageUrl,
                License = repository.License,
                PrimaryLanguage = repository.PrimaryLanguage,
                IsArchived = repository.IsArchived,
                IsFork = repository.IsFork,
                HttpsUrl = repository.HttpsUrl,
                SshUrl = repository.SshUrl,
                ProviderData = repository
            };
        }

        public static GitHubRepositoryEntry ToGitHubRepository(RemoteRepositoryEntry repository)
        {
            if (repository == null)
            {
                return null;
            }

            if (repository.ProviderData is GitHubRepositoryEntry gitHubRepository)
            {
                return gitHubRepository;
            }

            return new GitHubRepositoryEntry
            {
                NameWithOwner = repository.NameWithOwner,
                Description = repository.Description,
                Visibility = repository.Visibility,
                IsPrivate = repository.IsPrivate,
                DefaultBranch = repository.DefaultBranch,
                UpdatedAt = repository.UpdatedAt,
                CreatedAt = repository.CreatedAt,
                PushedAt = repository.PushedAt,
                DiskUsage = repository.DiskUsage,
                Stars = repository.Stars,
                Forks = repository.Forks,
                Watchers = repository.Watchers,
                HomepageUrl = repository.HomepageUrl,
                License = repository.License,
                PrimaryLanguage = repository.PrimaryLanguage,
                IsArchived = repository.IsArchived,
                IsFork = repository.IsFork,
                HttpsUrl = repository.HttpsUrl,
                SshUrl = repository.SshUrl
            };
        }
    }
}
