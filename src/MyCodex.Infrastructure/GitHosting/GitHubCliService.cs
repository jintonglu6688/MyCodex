using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.GitHosting
{
    public sealed class GitHubCliService : IGitHubCliService
    {
        private const int CommandTimeoutMilliseconds = 15000;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly IGitHubCliCommandRunner _commandRunner;

        public GitHubCliService()
            : this(new LocalGitHubCliCommandRunner())
        {
        }

        internal GitHubCliService(IGitHubCliCommandRunner commandRunner)
        {
            _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        }

        public static GitHubCliService CreateRemoteSsh(RemoteMacSshConfigSnapshot config)
        {
            return new GitHubCliService(new RemoteSshGitHubCliCommandRunner(config));
        }

        public static GitHubCliService CreateWsl(WslAppServerConfigSnapshot config)
        {
            return new GitHubCliService(new WslGitHubCliCommandRunner(config));
        }

        public async Task<GitHubCliAccountSnapshot> ReadAccountAsync(CancellationToken cancellationToken)
        {
            var versionResult = await RunGhAsync("--version", cancellationToken).ConfigureAwait(false);
            if (versionResult.ExitCode == GitHubCliCommandResult.NotFoundExitCode)
            {
                return new GitHubCliAccountSnapshot
                {
                    IsInstalled = false,
                    IsAuthenticated = false,
                    Status = SharedLocalization.Text("GitHubCli.Status.NotInstalled", "GitHub CLI is not installed."),
                    Details = versionResult.StandardError
                };
            }

            var version = ParseVersion(versionResult.StandardOutput);
            var authResult = await RunGhAsync("auth status --hostname github.com", cancellationToken).ConfigureAwait(false);
            var authOutput = Combine(authResult.StandardOutput, authResult.StandardError);
            if (!authResult.Success)
            {
                return new GitHubCliAccountSnapshot
                {
                    IsInstalled = true,
                    IsAuthenticated = false,
                    Version = version,
                    Status = SharedLocalization.Text("GitHubCli.Status.NotAuthenticated", "GitHub CLI is not logged in to GitHub."),
                    Details = Redact(authOutput)
                };
            }

            var apiResult = await RunGhAsync("api user", cancellationToken).ConfigureAwait(false);
            var user = apiResult.Success ? Deserialize(apiResult.StandardOutput) : new Dictionary<string, object>();

            return new GitHubCliAccountSnapshot
            {
                IsInstalled = true,
                IsAuthenticated = true,
                Version = version,
                Login = GetString(user, "login") ?? ParseLogin(authOutput),
                DisplayName = GetString(user, "name"),
                Email = GetString(user, "email"),
                ProfileUrl = GetString(user, "html_url"),
                GitProtocol = ParseValue(authOutput, "Git operations protocol:"),
                TokenScopes = ParseValue(authOutput, "Token scopes:"),
                Status = SharedLocalization.Text("GitHubCli.Status.Authenticated", "GitHub CLI is logged in to GitHub."),
                Details = Redact(Combine(authOutput, apiResult.Success ? apiResult.StandardOutput : apiResult.StandardError))
            };
        }

        public void StartInteractiveLogin()
        {
            _commandRunner.StartInteractiveLogin();
        }

        public async Task<IReadOnlyList<GitHubRepositoryEntry>> ListRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            var normalizedLimit = limit <= 0 ? 100 : Math.Min(limit, 200);
            var arguments = string.Format(
                "repo list --limit {0} --json nameWithOwner,description,visibility,isPrivate,defaultBranchRef,updatedAt,createdAt,pushedAt,url,sshUrl,diskUsage,primaryLanguage,stargazerCount,forkCount,watchers,homepageUrl,licenseInfo,isArchived,isFork",
                normalizedLimit);
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == GitHubCliCommandResult.NotFoundExitCode)
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitHubCli.Status.NotInstalled", "GitHub CLI is not installed."));
            }

            if (!result.Success)
            {
                throw new InvalidOperationException(Redact(string.IsNullOrWhiteSpace(result.StandardError)
                    ? SharedLocalization.Text("GitHubCli.Status.NotAuthenticated", "GitHub CLI is not logged in to GitHub.")
                    : result.StandardError.Trim()));
            }

            return FilterRepositories(ParseRepositories(result.StandardOutput), keyword);
        }

        public async Task<GitHubRepositorySearchResult> SearchRepositoriesAsync(
            GitHubRepositorySearchRequest request,
            CancellationToken cancellationToken)
        {
            var searchRequest = request ?? new GitHubRepositorySearchRequest();
            var normalizedLimit = NormalizeLimit(searchRequest.Limit, 50, 100);
            var query = BuildSearchQuery(searchRequest);
            var searchParameters = BuildSearchApiParameters(query, searchRequest.Sort, searchRequest.Order, normalizedLimit);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "search/repositories?" + searchParameters
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseRepositorySearchResult(result.StandardOutput);
        }

        public async Task<IReadOnlyList<GitHubRepositoryEntry>> ListEmptyRepositoriesAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            var repositories = await ListRepositoriesAsync(keyword, limit, cancellationToken).ConfigureAwait(false);
            return repositories
                .Where(IsEmptyRepository)
                .ToArray();
        }

        public async Task<GitHubRepositoryEntry> GetRepositoryDetailsAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "repos/" + repo
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseSearchRepository(Deserialize(result.StandardOutput));
        }

        public async Task<IReadOnlyList<GitHubBranchEntry>> ListBranchesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "repos/" + repo + "/branches?per_page=" + normalizedLimit
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseBranches(result.StandardOutput);
        }

        public async Task DeleteRepositoryBranchAsync(
            GitHubRepositoryEntry repository,
            string branchName,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var branch = NormalizeRequired(branchName, "branchName").Trim().Trim('/');
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "-X",
                "DELETE",
                "repos/" + repo + "/git/refs/heads/" + branch
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task<IReadOnlyList<GitHubTagEntry>> ListTagsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "repos/" + repo + "/tags?per_page=" + normalizedLimit
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseTags(result.StandardOutput);
        }

        public async Task<IReadOnlyList<GitHubReleaseEntry>> ListReleasesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "release",
                "list",
                "-R",
                repo,
                "--limit",
                normalizedLimit.ToString(),
                "--json",
                "tagName,name,isDraft,isPrerelease,isLatest,publishedAt,createdAt"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseReleases(result.StandardOutput);
        }

        public async Task<GitHubReleaseDetailEntry> GetReleaseDetailsAsync(
            GitHubRepositoryEntry repository,
            string tagName,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var tag = NormalizeRequired(tagName, "tagName");
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "release",
                "view",
                tag,
                "-R",
                repo,
                "--json",
                "name,tagName,isDraft,isPrerelease,publishedAt,createdAt,author,body,assets,url"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseReleaseDetails(result.StandardOutput);
        }

        public async Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            return await ListIssuesAsync(repository, limit, "all", null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(
            GitHubRepositoryEntry repository,
            int limit,
            string state,
            string scope,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = new List<string>
            {
                "issue",
                "list",
                "-R",
                repo,
                "--state",
                NormalizeIssueState(state),
                "--limit",
                normalizedLimit.ToString(),
                "--json",
                "number,title,state,author,comments,createdAt,updatedAt,url"
            };
            AppendIssueScopeArguments(arguments, scope);
            var result = await RunGhAsync(GitCommandLineBuilder.BuildArguments(arguments), cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseIssues(result.StandardOutput);
        }

        public async Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            return await ListPullRequestsAsync(repository, limit, "all", null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            string state,
            string scope,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = new List<string>
            {
                "pr",
                "list",
                "-R",
                repo,
                "--state",
                NormalizePullRequestState(state),
                "--limit",
                normalizedLimit.ToString(),
                "--json",
                "number,title,state,author,isDraft,reviewDecision,comments,createdAt,updatedAt,url,baseRefName,headRefName,mergedAt"
            };
            AppendPullRequestScopeArguments(arguments, scope);
            var result = await RunGhAsync(GitCommandLineBuilder.BuildArguments(arguments), cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParsePullRequests(result.StandardOutput);
        }

        public async Task<IReadOnlyList<GitHubContributorEntry>> ListContributorsAsync(
            GitHubRepositoryEntry repository,
            int limit,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedLimit = NormalizeLimit(limit, 50, 100);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "api",
                "repos/" + repo + "/contributors?per_page=" + normalizedLimit
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseContributors(result.StandardOutput);
        }

        public async Task<GitHubDiscussionEntry> GetIssueDiscussionAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "issue",
                "view",
                number.ToString(),
                "-R",
                repo,
                "--json",
                "number,title,state,author,body,comments,createdAt,updatedAt,url"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseDiscussion(result.StandardOutput);
        }

        public async Task<GitHubDiscussionEntry> GetPullRequestDiscussionAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "pr",
                "view",
                number.ToString(),
                "-R",
                repo,
                "--json",
                "number,title,state,author,body,comments,createdAt,updatedAt,url"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseDiscussion(result.StandardOutput);
        }

        public async Task AddPullRequestCommentAsync(
            GitHubRepositoryEntry repository,
            int number,
            string body,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var comment = NormalizeRequired(body, "body");
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "pr",
                "comment",
                number.ToString(),
                "-R",
                repo,
                "--body",
                comment
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task AddIssueCommentAsync(
            GitHubRepositoryEntry repository,
            int number,
            string body,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var comment = NormalizeRequired(body, "body");
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "issue",
                "comment",
                number.ToString(),
                "-R",
                repo,
                "--body",
                comment
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task CloseIssueAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            await RunIssueStateCommandAsync("close", repository, number, cancellationToken).ConfigureAwait(false);
        }

        public async Task ReopenIssueAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            await RunIssueStateCommandAsync("reopen", repository, number, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> CreateIssueAsync(
            GitHubRepositoryEntry repository,
            string title,
            string body,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedTitle = NormalizeRequired(title, "title");
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "issue",
                "create",
                "-R",
                repo,
                "--title",
                normalizedTitle,
                "--body",
                body == null ? string.Empty : body.Trim()
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseFirstUrl(Combine(result.StandardOutput, result.StandardError));
        }

        public async Task ClosePullRequestAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            await RunPullRequestStateCommandAsync("close", repository, number, cancellationToken).ConfigureAwait(false);
        }

        public async Task ReopenPullRequestAsync(
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            await RunPullRequestStateCommandAsync("reopen", repository, number, cancellationToken).ConfigureAwait(false);
        }

        public async Task<string> CreatePullRequestAsync(
            GitHubRepositoryEntry repository,
            string baseBranch,
            string headBranch,
            string title,
            string body,
            bool draft,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedBase = NormalizeRequired(baseBranch, "baseBranch");
            var normalizedHead = NormalizeRequired(headBranch, "headBranch");
            var normalizedTitle = NormalizeRequired(title, "title");
            var arguments = new List<string>
            {
                "pr",
                "create",
                "-R",
                repo,
                "--base",
                normalizedBase,
                "--head",
                normalizedHead,
                "--title",
                normalizedTitle,
                "--body",
                body == null ? string.Empty : body.Trim()
            };
            if (draft)
            {
                arguments.Add("--draft");
            }

            var result = await RunGhAsync(GitCommandLineBuilder.BuildArguments(arguments), cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
            return ParseFirstUrl(Combine(result.StandardOutput, result.StandardError));
        }

        public async Task<GitHubCreateRepositoryResult> CreateRepositoryAsync(
            GitHubCreateRepositoryRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var name = NormalizeRepositoryName(request.Name);
            var arguments = BuildCreateRepositoryArguments(request, name);
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == GitHubCliCommandResult.NotFoundExitCode)
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitHubCli.Status.NotInstalled", "GitHub CLI is not installed."));
            }

            var output = Redact(Combine(result.StandardOutput, result.StandardError));
            if (!result.Success)
            {
                return new GitHubCreateRepositoryResult
                {
                    Success = false,
                    ExitCode = result.ExitCode,
                    Output = output
                };
            }

            return new GitHubCreateRepositoryResult
            {
                Success = true,
                ExitCode = result.ExitCode,
                Output = output,
                Repository = new GitHubRepositoryEntry
                {
                    NameWithOwner = name,
                    Description = request.Description,
                    IsPrivate = request.IsPrivate,
                    Visibility = request.IsPrivate ? "PRIVATE" : "PUBLIC",
                    HttpsUrl = ParseFirstUrl(output)
                }
            };
        }

        public async Task ArchiveRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            await RunRepositoryLifecycleCommandAsync("archive", repository, cancellationToken).ConfigureAwait(false);
        }

        public async Task UnarchiveRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            await RunRepositoryLifecycleCommandAsync("unarchive", repository, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeleteRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            await RunRepositoryLifecycleCommandAsync("delete", repository, cancellationToken).ConfigureAwait(false);
        }

        public async Task RenameRepositoryAsync(
            GitHubRepositoryEntry repository,
            string newName,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var name = NormalizeRepositoryName(newName);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "rename",
                "-R",
                repo,
                name,
                "--yes"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task EditRepositoryMetadataAsync(
            GitHubRepositoryEntry repository,
            string description,
            string homepageUrl,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "edit",
                repo,
                "--description",
                description == null ? string.Empty : description.Trim(),
                "--homepage",
                homepageUrl == null ? string.Empty : homepageUrl.Trim()
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task SetDefaultBranchAsync(
            GitHubRepositoryEntry repository,
            string branchName,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var branch = NormalizeRequired(branchName, "branchName");
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "edit",
                repo,
                "--default-branch",
                branch
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        public async Task ChangeRepositoryVisibilityAsync(
            GitHubRepositoryEntry repository,
            string visibility,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var normalizedVisibility = NormalizeRepositoryVisibility(visibility);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "edit",
                repo,
                "--visibility",
                normalizedVisibility,
                "--accept-visibility-change-consequences"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
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
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            return Task.Run(
                () => _commandRunner.CloneRepository(repository, targetParentDirectory, targetFolderName, recurseSubmodules, useSshUrl, progress, cancellationToken),
                cancellationToken);
        }

        public async Task<GitHubRepositoryEntry> ForkRepositoryAsync(
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            var sourceRepository = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "fork",
                sourceRepository
            });

            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);

            var forked = GetForkedRepositoryFromGitHubCliOutput(result);
            if (!string.IsNullOrWhiteSpace(forked.NameWithOwner))
            {
                return forked;
            }

            var fallbackHttpsUrl = string.IsNullOrWhiteSpace(repository.HttpsUrl)
                ? string.Format("https://github.com/{0}", sourceRepository)
                : repository.HttpsUrl.Trim();
            return new GitHubRepositoryEntry
            {
                NameWithOwner = sourceRepository,
                HttpsUrl = string.IsNullOrWhiteSpace(forked.HttpsUrl)
                    ? fallbackHttpsUrl
                    : forked.HttpsUrl,
                SshUrl = forked.SshUrl
            };
        }

        private async Task RunRepositoryLifecycleCommandAsync(
            string command,
            GitHubRepositoryEntry repository,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                command,
                repo,
                "--yes"
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        private Task<GitHubCliCommandResult> RunGhAsync(string arguments, CancellationToken cancellationToken)
        {
            return _commandRunner.RunAsync(arguments, CommandTimeoutMilliseconds, cancellationToken);
        }

        private Dictionary<string, object> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, object>();
            }

            return _serializer.DeserializeObject(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private object[] DeserializeArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new object[0];
            }

            return _serializer.DeserializeObject(json) as object[] ?? new object[0];
        }

        private IReadOnlyList<GitHubRepositoryEntry> ParseRepositories(string json)
        {
            var entries = new List<GitHubRepositoryEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(ParseRepository(source));
            }

            return entries;
        }

        private GitHubRepositorySearchResult ParseRepositorySearchResult(string json)
        {
            var source = Deserialize(NormalizeSearchResponseJson(json));
            var items = source.ContainsKey("items") ? source["items"] as object[] : null;
            var repositories = new List<GitHubRepositoryEntry>();
            foreach (var item in items ?? new object[0])
            {
                var repository = item as Dictionary<string, object>;
                if (repository == null)
                {
                    continue;
                }

                repositories.Add(ParseSearchRepository(repository));
            }

            return new GitHubRepositorySearchResult
            {
                TotalCount = GetNullableLong(source, "total_count"),
                IncompleteResults = GetBool(source, "incomplete_results"),
                Items = repositories
            };
        }

        private static string NormalizeSearchResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            var normalized = Regex.Replace(
                json,
                @"""license""\s*:\s*\{\s*""spdx_id""\s*:\s*""([^""]+)""\s*\}",
                "\"license\":\"$1\"",
                RegexOptions.IgnoreCase);

            return Regex.Replace(
                normalized,
                @"""topics""\s*:\s*\[([^\]]*)\]",
                match =>
                {
                    var inner = match.Groups[1].Value;
                    var values = Regex
                        .Matches(inner, @"""([^""]*)""")
                        .OfType<Match>()
                        .Select(item => item.Groups[1].Value)
                        .Where(value => !string.IsNullOrWhiteSpace(value));
                    var text = string.Join(",", values);
                    return string.Format("\"topics\":\"{0}\"", text);
                },
                RegexOptions.IgnoreCase);
        }

        private static string BuildSearchApiParameters(
            string query,
            string sort,
            string order,
            int limit)
        {
            var normalizedQuery = string.IsNullOrWhiteSpace(query) ? "stars:>=1" : query;
            var parts = new List<string>
            {
                $"q={Uri.EscapeDataString(normalizedQuery)}",
                $"per_page={NormalizeLimit(limit, 50, 100)}"
            };

            var normalizedSort = NormalizeSearchSort(sort);
            if (!string.IsNullOrWhiteSpace(normalizedSort))
            {
                parts.Add($"sort={normalizedSort}");
                parts.Add($"order={NormalizeSearchOrder(order)}");
            }

            return string.Join("&", parts);
        }

        private static string BuildSearchQuery(GitHubRepositorySearchRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                terms.Add(request.Keyword.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                terms.Add("language:" + request.Language.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.Topic))
            {
                terms.Add("topic:" + request.Topic.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.License))
            {
                terms.Add("license:" + request.License.Trim());
            }

            if (request.MinStars.HasValue && request.MinStars.Value >= 0)
            {
                terms.Add("stars:>=" + request.MinStars.Value);
            }

            if (request.MaxStars.HasValue && request.MaxStars.Value >= 0)
            {
                terms.Add("stars:<=" + request.MaxStars.Value);
            }

            if (request.MinForks.HasValue && request.MinForks.Value >= 0)
            {
                terms.Add("forks:>=" + request.MinForks.Value);
            }

            if (request.MaxForks.HasValue && request.MaxForks.Value >= 0)
            {
                terms.Add("forks:<=" + request.MaxForks.Value);
            }

            if (request.MinOpenIssues.HasValue && request.MinOpenIssues.Value >= 0)
            {
                terms.Add("open_issues:>=" + request.MinOpenIssues.Value);
            }

            if (request.MaxOpenIssues.HasValue && request.MaxOpenIssues.Value >= 0)
            {
                terms.Add("open_issues:<=" + request.MaxOpenIssues.Value);
            }

            if (request.ExcludeForks)
            {
                terms.Add("fork:false");
            }

            if (request.ExcludeArchived)
            {
                terms.Add("archived:false");
            }

            if (request.PublicOnly)
            {
                terms.Add("is:public");
            }

            return string.Join(" ", terms);
        }

        private static GitHubRepositoryEntry ParseSearchRepository(Dictionary<string, object> source)
        {
            return new GitHubRepositoryEntry
            {
                NameWithOwner = GetString(source, "nameWithOwner") ?? GetString(source, "full_name"),
                Description = GetString(source, "description"),
                Visibility = GetString(source, "visibility") ??
                             (GetBool(source, "private") ? "PRIVATE" : "PUBLIC"),
                IsPrivate = GetBool(source, "isPrivate") || GetBool(source, "private"),
                DefaultBranch = GetString(source, "defaultBranch") ?? GetString(source, "default_branch"),
                UpdatedAt = ParseDate(GetString(source, "updatedAt") ?? GetString(source, "updated_at")),
                CreatedAt = ParseDate(GetString(source, "createdAt") ?? GetString(source, "created_at")),
                PushedAt = ParseDate(GetString(source, "pushedAt") ?? GetString(source, "pushed_at")),
                DiskUsage = GetNullableLong(source, "diskUsage") ?? GetNullableLong(source, "size"),
                Stars = GetNullableInt(source, "stargazerCount") ?? GetNullableInt(source, "stargazers_count"),
                Forks = GetNullableInt(source, "forkCount") ?? GetNullableInt(source, "forks_count"),
                Watchers = GetNestedNullableInt(source, "watchers", "totalCount") ?? GetNullableInt(source, "watchers_count"),
                HomepageUrl = GetString(source, "homepageUrl") ?? GetString(source, "homepage"),
                License = GetNestedString(source, "licenseInfo", "spdxId") ??
                          GetNestedString(source, "licenseInfo", "name") ??
                          GetNestedString(source, "license", "spdx_id") ??
                          GetNestedString(source, "license", "name") ??
                          GetString(source, "license"),
                PrimaryLanguage = GetNestedString(source, "primaryLanguage", "name") ?? GetString(source, "language"),
                IsArchived = GetBool(source, "isArchived") || GetBool(source, "archived"),
                IsFork = GetBool(source, "isFork") || GetBool(source, "fork"),
                HttpsUrl = GetString(source, "html_url") ?? GetString(source, "url"),
                SshUrl = GetString(source, "sshUrl") ?? GetString(source, "ssh_url"),
                OpenIssues = GetNullableInt(source, "openIssuesCount") ?? GetNullableInt(source, "open_issues_count"),
                Topics = JoinTopics(source)
            };
        }

        private GitHubRepositoryEntry GetForkedRepositoryFromGitHubCliOutput(GitHubCliCommandResult result)
        {
            if (result == null)
            {
                return new GitHubRepositoryEntry();
            }

            var output = Combine(result.StandardOutput, result.StandardError);
            var candidate = TryDeserialize(NormalizeCommandOutputJson(output));
            if (candidate.Count > 0)
            {
                return ParseSearchRepository(candidate);
            }

            var repositoryUrl = ParseFirstUrl(output);
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return new GitHubRepositoryEntry();
            }

            var repositoryName = ExtractRepositoryNameFromGitHubUrl(repositoryUrl);
            return new GitHubRepositoryEntry
            {
                NameWithOwner = repositoryName ?? string.Empty,
                HttpsUrl = repositoryUrl,
                SshUrl = null
            };
        }

        private Dictionary<string, object> TryDeserialize(string json)
        {
            try
            {
                return Deserialize(json);
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static string NormalizeCommandOutputJson(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return output;
            }

            if (output.IndexOf('%') < 0)
            {
                return output;
            }

            try
            {
                var normalized = Uri.UnescapeDataString(output);
                return string.IsNullOrWhiteSpace(normalized) ? output : normalized;
            }
            catch
            {
                return output;
            }
        }

        private static string ExtractRepositoryNameFromGitHubUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
            {
                return null;
            }

            if (!string.Equals(parsedUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parts = parsedUrl.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                return null;
            }

            return parts[0] + "/" + parts[1];
        }

        private static string JoinTopics(IDictionary<string, object> source)
        {
            if (source == null || !source.ContainsKey("topics"))
            {
                return null;
            }

            var topics = source["topics"] as object[];
            if (topics == null)
            {
                return GetString(source, "topics");
            }

            return string.Join(",", topics.Select(GetObjectText).Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static GitHubRepositoryEntry ParseRepository(Dictionary<string, object> source)
        {
            return new GitHubRepositoryEntry
            {
                NameWithOwner = GetString(source, "nameWithOwner"),
                Description = GetString(source, "description"),
                Visibility = GetString(source, "visibility"),
                IsPrivate = GetBool(source, "isPrivate"),
                DefaultBranch = GetNestedString(source, "defaultBranchRef", "name"),
                UpdatedAt = ParseDate(GetString(source, "updatedAt")),
                CreatedAt = ParseDate(GetString(source, "createdAt")),
                PushedAt = ParseDate(GetString(source, "pushedAt")),
                DiskUsage = GetNullableLong(source, "diskUsage"),
                Stars = GetNullableInt(source, "stargazerCount"),
                Forks = GetNullableInt(source, "forkCount"),
                Watchers = GetNestedNullableInt(source, "watchers", "totalCount"),
                HomepageUrl = GetString(source, "homepageUrl"),
                License = GetNestedString(source, "licenseInfo", "spdxId") ?? GetNestedString(source, "licenseInfo", "name"),
                PrimaryLanguage = GetNestedString(source, "primaryLanguage", "name"),
                IsArchived = GetBool(source, "isArchived"),
                IsFork = GetBool(source, "isFork"),
                HttpsUrl = GetString(source, "url"),
                SshUrl = GetString(source, "sshUrl")
            };
        }

        private IReadOnlyList<GitHubBranchEntry> ParseBranches(string json)
        {
            var entries = new List<GitHubBranchEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubBranchEntry
                {
                    Name = GetString(source, "name"),
                    CommitSha = GetNestedString(source, "commit", "sha"),
                    IsProtected = GetBool(source, "protected")
                });
            }

            return entries;
        }

        private IReadOnlyList<GitHubTagEntry> ParseTags(string json)
        {
            var entries = new List<GitHubTagEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubTagEntry
                {
                    Name = GetString(source, "name"),
                    CommitSha = GetNestedString(source, "commit", "sha"),
                    ZipballUrl = GetString(source, "zipball_url"),
                    TarballUrl = GetString(source, "tarball_url")
                });
            }

            return entries;
        }

        private IReadOnlyList<GitHubReleaseEntry> ParseReleases(string json)
        {
            var entries = new List<GitHubReleaseEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubReleaseEntry
                {
                    TagName = GetString(source, "tagName"),
                    Name = GetString(source, "name"),
                    IsDraft = GetBool(source, "isDraft"),
                    IsPrerelease = GetBool(source, "isPrerelease"),
                    IsLatest = GetBool(source, "isLatest"),
                    PublishedAt = ParseDate(GetString(source, "publishedAt")),
                    CreatedAt = ParseDate(GetString(source, "createdAt"))
                });
            }

            return entries;
        }

        private GitHubReleaseDetailEntry ParseReleaseDetails(string json)
        {
            var source = Deserialize(json);
            var entry = new GitHubReleaseDetailEntry
            {
                TagName = GetString(source, "tagName"),
                Name = GetString(source, "name"),
                IsDraft = GetBool(source, "isDraft"),
                IsPrerelease = GetBool(source, "isPrerelease"),
                Author = GetNestedString(source, "author", "login"),
                Body = GetString(source, "body"),
                PublishedAt = ParseDate(GetString(source, "publishedAt")),
                CreatedAt = ParseDate(GetString(source, "createdAt")),
                Url = GetString(source, "url")
            };

            var assets = source.ContainsKey("assets") ? source["assets"] as object[] : null;
            foreach (var item in assets ?? new object[0])
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null)
                {
                    continue;
                }

                entry.Assets.Add(new GitHubReleaseAssetEntry
                {
                    Name = GetString(asset, "name"),
                    Label = GetString(asset, "label"),
                    ContentType = GetString(asset, "contentType"),
                    Size = GetNullableLong(asset, "size"),
                    DownloadCount = GetInt(asset, "downloadCount"),
                    State = GetString(asset, "state"),
                    CreatedAt = ParseDate(GetString(asset, "createdAt")),
                    UpdatedAt = ParseDate(GetString(asset, "updatedAt")),
                    Url = GetString(asset, "url")
                });
            }

            return entry;
        }

        private IReadOnlyList<GitHubIssueEntry> ParseIssues(string json)
        {
            var entries = new List<GitHubIssueEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubIssueEntry
                {
                    Number = GetInt(source, "number"),
                    Title = GetString(source, "title"),
                    State = GetString(source, "state"),
                    Author = GetNestedString(source, "author", "login"),
                    Comments = GetInt(source, "comments"),
                    CreatedAt = ParseDate(GetString(source, "createdAt")),
                    UpdatedAt = ParseDate(GetString(source, "updatedAt")),
                    Url = GetString(source, "url")
                });
            }

            return entries;
        }

        private IReadOnlyList<GitHubPullRequestEntry> ParsePullRequests(string json)
        {
            var entries = new List<GitHubPullRequestEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubPullRequestEntry
                {
                    Number = GetInt(source, "number"),
                    Title = GetString(source, "title"),
                    State = GetString(source, "state"),
                    Author = GetNestedString(source, "author", "login"),
                    IsDraft = GetBool(source, "isDraft"),
                    ReviewDecision = GetString(source, "reviewDecision"),
                    Comments = GetInt(source, "comments"),
                    BaseBranch = GetString(source, "baseRefName"),
                    HeadBranch = GetString(source, "headRefName"),
                    MergedAt = ParseDate(GetString(source, "mergedAt")),
                    CreatedAt = ParseDate(GetString(source, "createdAt")),
                    UpdatedAt = ParseDate(GetString(source, "updatedAt")),
                    Url = GetString(source, "url")
                });
            }

            return entries;
        }

        private IReadOnlyList<GitHubContributorEntry> ParseContributors(string json)
        {
            var entries = new List<GitHubContributorEntry>();
            foreach (var item in DeserializeArray(json))
            {
                var source = item as Dictionary<string, object>;
                if (source == null)
                {
                    continue;
                }

                entries.Add(new GitHubContributorEntry
                {
                    Login = GetString(source, "login"),
                    Type = GetString(source, "type"),
                    Contributions = GetInt(source, "contributions"),
                    Url = GetString(source, "html_url")
                });
            }

            return entries;
        }

        private GitHubDiscussionEntry ParseDiscussion(string json)
        {
            var source = Deserialize(json);
            var entry = new GitHubDiscussionEntry
            {
                Number = GetInt(source, "number"),
                Title = GetString(source, "title"),
                State = GetString(source, "state"),
                Author = GetNestedString(source, "author", "login"),
                Body = GetString(source, "body"),
                CreatedAt = ParseDate(GetString(source, "createdAt")),
                UpdatedAt = ParseDate(GetString(source, "updatedAt")),
                Url = GetString(source, "url")
            };

            var comments = source.ContainsKey("comments") ? source["comments"] as object[] : null;
            foreach (var item in comments ?? new object[0])
            {
                var comment = item as Dictionary<string, object>;
                if (comment == null)
                {
                    continue;
                }

                entry.Comments.Add(new GitHubDiscussionCommentEntry
                {
                    Author = GetNestedString(comment, "author", "login"),
                    Body = GetString(comment, "body"),
                    CreatedAt = ParseDate(GetString(comment, "createdAt")),
                    UpdatedAt = ParseDate(GetString(comment, "updatedAt")),
                    Url = GetString(comment, "url")
                });
            }

            return entry;
        }

        private async Task RunPullRequestStateCommandAsync(
            string command,
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "pr",
                command,
                number.ToString(),
                "-R",
                repo
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        private async Task RunIssueStateCommandAsync(
            string command,
            GitHubRepositoryEntry repository,
            int number,
            CancellationToken cancellationToken)
        {
            var repo = NormalizeRepositoryFullName(repository);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "issue",
                command,
                number.ToString(),
                "-R",
                repo
            });
            var result = await RunGhAsync(arguments, cancellationToken).ConfigureAwait(false);
            EnsureGhSuccess(result);
        }

        private static IReadOnlyList<GitHubRepositoryEntry> FilterRepositories(
            IReadOnlyList<GitHubRepositoryEntry> repositories,
            string keyword)
        {
            if (repositories == null)
            {
                return new List<GitHubRepositoryEntry>();
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return repositories;
            }

            var filter = keyword.Trim();
            var result = new List<GitHubRepositoryEntry>();
            foreach (var repository in repositories)
            {
                if (repository == null)
                {
                    continue;
                }

                if (Contains(repository.NameWithOwner, filter) ||
                    Contains(repository.Description, filter))
                {
                    result.Add(repository);
                }
            }

            return result;
        }

        private static string ParseVersion(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var firstLine = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0].Trim();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
        }

        private static string ParseLogin(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var match = Regex.Match(output, @"Logged in to github\.com account\s+([^\s]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ParseValue(string output, string prefix)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(prefix))
            {
                return null;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(2).Trim();
                }

                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(prefix.Length).Trim().Trim('\'');
                }
            }

            return null;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.ContainsKey(key))
            {
                return null;
            }

            return source[key] == null ? null : Convert.ToString(source[key]);
        }

        private static string GetNestedString(IDictionary<string, object> source, string key, string nestedKey)
        {
            if (source == null || !source.ContainsKey(key))
            {
                return null;
            }

            var nested = source[key] as Dictionary<string, object>;
            return GetString(nested, nestedKey);
        }

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.ContainsKey(key) || source[key] == null)
            {
                return false;
            }

            bool value;
            return bool.TryParse(Convert.ToString(source[key]), out value) && value;
        }

        private static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.ContainsKey(key) || source[key] == null)
            {
                return null;
            }

            long value;
            return long.TryParse(Convert.ToString(source[key]), out value) ? value : (long?)null;
        }

        private static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.ContainsKey(key) || source[key] == null)
            {
                return null;
            }

            int value;
            return int.TryParse(Convert.ToString(source[key]), out value) ? value : (int?)null;
        }

        private static int? GetNestedNullableInt(IDictionary<string, object> source, string key, string nestedKey)
        {
            if (source == null || !source.ContainsKey(key))
            {
                return null;
            }

            var nested = source[key] as Dictionary<string, object>;
            return GetNullableInt(nested, nestedKey);
        }

        private static int GetInt(IDictionary<string, object> source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.ContainsKey(key) || source[key] == null)
            {
                return 0;
            }

            int value;
            return int.TryParse(Convert.ToString(source[key]), out value) ? value : 0;
        }

        private static int NormalizeLimit(int limit, int defaultLimit, int maxLimit)
        {
            return limit <= 0 ? defaultLimit : Math.Min(limit, maxLimit);
        }

        private static string NormalizeSearchSort(string sort)
        {
            if (string.IsNullOrWhiteSpace(sort))
            {
                return null;
            }

            var value = sort.Trim().ToLowerInvariant();
            if (value == "stars" || value == "forks" || value == "updated" || value == "created")
            {
                return value;
            }

            return null;
        }

        private static string NormalizeSearchOrder(string order)
        {
            if (string.IsNullOrWhiteSpace(order))
            {
                return "desc";
            }

            var value = order.Trim().ToLowerInvariant();
            if (value == "asc" || value == "ascending")
            {
                return "asc";
            }

            return "desc";
        }

        private static string GetObjectText(object value)
        {
            return value == null ? null : Convert.ToString(value);
        }

        private static string NormalizePullRequestState(string value)
        {
            var state = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
            if (state == "open" || state == "closed" || state == "merged" || state == "all")
            {
                return state;
            }

            return "all";
        }

        private static string NormalizeIssueState(string value)
        {
            var state = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
            if (state == "open" || state == "closed" || state == "all")
            {
                return state;
            }

            return "all";
        }

        private static void AppendIssueScopeArguments(IList<string> arguments, string scope)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(scope))
            {
                return;
            }

            var value = scope.Trim().ToLowerInvariant();
            if (value == "authored")
            {
                arguments.Add("--author");
                arguments.Add("@me");
                return;
            }

            if (value == "assigned")
            {
                arguments.Add("--assignee");
                arguments.Add("@me");
            }
        }

        private static void AppendPullRequestScopeArguments(IList<string> arguments, string scope)
        {
            if (arguments == null || string.IsNullOrWhiteSpace(scope))
            {
                return;
            }

            var value = scope.Trim().ToLowerInvariant();
            if (value == "authored")
            {
                arguments.Add("--author");
                arguments.Add("@me");
                return;
            }

            if (value == "assigned")
            {
                arguments.Add("--assignee");
                arguments.Add("@me");
                return;
            }

            if (value == "review-requested")
            {
                arguments.Add("--search");
                arguments.Add("review-requested:@me");
            }
        }

        private static string NormalizeRepositoryFullName(GitHubRepositoryEntry repository)
        {
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                throw new ArgumentException(SharedLocalization.Text("GitHubCli.Error.RepositoryNameRequired", "Repository name cannot be empty."));
            }

            var value = repository.NameWithOwner.Trim().Trim('/');
            if (value.IndexOf('/') <= 0 ||
                value.StartsWith("-", StringComparison.Ordinal) ||
                value.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                throw new ArgumentException(SharedLocalization.Text("GitHubCli.Error.RepositoryNameInvalid", "Repository name is invalid."));
            }

            return value;
        }

        private static void EnsureGhSuccess(GitHubCliCommandResult result)
        {
            if (result == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitHubCli.Error.CommandFailed", "GitHub CLI command failed."));
            }

            if (result.ExitCode == GitHubCliCommandResult.NotFoundExitCode)
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitHubCli.Status.NotInstalled", "GitHub CLI is not installed."));
            }

            if (!result.Success)
            {
                throw new InvalidOperationException(Redact(string.IsNullOrWhiteSpace(result.StandardError)
                    ? SharedLocalization.Text("GitHubCli.Error.CommandFailed", "GitHub CLI command failed.")
                    : result.StandardError.Trim()));
            }
        }

        private static bool IsEmptyRepository(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return false;
            }

            return repository.DiskUsage.GetValueOrDefault() == 0 &&
                   string.IsNullOrWhiteSpace(repository.DefaultBranch);
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            DateTimeOffset date;
            return DateTimeOffset.TryParse(value, out date) ? date : (DateTimeOffset?)null;
        }

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildCreateRepositoryArguments(GitHubCreateRepositoryRequest request, string name)
        {
            var arguments = new List<string>
            {
                "repo",
                "create",
                name,
                request.IsPrivate ? "--private" : "--public"
            };

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                arguments.Add("--description");
                arguments.Add(request.Description.Trim());
            }

            if (request.AddReadme && string.IsNullOrWhiteSpace(request.SourceDirectory))
            {
                arguments.Add("--add-readme");
            }

            if (!string.IsNullOrWhiteSpace(request.SourceDirectory))
            {
                arguments.Add("--source");
                arguments.Add(request.SourceDirectory.Trim());
                arguments.Add("--remote");
                arguments.Add(string.IsNullOrWhiteSpace(request.RemoteName) ? "origin" : request.RemoteName.Trim());
            }

            return GitCommandLineBuilder.BuildArguments(arguments);
        }

        private static string NormalizeRepositoryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(SharedLocalization.Text("GitHubCli.Error.RepositoryNameRequired", "Repository name cannot be empty."));
            }

            var name = value.Trim();
            if (name.StartsWith("-", StringComparison.Ordinal) ||
                name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                throw new ArgumentException(SharedLocalization.Text("GitHubCli.Error.RepositoryNameInvalid", "Repository name is invalid."));
            }

            return name;
        }

        private static string NormalizeRepositoryVisibility(string value)
        {
            var visibility = NormalizeRequired(value, "visibility").ToLowerInvariant();
            if (visibility == "public" || visibility == "private" || visibility == "internal")
            {
                return visibility;
            }

            throw new ArgumentException(SharedLocalization.Text("GitHubCli.Error.RepositoryVisibilityInvalid", "Repository visibility is invalid."));
        }

        private static string ParseFirstUrl(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var match = Regex.Match(output, @"https://github\.com/[^\s]+", RegexOptions.IgnoreCase);
            return match.Success ? match.Value.Trim() : null;
        }

        private static string NormalizeRequired(string value, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(argumentName + " cannot be empty.", argumentName);
            }

            return value.Trim();
        }

        private static string Combine(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
            {
                return second ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(second))
            {
                return first ?? string.Empty;
            }

            return first.TrimEnd() + Environment.NewLine + second.TrimStart();
        }

        private static string Redact(string value)
        {
            return GitOutputSanitizer.RedactSensitiveText(value);
        }

    }
}
