using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.GitHosting;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitHubCliCommandRunnerTests
    {
        [Test]
        public void ArgumentParser_PreservesQuotedSpecialCharacters()
        {
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "create",
                "demo",
                "--description",
                "contains $(uname) and \"quotes\""
            });

            var parsed = GitHubCliArgumentParser.Split(arguments);

            CollectionAssert.AreEqual(
                new[] { "repo", "create", "demo", "--description", "contains $(uname) and \"quotes\"" },
                parsed);
        }

        [Test]
        public void WslGitHubCliCommandRunner_PassesArgumentsWithoutShellEval()
        {
            var executor = new CapturingWslCommandExecutor();
            var runner = new WslGitHubCliCommandRunner(new WslAppServerConfigSnapshot(), executor);
            var arguments = GitCommandLineBuilder.BuildArguments(new[]
            {
                "repo",
                "create",
                "demo",
                "--description",
                "contains $(uname)"
            });

            runner.RunAsync(arguments, 15000, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Not.Contain("eval"));
            CollectionAssert.AreEqual(
                new[] { "repo", "create", "demo", "--description", "contains $(uname)" },
                executor.Arguments);
        }

        [Test]
        public void WslGitHubCliCommandRunner_RunAsync_ForwardsCancellationTokenToExecutor()
        {
            var executor = new CapturingWslCommandExecutor();
            var runner = new WslGitHubCliCommandRunner(new WslAppServerConfigSnapshot(), executor);
            using (var cancellation = new CancellationTokenSource())
            {
                runner.RunAsync("auth status", 15000, cancellation.Token).GetAwaiter().GetResult();
            }

            Assert.That(executor.CancellationTokenWasForwarded, Is.True);
        }

        [Test]
        public void WslGitHubCliCommandRunner_CloneRepository_ForwardsCancellationTokenToExecutor()
        {
            var executor = new CapturingWslCommandExecutor();
            var runner = new WslGitHubCliCommandRunner(new WslAppServerConfigSnapshot(), executor);

            using (var cancellation = new CancellationTokenSource())
            {
                runner.CloneRepository(
                    new GitHubRepositoryEntry { NameWithOwner = "example/demo" },
                    "/home/demo/projects",
                    "demo",
                    false,
                    false,
                    null,
                    cancellation.Token);
            }

            Assert.That(executor.CancellationTokenWasForwarded, Is.True);
        }

        [Test]
        public void WslGitHubCliCommandRunner_RedactsSensitiveOutput()
        {
            var executor = new CapturingWslCommandExecutor
            {
                Result = new WslCommandExecutionResult
                {
                    ExitCode = 1,
                    StandardOutput = "https://user:pass@example.com/repo.git",
                    StandardError = "ghp_SECRET"
                }
            };
            var runner = new WslGitHubCliCommandRunner(new WslAppServerConfigSnapshot(), executor);

            var result = runner.RunAsync("auth status", 15000, CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.StandardOutput, Is.EqualTo("https://***:***@example.com/repo.git"));
            Assert.That(result.StandardError, Is.EqualTo("***"));
        }

        [Test]
        public void RemoteSshGitHubCliCommandRunner_RunAsync_ForwardsCancellationTokenToExecutor()
        {
            var executor = new CapturingRemoteSshCommandExecutor();
            var runner = new RemoteSshGitHubCliCommandRunner(new RemoteMacSshConfigSnapshot(), executor);
            using (var cancellation = new CancellationTokenSource())
            {
                runner.RunAsync("auth status", 15000, cancellation.Token).GetAwaiter().GetResult();
            }

            Assert.That(executor.CancellationTokenWasForwarded, Is.True);
        }

        [Test]
        public void RemoteSshGitHubCliCommandRunner_ClonePassesParentFolderAndGhArgumentsSeparately()
        {
            var executor = new CapturingRemoteSshCommandExecutor();
            var runner = new RemoteSshGitHubCliCommandRunner(new RemoteMacSshConfigSnapshot(), executor);
            var repository = new GitHubRepositoryEntry
            {
                NameWithOwner = "example/demo",
                SshUrl = "git@github.com:example/demo.git"
            };

            var result = runner.CloneRepository(
                repository,
                "/Users/demo/projects",
                "repo $(unsafe)",
                false,
                true,
                null,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            CollectionAssert.AreEqual(
                new[]
                {
                    "/Users/demo/projects",
                    "repo $(unsafe)",
                    "repo",
                    "clone",
                    "git@github.com:example/demo.git",
                    "repo $(unsafe)",
                    "--",
                    "--progress"
                },
                executor.Arguments);
        }

        [Test]
        public void RemoteSshGitHubCliCommandRunner_CloneRepository_ForwardsCancellationTokenToBufferedExecutor()
        {
            var executor = new CapturingRemoteSshBufferedCommandExecutor();
            var runner = new RemoteSshGitHubCliCommandRunner(new RemoteMacSshConfigSnapshot(), executor);

            using (var cancellation = new CancellationTokenSource())
            {
                runner.CloneRepository(
                    new GitHubRepositoryEntry { NameWithOwner = "example/demo" },
                    "/Users/demo/projects",
                    "demo",
                    false,
                    false,
                    null,
                    cancellation.Token);
            }

            Assert.That(executor.CancellationTokenWasForwarded, Is.True);
        }

        [Test]
        public void RemoteSshGitHubCliCommandRunner_CloneUsesStreamingExecutorWhenAvailable()
        {
            var executor = new CapturingRemoteSshCommandExecutor();
            var runner = new RemoteSshGitHubCliCommandRunner(new RemoteMacSshConfigSnapshot(), executor);
            var progress = new CapturingProgress();

            var result = runner.CloneRepository(
                new GitHubRepositoryEntry { NameWithOwner = "example/demo" },
                "/Users/demo/projects",
                "demo",
                false,
                false,
                progress,
                CancellationToken.None);

            Assert.That(executor.StreamingUsed, Is.True);
            Assert.That(executor.TimeoutMilliseconds, Is.EqualTo(30 * 60 * 1000));
            Assert.That(result.TargetPath, Is.EqualTo("/Users/demo/projects/demo"));
            Assert.That(progress.Items.Select(item => item.Text), Is.EquivalentTo(new[] { "cloning", "receiving" }));
        }

        [Test]
        public void GitHubCliService_ArchiveRepository_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.ArchiveRepositoryAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "archive", "owner/demo", "--yes" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_SearchRepositories_BuildsSearchEndpointWithFilters()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);
            service.SearchRepositoriesAsync(
                    new GitHubRepositorySearchRequest
                    {
                        Keyword = "screen reader",
                        Language = "C#",
                        Topic = "accessibility",
                        Sort = "stars",
                        Order = "desc",
                        MinStars = 100,
                        MinForks = 10,
                        ExcludeForks = true,
                        ExcludeArchived = true,
                        PublicOnly = true,
                        Limit = 75
                    },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var arguments = GitHubCliArgumentParser.Split(runner.Arguments);
            Assert.That(arguments[0], Is.EqualTo("api"));
            Assert.That(arguments[1], Does.StartWith("search/repositories?q="));
            var endpoint = arguments[1];
            var queryString = endpoint.Substring(endpoint.IndexOf("q=") + 2);
            var encodedQuery = queryString.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)[0];
            var decodedQuery = Uri.UnescapeDataString(encodedQuery);

            Assert.That(decodedQuery, Does.Contain("screen reader"));
            Assert.That(decodedQuery, Does.Contain("language:C#"));
            Assert.That(decodedQuery, Does.Contain("topic:accessibility"));
            Assert.That(decodedQuery, Does.Contain("stars:>=100"));
            Assert.That(decodedQuery, Does.Contain("forks:>=10"));
            Assert.That(decodedQuery, Does.Contain("fork:false"));
            Assert.That(decodedQuery, Does.Contain("archived:false"));
            Assert.That(decodedQuery, Does.Contain("is:public"));
            Assert.That(endpoint, Does.Contain("sort=stars"));
            Assert.That(endpoint, Does.Contain("order=desc"));
            Assert.That(endpoint, Does.Contain("per_page=75"));
        }

        [Test]
        public void GitHubCliService_ForkRepositoryAsync_ParsesJsonOutput()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);
            runner.Result = new GitHubCliCommandResult
            {
                ExitCode = 0,
                StandardOutput = "{\"nameWithOwner\":\"forked/repo\",\"ssh_url\":\"git@github.com:forked/repo.git\",\"html_url\":\"https://github.com/forked/repo\",\"private\":false}",
                StandardError = string.Empty
            };

            var result = service.ForkRepositoryAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/source", HttpsUrl = "https://github.com/owner/source" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var arguments = GitHubCliArgumentParser.Split(runner.Arguments);
            CollectionAssert.AreEqual(
                new[] { "repo", "fork", "owner/source" },
                arguments);
            Assert.That(result.NameWithOwner, Is.EqualTo("forked/repo"));
            Assert.That(result.HttpsUrl, Is.EqualTo("https://github.com/forked/repo"));
            Assert.That(result.SshUrl, Is.EqualTo("git@github.com:forked/repo.git"));
        }

        [Test]
        public void GitHubCliService_ForkRepositoryAsync_ParsesUrlFallbackFromOutput()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);
            runner.Result = new GitHubCliCommandResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = "Creating fork https://github.com/forked/repo done."
            };

            var result = service.ForkRepositoryAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/source", HttpsUrl = "https://github.com/owner/source" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var arguments = GitHubCliArgumentParser.Split(runner.Arguments);
            CollectionAssert.AreEqual(
                new[] { "repo", "fork", "owner/source" },
                arguments);
            Assert.That(result.NameWithOwner, Is.EqualTo("forked/repo"));
            Assert.That(result.HttpsUrl, Is.EqualTo("https://github.com/forked/repo"));
            Assert.That(result.SshUrl, Is.Null);
        }

        [Test]
        public void GitHubCliService_ForkRepositoryAsync_FallsBackToSourceRepository_WhenOutputIsUnrecognized()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);
            runner.Result = new GitHubCliCommandResult
            {
                ExitCode = 0,
                StandardOutput = "Fork operation succeeded.",
                StandardError = string.Empty
            };

            var result = service.ForkRepositoryAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/source", HttpsUrl = "https://github.com/owner/source" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var arguments = GitHubCliArgumentParser.Split(runner.Arguments);
            CollectionAssert.AreEqual(
                new[] { "repo", "fork", "owner/source" },
                arguments);
            Assert.That(result.NameWithOwner, Is.EqualTo("owner/source"));
            Assert.That(result.HttpsUrl, Is.EqualTo("https://github.com/owner/source"));
        }

        [Test]
        public void GitHubCliService_SearchRepositories_ParsesSearchResponse()
        {
            var runner = new CapturingGitHubCliCommandRunner
            {
                Result = new GitHubCliCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = "{\"total_count\":1,\"incomplete_results\":false,\"items\":[{\"full_name\":\"owner/demo\",\"description\":\"demo repo\",\"private\":false,\"default_branch\":\"main\",\"updated_at\":\"2026-05-01T01:02:03Z\",\"created_at\":\"2026-01-01T01:01:01Z\",\"pushed_at\":\"2026-03-03T03:03:03Z\",\"html_url\":\"https://github.com/owner/demo\",\"ssh_url\":\"git@github.com:owner/demo.git\",\"size\":123,\"stargazers_count\":120,\"forks_count\":11,\"watchers_count\":20,\"language\":\"C#\",\"license\":{\"spdx_id\":\"MIT\"},\"fork\":false,\"archived\":false,\"open_issues_count\":2,\"topics\":[\"csharp\",\"winforms\"]}]}",
                    StandardError = string.Empty
                }
            };
            var service = new GitHubCliService(runner);

            var result = service.SearchRepositoriesAsync(new GitHubRepositorySearchRequest(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.TotalCount, Is.EqualTo(1));
            Assert.That(result.IncompleteResults, Is.False);
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].NameWithOwner, Is.EqualTo("owner/demo"));
            Assert.That(result.Items[0].Description, Is.EqualTo("demo repo"));
            Assert.That(result.Items[0].IsPrivate, Is.False);
            Assert.That(result.Items[0].DefaultBranch, Is.EqualTo("main"));
            Assert.That(result.Items[0].DiskUsage, Is.EqualTo(123));
            Assert.That(result.Items[0].Stars, Is.EqualTo(120));
            Assert.That(result.Items[0].Forks, Is.EqualTo(11));
            Assert.That(result.Items[0].Watchers, Is.EqualTo(20));
            Assert.That(result.Items[0].OpenIssues, Is.EqualTo(2));
            Assert.That(result.Items[0].PrimaryLanguage, Is.EqualTo("C#"));
            Assert.That(result.Items[0].License, Is.EqualTo("MIT"));
            Assert.That(result.Items[0].Topics, Is.EqualTo("csharp,winforms"));
        }

        [Test]
        public void GitHubCliService_GetRepositoryDetails_UsesRepositoryApiAndParsesPublicFields()
        {
            var runner = new CapturingGitHubCliCommandRunner
            {
                Result = new GitHubCliCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = "{\"full_name\":\"owner/demo\",\"description\":\"demo repo\",\"private\":false,\"default_branch\":\"main\",\"updated_at\":\"2026-05-01T01:02:03Z\",\"created_at\":\"2026-01-01T01:01:01Z\",\"pushed_at\":\"2026-03-03T03:03:03Z\",\"html_url\":\"https://github.com/owner/demo\",\"ssh_url\":\"git@github.com:owner/demo.git\",\"size\":123,\"stargazers_count\":120,\"forks_count\":11,\"watchers_count\":20,\"language\":\"C#\",\"license\":{\"spdx_id\":\"MIT\"},\"fork\":false,\"archived\":false,\"open_issues_count\":7,\"topics\":[\"csharp\",\"winforms\"]}",
                    StandardError = string.Empty
                }
            };
            var service = new GitHubCliService(runner);

            var result = service.GetRepositoryDetailsAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "api", "repos/owner/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));
            Assert.That(result.NameWithOwner, Is.EqualTo("owner/demo"));
            Assert.That(result.OpenIssues, Is.EqualTo(7));
            Assert.That(result.Topics, Is.EqualTo("csharp,winforms"));
            Assert.That(result.License, Is.EqualTo("MIT"));
            Assert.That(result.PrimaryLanguage, Is.EqualTo("C#"));
        }

        [Test]
        public void GitHubCliService_UnarchiveRepository_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.UnarchiveRepositoryAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "unarchive", "owner/demo", "--yes" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_DeleteRepository_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.DeleteRepositoryAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "delete", "owner/demo", "--yes" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_RenameRepository_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.RenameRepositoryAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, "demo-renamed", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "rename", "-R", "owner/demo", "demo-renamed", "--yes" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_EditRepositoryMetadata_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.EditRepositoryMetadataAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    "new description",
                    "https://example.com/demo",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "edit", "owner/demo", "--description", "new description", "--homepage", "https://example.com/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_SetDefaultBranch_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.SetDefaultBranchAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, "main", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "edit", "owner/demo", "--default-branch", "main" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_ChangeRepositoryVisibility_RunsExplicitRepositoryCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.ChangeRepositoryVisibilityAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, "private", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "repo", "edit", "owner/demo", "--visibility", "private", "--accept-visibility-change-consequences" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_DeleteRepositoryBranch_RunsExplicitApiCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.DeleteRepositoryBranchAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, "feature/demo", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "api", "-X", "DELETE", "repos/owner/demo/git/refs/heads/feature/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_ListPullRequests_AppliesStateAndScopeFilters()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.ListPullRequestsAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    25,
                    "open",
                    "review-requested",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[]
                {
                    "pr",
                    "list",
                    "-R",
                    "owner/demo",
                    "--state",
                    "open",
                    "--limit",
                    "25",
                    "--json",
                    "number,title,state,author,isDraft,reviewDecision,comments,createdAt,updatedAt,url,baseRefName,headRefName,mergedAt",
                    "--search",
                    "review-requested:@me"
                },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_CreatePullRequest_RunsExplicitCreateCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.CreatePullRequestAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    "main",
                    "feature/demo",
                    "Add demo",
                    "Body with details",
                    true,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[]
                {
                    "pr",
                    "create",
                    "-R",
                    "owner/demo",
                    "--base",
                    "main",
                    "--head",
                    "feature/demo",
                    "--title",
                    "Add demo",
                    "--body",
                    "Body with details",
                    "--draft"
                },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_AddPullRequestComment_RunsExplicitCommentCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.AddPullRequestCommentAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    12,
                    "Looks good",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "pr", "comment", "12", "-R", "owner/demo", "--body", "Looks good" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_CloseAndReopenPullRequest_RunExplicitStateCommands()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.ClosePullRequestAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, 12, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "pr", "close", "12", "-R", "owner/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));

            service.ReopenPullRequestAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, 12, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "pr", "reopen", "12", "-R", "owner/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_ListIssues_AppliesStateAndScopeFilters()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.ListIssuesAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    30,
                    "open",
                    "assigned",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[]
                {
                    "issue",
                    "list",
                    "-R",
                    "owner/demo",
                    "--state",
                    "open",
                    "--limit",
                    "30",
                    "--json",
                    "number,title,state,author,comments,createdAt,updatedAt,url",
                    "--assignee",
                    "@me"
                },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_CreateIssue_RunsExplicitCreateCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.CreateIssueAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    "Bug title",
                    "Bug body",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "issue", "create", "-R", "owner/demo", "--title", "Bug title", "--body", "Bug body" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_AddIssueComment_RunsExplicitCommentCommand()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.AddIssueCommentAsync(
                    new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                    7,
                    "Investigating",
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "issue", "comment", "7", "-R", "owner/demo", "--body", "Investigating" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        [Test]
        public void GitHubCliService_CloseAndReopenIssue_RunExplicitStateCommands()
        {
            var runner = new CapturingGitHubCliCommandRunner();
            var service = new GitHubCliService(runner);

            service.CloseIssueAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, 7, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "issue", "close", "7", "-R", "owner/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));

            service.ReopenIssueAsync(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }, 7, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(
                new[] { "issue", "reopen", "7", "-R", "owner/demo" },
                GitHubCliArgumentParser.Split(runner.Arguments));
        }

        private sealed class CapturingGitHubCliCommandRunner : IGitHubCliCommandRunner
        {
            public GitHubCliCommandResult Result { get; set; }

            public string Arguments { get; private set; }

            public Task<GitHubCliCommandResult> RunAsync(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
            {
                Arguments = arguments;
                return Task.FromResult(Result ?? new GitHubCliCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty
                });
            }

            public GitCloneResult CloneRepository(
                GitHubRepositoryEntry repository,
                string targetParentDirectory,
                string targetFolderName,
                bool recurseSubmodules,
                bool useSshUrl,
                IProgress<GitCloneProgress> progress,
                CancellationToken cancellationToken)
            {
                return new GitCloneResult { Success = true };
            }

            public void StartInteractiveLogin()
            {
            }
        }

        private sealed class CapturingWslCommandExecutor : IWslCommandExecutor
        {
            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public WslCommandExecutionResult Result { get; set; }

            public string Script { get; private set; }

            public string[] Arguments { get; private set; }

            public bool CancellationTokenWasForwarded { get; private set; }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                Script = script;
                Arguments = arguments == null ? new string[0] : arguments.ToArray();
                return Task.FromResult(Result ?? new WslCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "{}",
                    StandardError = string.Empty
                });
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                CancellationTokenWasForwarded = cancellationToken.CanBeCanceled;
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }
        }

        private sealed class CapturingRemoteSshCommandExecutor : IRemoteSshCommandExecutor, IRemoteSshStreamingCommandExecutor
        {
            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public string Script { get; private set; }

            public string[] Arguments { get; private set; }

            public bool StreamingUsed { get; private set; }

            public int TimeoutMilliseconds { get; private set; }

            public bool CancellationTokenWasForwarded { get; private set; }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                Script = script;
                Arguments = arguments == null ? new string[0] : arguments.ToArray();
                TimeoutMilliseconds = timeoutMilliseconds;
                return Task.FromResult(new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "MYCODEX_CLONE_TARGET\t/Users/demo/projects/repo $(unsafe)\n",
                    StandardError = string.Empty
                });
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                CancellationTokenWasForwarded = cancellationToken.CanBeCanceled;
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteStreamingAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                Action<string, bool> outputLineReceived,
                params string[] arguments)
            {
                CancellationTokenWasForwarded = cancellationToken.CanBeCanceled;
                StreamingUsed = true;
                Script = script;
                Arguments = arguments == null ? new string[0] : arguments.ToArray();
                TimeoutMilliseconds = timeoutMilliseconds;
                if (outputLineReceived != null)
                {
                    outputLineReceived("cloning", false);
                    outputLineReceived("receiving", true);
                    outputLineReceived("MYCODEX_CLONE_TARGET\t/Users/demo/projects/demo", false);
                }

                return Task.FromResult(new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "cloning\nMYCODEX_CLONE_TARGET\t/Users/demo/projects/demo\n",
                    StandardError = "receiving\n"
                });
            }
        }

        private sealed class CapturingRemoteSshBufferedCommandExecutor : IRemoteSshCommandExecutor
        {
            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public bool CancellationTokenWasForwarded { get; private set; }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                return Task.FromResult(new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "MYCODEX_CLONE_TARGET\t/Users/demo/projects/demo\n",
                    StandardError = string.Empty
                });
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                CancellationTokenWasForwarded = cancellationToken.CanBeCanceled;
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }
        }

        private sealed class CapturingProgress : IProgress<GitCloneProgress>
        {
            public CapturingProgress()
            {
                Items = new List<GitCloneProgress>();
            }

            public List<GitCloneProgress> Items { get; private set; }

            public void Report(GitCloneProgress value)
            {
                Items.Add(value);
            }
        }
    }
}
