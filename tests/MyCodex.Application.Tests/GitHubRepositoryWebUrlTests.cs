using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitHubRepositoryWebUrlTests
    {
        [TestCase(" https://github.com/o/r.git ", null, null, "https://github.com/o/r")]
        [TestCase("https://github.com/o/r/", "o/r", "/releases", "https://github.com/o/r/releases")]
        [TestCase(null, " /o/r/ ", null, "https://github.com/o/r")]
        [TestCase("http://github.com/o/r", "o/r", "tree/main", "https://github.com/o/r/tree/main")]
        [TestCase("https://evil.example/o/r", "o/r", null, "https://github.com/o/r")]
        public void Build_ReturnsGitHubAddress(string httpsUrl, string nameWithOwner, string relativePath, string expected)
        {
            var repository = new GitHubRepositoryEntry { HttpsUrl = httpsUrl, NameWithOwner = nameWithOwner };

            Assert.That(GitHubRepositoryWebUrl.Build(repository, relativePath), Is.EqualTo(expected));
        }

        [TestCase("https://evil.example/o/r", null)]
        [TestCase(null, "no-owner")]
        [TestCase(null, null)]
        public void Build_ReturnsEmptyWithoutGitHubAddress(string httpsUrl, string nameWithOwner)
        {
            var repository = new GitHubRepositoryEntry { HttpsUrl = httpsUrl, NameWithOwner = nameWithOwner };

            Assert.That(GitHubRepositoryWebUrl.Build(repository, "tree/main"), Is.Empty);
        }

        [Test]
        public void Build_ReturnsEmptyForMissingRepository()
        {
            Assert.That(GitHubRepositoryWebUrl.Build(null, null), Is.Empty);
        }
    }
}
