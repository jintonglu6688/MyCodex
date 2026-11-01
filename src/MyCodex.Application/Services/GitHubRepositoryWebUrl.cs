using System;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class GitHubRepositoryWebUrl
    {
        // Returns an empty string when the repository has no github.com address to open.
        public static string Build(GitHubRepositoryEntry repository, string relativePath)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            var baseUrl = NormalizeHttpsUrl(repository.HttpsUrl);
            if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                var fullName = repository.NameWithOwner.Trim().Trim('/');
                if (fullName.IndexOf('/') > 0)
                {
                    baseUrl = "https://github.com/" + fullName;
                }
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(relativePath)
                ? baseUrl
                : baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }

        private static string NormalizeHttpsUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var value = url.Trim();
            if (!value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 4)
                : value;
        }
    }
}
