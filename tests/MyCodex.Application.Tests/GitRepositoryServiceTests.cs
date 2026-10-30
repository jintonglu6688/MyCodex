using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class GitRepositoryServiceTests
    {
        private static GitRepositoryService CreateService()
        {
            return new GitRepositoryService(new MyCodex.Infrastructure.Git.GitCommandRunner());
        }

        private string _repositoryTemplate;

        [OneTimeSetUp]
        public void CreateRepositoryTemplate()
        {
            _repositoryTemplate = CreateTempDirectory();
            RunGit(_repositoryTemplate, "init");
            ConfigureTestIdentity(_repositoryTemplate);
        }

        [OneTimeTearDown]
        public void DeleteRepositoryTemplate()
        {
            DeleteDirectory(_repositoryTemplate);
        }

        /// <summary>
        /// Same result as git init plus the test identity: copies the repository prepared once for this fixture,
        /// which avoids three git process starts per test.
        /// </summary>
        private void InitializeTestRepository(string workspaceRoot)
        {
            var source = Path.Combine(_repositoryTemplate, ".git");
            var destination = Path.Combine(workspaceRoot, ".git");
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length + 1)));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(destination, file.Substring(source.Length + 1)));
            }
        }

        [Test]
        public void ReadStatus_ForNonGitDirectory_AllowsInitialization()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var service = CreateService();

                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(snapshot.IsGitRepository, Is.False);
                Assert.That(snapshot.CanInitializeRepository, Is.True);
                Assert.That(snapshot.Files, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatus_WhenGitStatusFailsButGitDirectoryExists_DoesNotOfferInitialization()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
                var runner = new FakeGitCommandRunner
                {
                    StatusExitCode = 128,
                    StatusError = "fatal: detected dubious ownership in repository at 'E:/qwen'"
                };
                var service = new GitRepositoryService(runner);

                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.CanInitializeRepository, Is.False);
                Assert.That(snapshot.StatusErrorMessage, Does.Contain("dubious ownership"));
                Assert.That(snapshot.Files, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatusAsync_CanceledTokenPropagatesToRunner()
        {
            var workspaceRoot = CreateTempDirectory();
            var runner = new BlockingCancellationGitCommandRunner();
            var service = new GitRepositoryService(runner);
            var cancellationTokenSource = new CancellationTokenSource();
            try
            {
                var task = service.ReadStatusAsync(workspaceRoot, cancellationTokenSource.Token);

                Assert.That(runner.RunStarted.Wait(TimeSpan.FromSeconds(2)), Is.True);
                cancellationTokenSource.Cancel();

                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.That(runner.LastCancellationToken, Is.EqualTo(cancellationTokenSource.Token));
            }
            finally
            {
                cancellationTokenSource.Dispose();
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void InitializeRepository_CreatesGitRepository()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var service = CreateService();

                var result = service.InitializeRepositoryAsync(workspaceRoot).GetAwaiter().GetResult();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.CanInitializeRepository, Is.False);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void StageAndUnstage_UpdatesStatusGroups()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);

                var service = CreateService();
                var initial = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                Assert.That(initial.Files.Any(item => item.IsUntracked && item.FilePath == "readme.txt"), Is.True);

                var stageResult = service.StageAsync(workspaceRoot, new[] { "readme.txt" }).GetAwaiter().GetResult();
                var staged = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                Assert.That(stageResult.Success, Is.True);
                Assert.That(staged.Files.Any(item => item.IsStaged && item.FilePath == "readme.txt"), Is.True);

                var unstageResult = service.UnstageAsync(workspaceRoot, new[] { "readme.txt" }).GetAwaiter().GetResult();
                var unstaged = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                Assert.That(unstageResult.Success, Is.True);
                Assert.That(unstaged.Files.Any(item => item.IsUntracked && item.FilePath == "readme.txt"), Is.True);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void Commit_CommitsStagedChanges()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);

                var service = CreateService();
                service.StageAllAsync(workspaceRoot).GetAwaiter().GetResult();
                var result = service.CommitAsync(workspaceRoot, "Initial commit").GetAwaiter().GetResult();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(snapshot.Files, Is.Empty);
                Assert.That(snapshot.HeadSha, Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void Commit_WithBody_WritesSubjectAndBody()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);

                var service = CreateService();
                service.StageAllAsync(workspaceRoot).GetAwaiter().GetResult();
                var result = service.CommitAsync(workspaceRoot, "Initial commit", "Detailed body").GetAwaiter().GetResult();
                var message = ReadGitOutput(workspaceRoot, "log -1 --pretty=%B").Trim();

                Assert.That(result.Success, Is.True);
                Assert.That(NormalizeNewLines(message), Is.EqualTo("Initial commit\n\nDetailed body"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void CommitAmend_ReplacesPreviousCommit()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");

                var service = CreateService();
                var result = service.CommitAmendAsync(workspaceRoot, "Updated initial commit", "Updated details").GetAwaiter().GetResult();
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 10).GetAwaiter().GetResult();
                var message = ReadGitOutput(workspaceRoot, "log -1 --pretty=%B").Trim();
                var content = File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt"));

                Assert.That(result.Success, Is.True);
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history[0].Subject, Is.EqualTo("Updated initial commit"));
                Assert.That(NormalizeNewLines(message), Is.EqualTo("Updated initial commit\n\nUpdated details"));
                Assert.That(content, Is.EqualTo("changed" + Environment.NewLine));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadCommitHistory_ReturnsNewestCommitsFirst()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");

                var service = CreateService();
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 10).GetAwaiter().GetResult();

                Assert.That(history.Count, Is.EqualTo(2));
                Assert.That(history[0].Subject, Is.EqualTo("Update readme"));
                Assert.That(history[1].Subject, Is.EqualTo("Initial commit"));
                Assert.That(history[0].FullSha, Is.Not.Null.And.Not.Empty);
                Assert.That(history[0].ShortSha, Is.Not.Null.And.Not.Empty);
                Assert.That(history[0].AuthorName, Is.EqualTo("MyCodex Tests"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadCommitHistory_WithRevision_ReturnsSelectedBranchHistory()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "checkout -b feature/demo");
                File.WriteAllText(Path.Combine(workspaceRoot, "feature.txt"), "feature" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Feature commit\"");
                RunGit(workspaceRoot, "checkout main");

                var service = CreateService();
                var currentHistory = service.ReadCommitHistoryAsync(workspaceRoot, 10).GetAwaiter().GetResult();
                var featureHistory = service.ReadCommitHistoryAsync(workspaceRoot, "feature/demo", 10).GetAwaiter().GetResult();

                Assert.That(currentHistory[0].Subject, Is.EqualTo("Initial commit"));
                Assert.That(featureHistory[0].Subject, Is.EqualTo("Feature commit"));
                Assert.That(featureHistory[1].Subject, Is.EqualTo("Initial commit"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadCommitFilesAndDiff_ReturnSelectedCommitDetails()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                var commitRef = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();

                var service = CreateService();
                var files = service.ReadCommitFilesAsync(workspaceRoot, commitRef).GetAwaiter().GetResult();
                var diff = service.ReadCommitDiffAsync(workspaceRoot, commitRef).GetAwaiter().GetResult();

                Assert.That(files.Count, Is.EqualTo(1));
                Assert.That(files[0].FilePath, Is.EqualTo("readme.txt"));
                Assert.That(files[0].ChangeKind, Is.EqualTo("added"));
                Assert.That(diff.Success, Is.True);
                Assert.That(diff.StandardOutput, Does.Contain("diff --git"));
                Assert.That(diff.StandardOutput, Does.Contain("+hello"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadBranches_ReturnsCurrentBranchFirst()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "branch feature/demo");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                RunGit(workspaceRoot, "push -u origin main");
                RunGit(workspaceRoot, "checkout -b remote-only");
                File.WriteAllText(Path.Combine(workspaceRoot, "remote.txt"), "remote" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Remote branch\"");
                RunGit(workspaceRoot, "push -u origin remote-only");
                RunGit(workspaceRoot, "checkout main");
                RunGit(workspaceRoot, "branch -D remote-only");

                var service = CreateService();
                var branches = service.ReadBranchesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(branches.Count, Is.EqualTo(4));
                Assert.That(branches[0].Name, Is.EqualTo("main"));
                Assert.That(branches[0].IsCurrent, Is.True);
                Assert.That(branches.Any(item => item.Name == "feature/demo"), Is.True);
                Assert.That(branches.Any(item => item.Name == "origin/remote-only" && item.IsRemote), Is.True);
                Assert.That(branches.Any(item => item.Name == "origin/HEAD"), Is.False);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void RemoteOperations_ReadAddSetUrlsAndRemoveRemote()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(workspaceRoot, "init");

                var service = CreateService();
                var addResult = service.AddRemoteAsync(workspaceRoot, "origin", "https://example.com/fetch.git").GetAwaiter().GetResult();
                var pushUrlResult = service.SetRemotePushUrlAsync(workspaceRoot, "origin", "https://example.com/push.git").GetAwaiter().GetResult();

                var remotes = service.ReadRemotesAsync(workspaceRoot).GetAwaiter().GetResult();
                var setFetchResult = service.SetRemoteUrlAsync(workspaceRoot, "origin", "https://example.com/new-fetch.git").GetAwaiter().GetResult();
                var updated = service.ReadRemotesAsync(workspaceRoot).GetAwaiter().GetResult();
                var removeResult = service.RemoveRemoteAsync(workspaceRoot, "origin").GetAwaiter().GetResult();
                var afterRemove = service.ReadRemotesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(addResult.Success, Is.True);
                Assert.That(pushUrlResult.Success, Is.True);
                Assert.That(remotes.Count, Is.EqualTo(1));
                Assert.That(remotes[0].Name, Is.EqualTo("origin"));
                Assert.That(remotes[0].FetchUrl, Is.EqualTo("https://example.com/fetch.git"));
                Assert.That(remotes[0].PushUrl, Is.EqualTo("https://example.com/push.git"));
                Assert.That(setFetchResult.Success, Is.True);
                Assert.That(updated[0].FetchUrl, Is.EqualTo("https://example.com/new-fetch.git"));
                Assert.That(updated[0].PushUrl, Is.EqualTo("https://example.com/push.git"));
                Assert.That(removeResult.Success, Is.True);
                Assert.That(afterRemove, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void UpstreamOperations_SetAndUnsetBranchUpstream()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                RunGit(workspaceRoot, "push origin main");

                var service = CreateService();
                var setResult = service.SetUpstreamAsync(workspaceRoot, "main", "origin/main").GetAwaiter().GetResult();
                var upstream = ReadGitOutput(workspaceRoot, "rev-parse --abbrev-ref --symbolic-full-name @{u}").Trim();
                var unsetResult = service.UnsetUpstreamAsync(workspaceRoot, "main").GetAwaiter().GetResult();
                var missingExitCode = RunGitAllowFailure(workspaceRoot, "rev-parse --abbrev-ref --symbolic-full-name @{u}");

                Assert.That(setResult.Success, Is.True);
                Assert.That(upstream, Is.EqualTo("origin/main"));
                Assert.That(unsetResult.Success, Is.True);
                Assert.That(missingExitCode, Is.Not.EqualTo(0));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void BranchOperations_SwitchAndCreateBranches()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "branch feature/demo");

                var service = CreateService();
                var checkoutResult = service.CheckoutBranchAsync(workspaceRoot, "feature/demo").GetAwaiter().GetResult();
                var afterCheckout = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var createResult = service.CreateBranchAsync(workspaceRoot, "feature/new").GetAwaiter().GetResult();
                var afterCreate = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(checkoutResult.Success, Is.True);
                Assert.That(afterCheckout.Branch, Is.EqualTo("feature/demo"));
                Assert.That(createResult.Success, Is.True);
                Assert.That(afterCreate.Branch, Is.EqualTo("feature/new"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void RenameBranch_RenamesSpecifiedLocalBranch()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "branch feature/old-name");

                var service = CreateService();
                var result = service.RenameBranchAsync(workspaceRoot, "feature/old-name", "feature/new-name").GetAwaiter().GetResult();
                var oldBranch = ReadGitOutput(workspaceRoot, "branch --list feature/old-name").Trim();
                var newBranch = ReadGitOutput(workspaceRoot, "branch --list feature/new-name").Trim();

                Assert.That(result.Success, Is.True);
                Assert.That(oldBranch, Is.Empty);
                Assert.That(newBranch, Does.Contain("feature/new-name"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void CompareBranches_ReturnsCurrentAndTargetOnlyCounts()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "checkout -b feature/compare");
                File.WriteAllText(Path.Combine(workspaceRoot, "feature.txt"), "feature" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Feature commit\"");
                RunGit(workspaceRoot, "checkout main");
                File.WriteAllText(Path.Combine(workspaceRoot, "main.txt"), "main" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Main commit\"");

                var service = CreateService();
                var comparison = service.CompareBranchesAsync(workspaceRoot, "HEAD", "feature/compare").GetAwaiter().GetResult();
                var targetOnlyHistory = service.ReadCommitHistoryAsync(workspaceRoot, "HEAD..feature/compare", 10).GetAwaiter().GetResult();

                Assert.That(comparison.CurrentOnlyCount, Is.EqualTo(1));
                Assert.That(comparison.TargetOnlyCount, Is.EqualTo(1));
                Assert.That(targetOnlyHistory.Count, Is.EqualTo(1));
                Assert.That(targetOnlyHistory[0].Subject, Is.EqualTo("Feature commit"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void DeleteBranch_RemovesMergedLocalBranch()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "branch feature/delete-me");

                var service = CreateService();
                var result = service.DeleteBranchAsync(workspaceRoot, "feature/delete-me").GetAwaiter().GetResult();
                var branches = service.ReadBranchesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(branches.Any(item => item.Name == "feature/delete-me"), Is.False);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void DeleteBranch_RefusesUnmergedLocalBranch()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "checkout -b feature/unmerged");
                File.WriteAllText(Path.Combine(workspaceRoot, "feature.txt"), "feature" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Feature commit\"");
                RunGit(workspaceRoot, "checkout main");

                var service = CreateService();
                var result = service.DeleteBranchAsync(workspaceRoot, "feature/unmerged").GetAwaiter().GetResult();
                var branches = service.ReadBranchesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.False);
                Assert.That(branches.Any(item => item.Name == "feature/unmerged"), Is.True);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void StashOperations_SaveApplyPopAndDrop()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                File.WriteAllText(Path.Combine(workspaceRoot, "note.txt"), "note" + Environment.NewLine);

                var service = CreateService();
                var pushResult = service.StashPushAsync(workspaceRoot, "save work", true).GetAwaiter().GetResult();
                var stashes = service.ReadStashesAsync(workspaceRoot).GetAwaiter().GetResult();
                var afterPush = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var applyResult = service.StashApplyAsync(workspaceRoot, "stash@{0}").GetAwaiter().GetResult();
                var afterApplyStashes = service.ReadStashesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(pushResult.Success, Is.True);
                Assert.That(stashes.Count, Is.EqualTo(1));
                Assert.That(stashes[0].Subject, Does.Contain("save work"));
                Assert.That(afterPush.Files, Is.Empty);
                Assert.That(applyResult.Success, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt")), Is.EqualTo("changed" + Environment.NewLine));
                Assert.That(File.Exists(Path.Combine(workspaceRoot, "note.txt")), Is.True);
                Assert.That(afterApplyStashes.Count, Is.EqualTo(1));

                RunGit(workspaceRoot, "reset --hard");
                File.Delete(Path.Combine(workspaceRoot, "note.txt"));
                var popResult = service.StashPopAsync(workspaceRoot, "stash@{0}").GetAwaiter().GetResult();
                var afterPopStashes = service.ReadStashesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(popResult.Success, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt")), Is.EqualTo("changed" + Environment.NewLine));
                Assert.That(File.Exists(Path.Combine(workspaceRoot, "note.txt")), Is.True);
                Assert.That(afterPopStashes, Is.Empty);

                RunGit(workspaceRoot, "reset --hard");
                File.Delete(Path.Combine(workspaceRoot, "note.txt"));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "drop me" + Environment.NewLine);
                var secondPushResult = service.StashPushAsync(workspaceRoot, "drop work", false).GetAwaiter().GetResult();
                var dropResult = service.StashDropAsync(workspaceRoot, "stash@{0}").GetAwaiter().GetResult();
                var afterDropStashes = service.ReadStashesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(secondPushResult.Success, Is.True);
                Assert.That(dropResult.Success, Is.True);
                Assert.That(afterDropStashes, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void StashPushPaths_SavesOnlySelectedTrackedFile()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                File.WriteAllText(Path.Combine(workspaceRoot, "other.txt"), "other" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                File.WriteAllText(Path.Combine(workspaceRoot, "other.txt"), "kept" + Environment.NewLine);

                var service = CreateService();
                var result = service.StashPushPathsAsync(workspaceRoot, "selected work", false, new[] { "readme.txt" }).GetAwaiter().GetResult();
                var stashes = service.ReadStashesAsync(workspaceRoot).GetAwaiter().GetResult();
                var files = service.ReadStashFilesAsync(workspaceRoot, "stash@{0}").GetAwaiter().GetResult();
                var diff = service.ReadStashDiffAsync(workspaceRoot, "stash@{0}").GetAwaiter().GetResult();
                var fileDiff = service.ReadStashFileDiffAsync(workspaceRoot, "stash@{0}", "readme.txt").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt")), Is.EqualTo("hello" + Environment.NewLine));
                Assert.That(File.ReadAllText(Path.Combine(workspaceRoot, "other.txt")), Is.EqualTo("kept" + Environment.NewLine));
                Assert.That(stashes.Count, Is.EqualTo(1));
                Assert.That(stashes[0].Subject, Does.Contain("selected work"));
                Assert.That(files.Any(item => item.FilePath == "readme.txt"), Is.True);
                Assert.That(files.Any(item => item.FilePath == "other.txt"), Is.False);
                Assert.That(diff.Success, Is.True);
                Assert.That(diff.StandardOutput, Does.Contain("readme.txt"));
                Assert.That(diff.StandardOutput, Does.Not.Contain("other.txt"));
                Assert.That(fileDiff.Success, Is.True);
                Assert.That(fileDiff.StandardOutput, Does.Contain("readme.txt"));
                Assert.That(fileDiff.StandardOutput, Does.Not.Contain("other.txt"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void StashBranch_CreatesBranchFromSelectedStash()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);

                var service = CreateService();
                service.StashPushAsync(workspaceRoot, "branch work", false).GetAwaiter().GetResult();
                var result = service.StashBranchAsync(workspaceRoot, "stash@{0}", "feature/stash-work").GetAwaiter().GetResult();
                var status = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(status.Branch, Is.EqualTo("feature/stash-work"));
                Assert.That(File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt")), Is.EqualTo("changed" + Environment.NewLine));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void WorktreeOperations_CreateListAndRemove()
        {
            var workspaceRoot = CreateTempDirectory();
            var worktreeRoot = CreateTempDirectory();
            try
            {
                Directory.Delete(worktreeRoot, true);
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");

                var service = CreateService();
                var createResult = service.CreateWorktreeAsync(workspaceRoot, worktreeRoot, "feature/worktree-test", "HEAD").GetAwaiter().GetResult();
                var worktrees = service.ReadWorktreesAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(createResult.Success, Is.True);
                Assert.That(Directory.Exists(worktreeRoot), Is.True);
                Assert.That(worktrees.Any(item => string.Equals(
                    NormalizePathForComparison(item.Path),
                    NormalizePathForComparison(worktreeRoot),
                    StringComparison.OrdinalIgnoreCase)), Is.True);
                var removeResult = service.RemoveWorktreeAsync(workspaceRoot, worktreeRoot, false).GetAwaiter().GetResult();
                Assert.That(removeResult.Success, Is.True);
                Assert.That(Directory.Exists(worktreeRoot), Is.False);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(worktreeRoot);
            }
        }

        [Test]
        public void RevertCommit_CreatesReverseCommit()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");
                var targetCommit = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();

                var service = CreateService();
                var result = service.RevertCommitAsync(workspaceRoot, targetCommit).GetAwaiter().GetResult();
                var content = File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt"));
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 3).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(content, Is.EqualTo("hello" + Environment.NewLine));
                Assert.That(history[0].Subject, Does.Contain("Revert"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void CherryPick_AppliesSelectedCommit()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                var initialBranch = ReadGitOutput(workspaceRoot, "rev-parse --abbrev-ref HEAD").Trim();
                RunGit(workspaceRoot, "checkout -b feature/demo");
                File.WriteAllText(Path.Combine(workspaceRoot, "feature.txt"), "feature" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Feature commit\"");
                var featureCommit = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();
                RunGit(workspaceRoot, "checkout " + initialBranch);

                var service = CreateService();
                var result = service.CherryPickAsync(workspaceRoot, featureCommit).GetAwaiter().GetResult();
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 2).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(workspaceRoot, "feature.txt")), Is.True);
                Assert.That(history[0].Subject, Is.EqualTo("Feature commit"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ResetCommitMixed_RollsBackAndKeepsChangesUnstaged()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                var targetCommit = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");

                var service = CreateService();
                var result = service.ResetCommitAsync(workspaceRoot, targetCommit, GitResetMode.Mixed).GetAwaiter().GetResult();
                var content = File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt"));
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 3).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(content, Is.EqualTo("changed" + Environment.NewLine));
                Assert.That(snapshot.Files.Any(item => item.IsUnstaged && item.FilePath == "readme.txt"), Is.True);
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history[0].Subject, Is.EqualTo("Initial commit"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ResetCommitHard_RollsBackAndRemovesLaterFile()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                var targetCommit = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();
                File.WriteAllText(Path.Combine(workspaceRoot, "later.txt"), "later" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Add later file\"");

                var service = CreateService();
                var result = service.ResetCommitAsync(workspaceRoot, targetCommit, GitResetMode.Hard).GetAwaiter().GetResult();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var history = service.ReadCommitHistoryAsync(workspaceRoot, 3).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(workspaceRoot, "later.txt")), Is.False);
                Assert.That(snapshot.Files, Is.Empty);
                Assert.That(history.Count, Is.EqualTo(1));
                Assert.That(history[0].Subject, Is.EqualTo("Initial commit"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void DiscardUnstaged_RestoresTrackedFile()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);

                var service = CreateService();
                var result = service.DiscardUnstagedAsync(workspaceRoot, new[] { "readme.txt" }).GetAwaiter().GetResult();
                var content = File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt"));
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(content, Is.EqualTo("hello" + Environment.NewLine));
                Assert.That(snapshot.Files, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void DeleteUntracked_RemovesSelectedUntrackedFile()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                var notePath = Path.Combine(workspaceRoot, "note.txt");
                File.WriteAllText(notePath, "note" + Environment.NewLine);

                var service = CreateService();
                var result = service.DeleteUntrackedAsync(workspaceRoot, new[] { "note.txt" }).GetAwaiter().GetResult();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(notePath), Is.False);
                Assert.That(snapshot.Files, Is.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void Push_PushesCurrentBranchToUpstream()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "push -u origin main");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");

                var service = CreateService();
                var beforePush = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var result = service.PushAsync(workspaceRoot).GetAwaiter().GetResult();
                var afterPush = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(beforePush.AheadCount, Is.EqualTo(1));
                Assert.That(result.Success, Is.True);
                Assert.That(afterPush.AheadCount, Is.EqualTo(0));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void Push_WhenRemoteRejects_ReturnsGitErrorOutput()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "push -u origin main");
                var hookPath = Path.Combine(remoteRoot, "hooks", "pre-receive");
                File.WriteAllText(
                    hookPath,
                    "#!/bin/sh\n" +
                    "echo \"remote: simulated credential failure\" >&2\n" +
                    "exit 1\n",
                    new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");

                var service = CreateService();
                var result = service.PushAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.False);
                Assert.That(result.DisplayMessage, Does.Contain("simulated credential failure"));
                Assert.That(result.StandardError, Does.Contain("simulated credential failure"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void PushForceWithLease_RewritesRemoteBranchAfterLocalRollback()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            var cloneRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "push -u origin main");
                var initialCommit = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Update readme\"");
                RunGit(workspaceRoot, "push");
                RunGit(workspaceRoot, "reset --mixed " + initialCommit);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Rewrite readme\"");

                var service = CreateService();
                var normalPush = service.PushAsync(workspaceRoot).GetAwaiter().GetResult();
                var forcePush = service.PushForceWithLeaseAsync(workspaceRoot).GetAwaiter().GetResult();
                RunGit(cloneRoot, "clone " + QuoteArgument(remoteRoot) + " .");
                var remoteSubject = ReadGitOutput(cloneRoot, "log -1 --pretty=%s").Trim();

                Assert.That(normalPush.Success, Is.False);
                Assert.That(forcePush.Success, Is.True);
                Assert.That(remoteSubject, Is.EqualTo("Rewrite readme"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(cloneRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void Pull_PullsRemoteUpdatesFromUpstream()
        {
            var remoteRoot = CreateTempDirectory();
            var workspaceRoot = CreateTempDirectory();
            var otherRoot = CreateTempDirectory();
            try
            {
                RunGit(remoteRoot, "init --bare");
                InitializeTestRepository(workspaceRoot);
                RunGit(workspaceRoot, "branch -M main");
                RunGit(workspaceRoot, "remote add origin " + QuoteArgument(remoteRoot));
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "push -u origin main");

                RunGit(otherRoot, "clone " + QuoteArgument(remoteRoot) + " .");
                RunGit(otherRoot, "checkout main");
                ConfigureTestIdentity(otherRoot);
                File.WriteAllText(Path.Combine(otherRoot, "readme.txt"), "remote" + Environment.NewLine);
                RunGit(otherRoot, "add --all");
                RunGit(otherRoot, "commit -m \"Remote update\"");
                RunGit(otherRoot, "push");

                var service = CreateService();
                service.FetchAsync(workspaceRoot).GetAwaiter().GetResult();
                var beforePull = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var result = service.PullAsync(workspaceRoot).GetAwaiter().GetResult();
                var content = File.ReadAllText(Path.Combine(workspaceRoot, "readme.txt"));
                var afterPull = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(beforePull.BehindCount, Is.EqualTo(1));
                Assert.That(result.Success, Is.True);
                Assert.That(content, Is.EqualTo("remote" + Environment.NewLine));
                Assert.That(afterPull.BehindCount, Is.EqualTo(0));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
                DeleteDirectory(otherRoot);
                DeleteDirectory(remoteRoot);
            }
        }

        [Test]
        public void ReadStatus_RealDetachedHeadReportsDetachedBranch()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                var head = ReadGitOutput(workspaceRoot, "rev-parse HEAD").Trim();
                RunGit(workspaceRoot, "checkout --detach " + head);

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.Branch, Does.StartWith("detached"));
                Assert.That(snapshot.Upstream, Is.Null.Or.Empty);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatus_ReflectsExternalWorkspaceChangesAfterRefresh()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");

                var service = CreateService();
                var beforeExternalChange = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "changed outside MyCodex" + Environment.NewLine);
                var afterExternalChange = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(beforeExternalChange.Files, Is.Empty);
                Assert.That(afterExternalChange.Files.Any(item => item.IsUnstaged && item.FilePath == "readme.txt"), Is.True);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void GitCommandRunner_WhenGitIsNotOnPath_UsesPreparedBundledGitOrReportsMissingRuntime()
        {
            var workspaceRoot = CreateTempDirectory();
            var path = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", workspaceRoot);

                var runner = new MyCodex.Infrastructure.Git.GitCommandRunner();

                if (Path.IsPathRooted(MyCodex.Infrastructure.Git.BundledGitRuntime.ResolveExecutable()))
                {
                    var result = runner.Run(workspaceRoot, "--version");
                    Assert.That(result.Success, Is.True);
                    Assert.That(result.StandardOutput, Does.StartWith("git version "));
                }
                else
                {
                    Assert.Throws<Win32Exception>(() => runner.Run(workspaceRoot, "--version"));
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", path);
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadConflictDetails_ReturnsCombinedDiffForConflictedFile()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "base" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "checkout -b feature");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "feature" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Feature update\"");
                RunGit(workspaceRoot, "checkout main");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "main" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Main update\"");
                var mergeResult = RunGitAllowFailure(workspaceRoot, "merge feature");
                Assert.That(mergeResult, Is.Not.EqualTo(0));

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var entry = snapshot.Files.FirstOrDefault(item => item.IsConflicted && item.FilePath == "readme.txt");
                var result = service.ReadConflictDetailsAsync(workspaceRoot, entry).GetAwaiter().GetResult();

                Assert.That(entry, Is.Not.Null);
                Assert.That(result.Success, Is.True);
                Assert.That(result.StandardOutput, Does.Contain("diff --cc"));
                Assert.That(result.StandardOutput, Does.Contain("readme.txt"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatus_PreservesUtf8AndSpacesInPath()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                RunGit(workspaceRoot, "init");
                RunGit(workspaceRoot, "config core.quotepath true");
                var relativePath = Path.Combine("docs", "\u4E2D\u6587 \u7A7A\u683C.txt");
                Directory.CreateDirectory(Path.Combine(workspaceRoot, "docs"));
                File.WriteAllText(Path.Combine(workspaceRoot, relativePath), "hello");

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();

                Assert.That(snapshot.Files.Any(item =>
                    item.IsUntracked &&
                    string.Equals(item.FilePath, relativePath, StringComparison.OrdinalIgnoreCase)), Is.True);
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatus_ParsesRenamedPath()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "old.txt"), "hello" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                RunGit(workspaceRoot, "mv old.txt new.txt");

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var entry = snapshot.Files.FirstOrDefault(item => item.IsStaged && item.ChangeKind == "renamed");

                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.FilePath, Is.EqualTo("new.txt"));
                Assert.That(entry.OriginalFilePath, Is.EqualTo("old.txt"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadStatus_PopulatesLineStatsWithoutLoadingDiff()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "one" + Environment.NewLine + "two" + Environment.NewLine);
                RunGit(workspaceRoot, "add --all");
                RunGit(workspaceRoot, "commit -m \"Initial commit\"");
                File.WriteAllText(Path.Combine(workspaceRoot, "readme.txt"), "one" + Environment.NewLine + "three" + Environment.NewLine + "four" + Environment.NewLine);

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var entry = snapshot.Files.FirstOrDefault(item => item.IsUnstaged && item.FilePath == "readme.txt");

                Assert.That(entry, Is.Not.Null);
                Assert.That(entry.DiffText, Is.Null.Or.Empty);
                Assert.That(entry.HasLineStats, Is.True);
                Assert.That(entry.AddedLineCount, Is.EqualTo(2));
                Assert.That(entry.DeletedLineCount, Is.EqualTo(1));

                var diffResult = service.ReadDiffAsync(workspaceRoot, entry).GetAwaiter().GetResult();

                Assert.That(diffResult.Success, Is.True);
                Assert.That(diffResult.StandardOutput, Does.Contain("+three"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void PopulateDiffLineCounts_AssignsCountsAndPreservesLineStatsSemantics()
        {
            var entry = new GitChangedFileEntry
            {
                DiffText = "--- a/file\r+++ b/file\r@@ -1 +1 @@\r-old\r+new\r",
                AddedLineCount = 20,
                DeletedLineCount = 30
            };

            GitRepositoryService.PopulateDiffLineCounts(entry);
            GitRepositoryService.PopulateDiffLineCounts(entry);

            Assert.That(entry.AddedLineCount, Is.EqualTo(1));
            Assert.That(entry.DeletedLineCount, Is.EqualTo(1));
            Assert.That(entry.HasLineStats, Is.True);

            entry.DiffText = string.Empty;
            GitRepositoryService.PopulateDiffLineCounts(entry);

            Assert.That(entry.AddedLineCount, Is.Zero);
            Assert.That(entry.DeletedLineCount, Is.Zero);
            Assert.That(entry.HasLineStats, Is.True);
        }

        [Test]
        public void RuntimeGitService_ReadStatusPopulatesLineStatsForStagedAndUnstagedEntries()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "M  staged.txt\0 M unstaged.txt\0",
                StagedNumstatOutput = "2\t1\tstaged.txt\0bad\t1\tstaged.txt\0",
                UnstagedNumstatOutput = "3\t4\tunstaged.txt\0-\t-\tbinary.bin\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            var staged = snapshot.Files.FirstOrDefault(item => item.FilePath == "staged.txt");
            var unstaged = snapshot.Files.FirstOrDefault(item => item.FilePath == "unstaged.txt");

            Assert.That(staged, Is.Not.Null);
            Assert.That(staged.IsStaged, Is.True);
            Assert.That(staged.HasLineStats, Is.True);
            Assert.That(staged.AddedLineCount, Is.EqualTo(2));
            Assert.That(staged.DeletedLineCount, Is.EqualTo(1));

            Assert.That(unstaged, Is.Not.Null);
            Assert.That(unstaged.IsUnstaged, Is.True);
            Assert.That(unstaged.HasLineStats, Is.True);
            Assert.That(unstaged.AddedLineCount, Is.EqualTo(3));
            Assert.That(unstaged.DeletedLineCount, Is.EqualTo(4));
        }

        [Test]
        public void RuntimeGitService_ReadStatusAppliesLineStatsToAllMatchingDuplicateEntries()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "MM src/app.cs\0",
                StagedNumstatOutput = "5\t1\tsrc/app.cs\0",
                UnstagedNumstatOutput = "2\t3\tsrc/app.cs\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            var entry = snapshot.Files.FirstOrDefault(item => item.FilePath == "src/app.cs");

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.HasStagedChanges, Is.True);
            Assert.That(entry.HasUnstagedChanges, Is.True);
            Assert.That(entry.HasLineStats, Is.True);
            Assert.That(entry.AddedLineCount, Is.EqualTo(7));
            Assert.That(entry.DeletedLineCount, Is.EqualTo(4));
        }

        [Test]
        public void RuntimeGitService_ReadStatusUsesOneNumstatCommandPerStagedState()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = " M a.txt\0 M b.txt\0M  c.txt\0",
                UnstagedNumstatOutput = "1\t0\ta.txt\02\t0\tb.txt\0",
                StagedNumstatOutput = "3\t0\tc.txt\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            var numstatCommands = runner.Commands
                .Where(arguments => arguments.Count > 0 && arguments[0] == "diff" && arguments.Contains("--numstat"))
                .ToList();

            Assert.That(numstatCommands, Has.Count.EqualTo(2));
            Assert.That(numstatCommands.Count(arguments => arguments.Contains("--cached")), Is.EqualTo(1));
            Assert.That(numstatCommands.Count(arguments => !arguments.Contains("--cached")), Is.EqualTo(1));
        }

        [Test]
        public void ReadDiff_ForUntrackedTextFile_ReturnsNoIndexDiff()
        {
            var workspaceRoot = CreateTempDirectory();
            try
            {
                InitializeTestRepository(workspaceRoot);
                File.WriteAllText(Path.Combine(workspaceRoot, "note.txt"), "hello" + Environment.NewLine);

                var service = CreateService();
                var snapshot = service.ReadStatusAsync(workspaceRoot).GetAwaiter().GetResult();
                var entry = snapshot.Files.FirstOrDefault(item => item.IsUntracked && item.FilePath == "note.txt");
                var diffResult = service.ReadDiffAsync(workspaceRoot, entry).GetAwaiter().GetResult();

                Assert.That(entry, Is.Not.Null);
                Assert.That(diffResult.Success, Is.True);
                Assert.That(diffResult.StandardOutput, Does.Contain("new file mode"));
                Assert.That(diffResult.StandardOutput, Does.Contain("--- /dev/null"));
                Assert.That(diffResult.StandardOutput, Does.Contain("+hello"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void RuntimeGitService_PreservesUnixPathsAndPassesPathAsArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();
            var stageResult = service.StageAsync("/home/user/repo", new[] { "src/app file.txt" }).GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Files.Any(item =>
                item.IsUntracked &&
                string.Equals(item.FilePath, "src/app file.txt", StringComparison.Ordinal)), Is.True);
            Assert.That(stageResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "add", "--", "src/app file.txt" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadUntrackedDiffUsesNoIndexDiff()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);
            var entry = new GitChangedFileEntry
            {
                FilePath = "src/app file.txt",
                IsUntracked = true
            };

            var result = service.ReadDiffAsync("/home/user/repo", entry).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("+hello"));
            CollectionAssert.AreEqual(
                new[] { "diff", "--no-index", "--", "/dev/null", "src/app file.txt" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_DeleteUntrackedPassesPathAsArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.DeleteUntrackedAsync("/home/user/repo", new[] { "src/app file.txt" }).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "clean", "-f", "--", "src/app file.txt" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStatusUsesSingleLightweightStatusCommand()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## main...origin/main [ahead 2, behind 1]\0 M src/app.cs\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true, false, false);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Branch, Is.EqualTo("main"));
            Assert.That(snapshot.Upstream, Is.EqualTo("origin/main"));
            Assert.That(snapshot.AheadCount, Is.EqualTo(2));
            Assert.That(snapshot.BehindCount, Is.EqualTo(1));
            Assert.That(snapshot.Files.Any(item =>
                item.IsUnstaged &&
                string.Equals(item.FilePath, "src/app.cs", StringComparison.Ordinal)), Is.True);
            Assert.That(runner.CallCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all", "--no-renames" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStatusParsesDetachedHead()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## HEAD detached at abc1234\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true, false);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Branch, Is.EqualTo("detached@abc1234"));
        }

        [Test]
        public void RuntimeGitService_ReadStatusPopulatesWorkbenchCounters()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## main...origin/main [ahead 2, behind 1]\0M  staged.txt\0 M unstaged.txt\0?? note.txt\0UU conflict.txt\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true, false);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.Branch, Is.EqualTo("main"));
            Assert.That(snapshot.Upstream, Is.EqualTo("origin/main"));
            Assert.That(snapshot.AheadCount, Is.EqualTo(2));
            Assert.That(snapshot.BehindCount, Is.EqualTo(1));
            Assert.That(snapshot.StagedCount, Is.EqualTo(1));
            Assert.That(snapshot.UnstagedCount, Is.EqualTo(1));
            Assert.That(snapshot.UntrackedCount, Is.EqualTo(1));
            Assert.That(snapshot.ConflictedCount, Is.EqualTo(1));
            Assert.That(snapshot.HasConflicts, Is.True);
            Assert.That(snapshot.IsDetachedHead, Is.False);
            Assert.That(snapshot.Files.Single(item => item.FilePath == "staged.txt").HasStagedChanges, Is.True);
            Assert.That(snapshot.Files.Single(item => item.FilePath == "unstaged.txt").HasUnstagedChanges, Is.True);
        }

        [Test]
        public void RuntimeGitService_ReadStatusPopulatesStashCountWhenExtendedDetailsAreEnabled()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## main\0"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.StashCount, Is.EqualTo(1));
        }

        [Test]
        public void UnixGitRepositoryServiceFactory_ForWslReadStatusUsesExtendedRuntimeDetails()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## main...origin/main [ahead 2, behind 1]\0",
                RemoteUrl = "https://github.com/example/repo.git",
                CherryPickInProgress = true
            };
            var service = UnixGitRepositoryServiceFactory.Create(runner);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.OriginUrl, Is.EqualTo("https://github.com/example/repo.git"));
            Assert.That(snapshot.Upstream, Is.EqualTo("origin/main"));
            Assert.That(snapshot.AheadCount, Is.EqualTo(2));
            Assert.That(snapshot.BehindCount, Is.EqualTo(1));
            Assert.That(snapshot.StashCount, Is.EqualTo(1));
            Assert.That(snapshot.IsCherryPickInProgress, Is.True);
            Assert.That(runner.CallCount, Is.GreaterThan(1));
        }

        [Test]
        public void UnixGitRepositoryServiceFactory_ForRemoteMacReadStatusUsesExtendedRuntimeDetails()
        {
            var runner = new FakeGitCommandRunner
            {
                StatusOutput = "## main...origin/main [behind 3]\0",
                RemoteUrl = "git@github.com:example/repo.git",
                RevertInProgress = true
            };
            var service = UnixGitRepositoryServiceFactory.Create(runner);

            var snapshot = service.ReadStatusAsync("/Users/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.OriginUrl, Is.EqualTo("git@github.com:example/repo.git"));
            Assert.That(snapshot.Upstream, Is.EqualTo("origin/main"));
            Assert.That(snapshot.BehindCount, Is.EqualTo(3));
            Assert.That(snapshot.StashCount, Is.EqualTo(1));
            Assert.That(snapshot.IsRevertInProgress, Is.True);
            Assert.That(runner.CallCount, Is.GreaterThan(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void UnixGitRepositoryServiceFactory_ReadStatusRejectsMissingWorkspacePath(string workspacePath)
        {
            var service = UnixGitRepositoryServiceFactory.Create(new FakeGitCommandRunner());

            var exception = Assert.Throws<ArgumentException>(() => service.ReadStatusAsync(workspacePath).GetAwaiter().GetResult());

            var expectedMessage = SharedLocalization.Text("GitRepositoryService.Error.WorkspacePathRequired", "Workspace path cannot be empty.");
            Assert.That(exception.ParamName, Is.EqualTo("workspacePath"));
            Assert.That(exception.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void Fetch_UsesPruneFetchCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.FetchAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "fetch", "--prune" }, runner.LastArguments);
        }

        [Test]
        public void CheckoutPullRequest_FetchesPrHeadThenSwitchesLocalBranch()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CheckoutPullRequestAsync("/home/user/repo", "origin", 42, "pr-42")
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(runner.Commands.Count, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { "fetch", "origin", "pull/42/head:pr-42" }, runner.Commands[0]);
            CollectionAssert.AreEqual(new[] { "switch", "pr-42" }, runner.Commands[1]);
        }

        [Test]
        public void MarkConflictsResolved_UsesGitAddForSelectedPath()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.MarkConflictsResolvedAsync("/home/user/repo", new[] { "conflict.txt" }).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "add", "--", "conflict.txt" }, runner.LastArguments);
        }

        [Test]
        public void Pull_UsesPullCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.PullAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "pull" }, runner.LastArguments);
        }

        [Test]
        public void PushSetUpstream_UsesOriginAndBranch()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.PushSetUpstreamAsync("/home/user/repo", "feature/demo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "push", "-u", "origin", "feature/demo" }, runner.LastArguments);
        }

        [Test]
        public void AddRemote_UsesRemoteAddCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.AddRemoteAsync("/home/user/repo", "origin", "https://github.com/example/repo.git").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "remote", "add", "origin", "https://github.com/example/repo.git" }, runner.LastArguments);
        }

        [Test]
        public void PushForceWithLease_UsesLeaseArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.PushForceWithLeaseAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "push", "--force-with-lease" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadCommitHistoryUsesBoundedLogCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var history = service.ReadCommitHistoryAsync("/home/user/repo", 500).GetAwaiter().GetResult();

            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Subject, Is.EqualTo("Initial commit"));
            CollectionAssert.AreEqual(
                new[]
                {
                    "log",
                    "--date=iso-strict",
                    "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1e",
                    "-n",
                    "200"
                },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadCommitHistoryWithRevisionPassesRevision()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var history = service.ReadCommitHistoryAsync("/home/user/repo", "feature/demo", 500).GetAwaiter().GetResult();

            Assert.That(history.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(
                new[]
                {
                    "log",
                    "feature/demo",
                    "--date=iso-strict",
                    "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1e",
                    "-n",
                    "200"
                },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadCommitHistoryReturnsEmptyForRepositoryWithoutCommits()
        {
            var runner = new FakeGitCommandRunner
            {
                LogExitCode = 128,
                LogError = "fatal: your current branch 'main' does not have any commits yet"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var history = service.ReadCommitHistoryAsync("/home/user/repo", 10).GetAwaiter().GetResult();

            Assert.That(history, Is.Empty);
        }

        [Test]
        public void RuntimeGitService_ReadCommitHistoryThrowsWhenGitLogFails()
        {
            var runner = new FakeGitCommandRunner
            {
                LogExitCode = 128,
                LogError = "fatal: bad revision"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                service.ReadCommitHistoryAsync("/home/user/repo", "missing/ref", 10).GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain(SharedLocalization.Text("GitRepositoryService.Error.ReadCommitHistoryFailed", "Failed to read Git commit history.")));
            Assert.That(ex.Message, Does.Contain("fatal: bad revision"));
        }

        [Test]
        public void RuntimeGitService_ReadCommitMessageParsesSubjectAndBody()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var message = service.ReadCommitMessageAsync("/home/user/repo", "HEAD").GetAwaiter().GetResult();

            Assert.That(message.Subject, Is.EqualTo("Previous subject"));
            Assert.That(message.Body, Is.EqualTo("Previous body line 1" + Environment.NewLine + "Previous body line 2"));
            CollectionAssert.AreEqual(new[] { "log", "-1", "--format=%B", "HEAD" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadBranchesUsesFormattedBranchCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var branches = service.ReadBranchesAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(branches.Count, Is.EqualTo(2));
            Assert.That(branches[0].Name, Is.EqualTo("main"));
            Assert.That(branches[0].IsCurrent, Is.True);
            Assert.That(branches[0].Upstream, Is.EqualTo("origin/main"));
            Assert.That(branches[0].AheadCount, Is.EqualTo(1));
            Assert.That(branches[0].BehindCount, Is.Null);
            Assert.That(branches[1].Name, Is.EqualTo("origin/feature/demo"));
            Assert.That(branches[1].IsRemote, Is.True);
            CollectionAssert.AreEqual(
                new[] { "for-each-ref", "refs/heads", "refs/remotes", "--format=%(refname:short)%09%(HEAD)%09%(upstream:short)%09%(upstream:track)%09%(refname)" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadBranchesThrowsWhenGitCommandFails()
        {
            var runner = new FakeGitCommandRunner
            {
                BranchExitCode = 128,
                BranchError = "fatal: branch refs unavailable"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                service.ReadBranchesAsync("/home/user/repo").GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain(SharedLocalization.Text("GitRepositoryService.Error.ReadBranchesFailed", "Failed to read Git branches.")));
            Assert.That(ex.Message, Does.Contain("fatal: branch refs unavailable"));
        }

        [Test]
        public void RuntimeGitService_ReadStatusDetectsMergeAndRebaseInProgress()
        {
            var runner = new FakeGitCommandRunner { MergeInProgress = true, RebaseInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsMergeInProgress, Is.True);
            Assert.That(snapshot.IsRebaseInProgress, Is.True);
            Assert.That(snapshot.HasMergeOrRebaseOperation, Is.True);
            Assert.That(snapshot.HasSequencerOperation, Is.False);
        }

        [Test]
        public void RuntimeGitService_ReadStatusDetectsCherryPickInProgress()
        {
            var runner = new FakeGitCommandRunner { CherryPickInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var snapshot = service.ReadStatusAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsCherryPickInProgress, Is.True);
            Assert.That(snapshot.HasSequencerOperation, Is.True);
        }

        [Test]
        public void RuntimeGitService_ContinueSequencerRunsCherryPickContinue()
        {
            var runner = new FakeGitCommandRunner { CherryPickInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.ContinueSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "cherry-pick", "--continue" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_AbortSequencerRunsRevertAbort()
        {
            var runner = new FakeGitCommandRunner { RevertInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.AbortSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "revert", "--abort" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RevertCommitsUsesSingleGitRevertCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.RevertCommitsAsync("/home/user/repo", new[] { "bbb222", "aaa111" }).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "revert", "--no-edit", "bbb222", "aaa111" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_SkipSequencerFailsWithoutActiveOperation()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.SkipSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(result.DisplayMessage, Does.Contain("拣选"));
        }

        [Test]
        public void RuntimeGitService_MergeBranchIntoCurrentUsesNoFastForwardMerge()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.MergeBranchIntoCurrentAsync("/home/user/repo", "feature/demo", false).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "merge", "--no-ff", "feature/demo" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_MergeBranchIntoCurrentReportsNoChangesWhenAlreadyUpToDate()
        {
            var runner = new FakeGitCommandRunner { MergeOutput = "Already up to date.\n" };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.MergeBranchIntoCurrentAsync("/home/user/repo", "feature/demo", false).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.DisplayMessage, Does.Contain("无需再次合并"));
        }

        [Test]
        public void RuntimeGitService_MergeBranchIntoCurrentSquashUsesSquashMerge()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.MergeBranchIntoCurrentAsync("/home/user/repo", "feature/demo", true).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "merge", "--squash", "feature/demo" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RebaseCurrentBranchOntoUsesRebaseCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.RebaseCurrentBranchOntoAsync("/home/user/repo", "main").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "rebase", "main" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RebaseCurrentBranchOntoReportsNoChangesWhenAlreadyUpToDate()
        {
            var runner = new FakeGitCommandRunner { RebaseOutput = "Current branch test is up to date.\n" };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.RebaseCurrentBranchOntoAsync("/home/user/repo", "main").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.DisplayMessage, Does.Contain("无需再次变基"));
        }

        [Test]
        public void RuntimeGitService_ContinueSequencerRunsMergeContinue()
        {
            var runner = new FakeGitCommandRunner { MergeInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.ContinueSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "merge", "--continue" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_AbortSequencerRunsRebaseAbort()
        {
            var runner = new FakeGitCommandRunner { RebaseInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.AbortSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "rebase", "--abort" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_SkipSequencerFailsForMergeOperation()
        {
            var runner = new FakeGitCommandRunner { MergeInProgress = true };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.SkipSequencerAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            CollectionAssert.AreEqual(new[] { "rev-parse", "-q", "--verify", "MERGE_HEAD" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStashesUsesFormattedStashCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var stashes = service.ReadStashesAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(stashes.Count, Is.EqualTo(1));
            Assert.That(stashes[0].RefName, Is.EqualTo("stash@{0}"));
            Assert.That(stashes[0].Subject, Does.Contain("demo"));
            CollectionAssert.AreEqual(
                new[] { "stash", "list", "--format=%gd%x1f%gs%x1e" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStashesThrowsWhenGitCommandFails()
        {
            var runner = new FakeGitCommandRunner
            {
                StashListExitCode = 128,
                StashListError = "fatal: stash unavailable"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                service.ReadStashesAsync("/home/user/repo").GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain(SharedLocalization.Text("GitRepositoryService.Error.ReadStashesFailed", "Failed to read Git stashes.")));
            Assert.That(ex.Message, Does.Contain("fatal: stash unavailable"));
        }

        [Test]
        public void RuntimeGitService_ReadStashFilesUsesNameStatusCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var files = service.ReadStashFilesAsync("/home/user/repo", "stash@{0}").GetAwaiter().GetResult();

            Assert.That(files.Count, Is.EqualTo(1));
            Assert.That(files[0].FilePath, Is.EqualTo("src/app.cs"));
            Assert.That(files[0].ChangeKind, Is.EqualTo("modified"));
            CollectionAssert.AreEqual(
                new[] { "stash", "show", "--name-status", "-z", "stash@{0}" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStashDiffUsesPatchCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.ReadStashDiffAsync("/home/user/repo", "stash@{0}").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("diff --git"));
            CollectionAssert.AreEqual(
                new[] { "stash", "show", "--patch", "--no-ext-diff", "--binary", "stash@{0}" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadStashFileDiffUsesDiffPathCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.ReadStashFileDiffAsync("/home/user/repo", "stash@{0}", "src/app.cs").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(
                new[] { "diff", "--no-ext-diff", "--binary", "stash@{0}^1", "stash@{0}", "--", "src/app.cs" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RepositoryDiagnosticsReturnsStructuredItems()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var diagnostics = service.RunRepositoryDiagnosticsAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(diagnostics.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(diagnostics.Any(item => item.Title.Contains("Git") || item.Title.Contains("Git")), Is.True);
            Assert.That(diagnostics.Any(item => item.Title.Contains("Repository") || item.Title.Contains("仓库")), Is.True);
            Assert.That(diagnostics.Any(item => item.Title.Contains("Lock") || item.Title.Contains("锁")), Is.True);
            Assert.That(diagnostics.Any(item => item.Title.Contains("Working") || item.Title.Contains("工作树")), Is.True);
            Assert.That(diagnostics.Any(item => item.Title.Contains("Object") || item.Title.Contains("对象")), Is.True);
            Assert.That(diagnostics.Any(item => item.Detail.Contains("Staged") || item.Detail.Contains("已暂存")), Is.True);
        }

        [Test]
        public void RuntimeGitService_ReadWorktreesUsesPorcelainCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var worktrees = service.ReadWorktreesAsync("/home/user/repo").GetAwaiter().GetResult();

            Assert.That(worktrees.Count, Is.EqualTo(2));
            Assert.That(worktrees[0].Path, Is.EqualTo("/home/user/repo"));
            Assert.That(worktrees[0].Branch, Is.EqualTo("main"));
            Assert.That(worktrees[1].Path, Is.EqualTo("/home/user/repo-feature"));
            Assert.That(worktrees[1].IsPrunable, Is.True);
            CollectionAssert.AreEqual(new[] { "worktree", "list", "--porcelain" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadWorktreesThrowsWhenGitCommandFails()
        {
            var runner = new FakeGitCommandRunner
            {
                WorktreeListExitCode = 128,
                WorktreeListError = "fatal: worktree unavailable"
            };
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                service.ReadWorktreesAsync("/home/user/repo").GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain(SharedLocalization.Text("GitRepositoryService.Error.ReadWorktreesFailed", "Failed to read Git worktrees.")));
            Assert.That(ex.Message, Does.Contain("fatal: worktree unavailable"));
        }

        [Test]
        public void RuntimeGitService_ReadConflictDetailsUsesCombinedDiffCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);
            var entry = new GitChangedFileEntry
            {
                FilePath = "src/app.cs",
                IsConflicted = true
            };

            var result = service.ReadConflictDetailsAsync("/home/user/repo", entry).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("diff --cc"));
            CollectionAssert.AreEqual(
                new[] { "diff", "--cc", "--no-ext-diff", "--", "src/app.cs" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadCommitFilesUsesNameStatusShowCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var files = service.ReadCommitFilesAsync("/home/user/repo", "abcdef1").GetAwaiter().GetResult();

            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].FilePath, Is.EqualTo("src/app.cs"));
            Assert.That(files[0].ChangeKind, Is.EqualTo("modified"));
            Assert.That(files[1].OriginalFilePath, Is.EqualTo("old.txt"));
            Assert.That(files[1].FilePath, Is.EqualTo("new.txt"));
            Assert.That(files[1].ChangeKind, Is.EqualTo("renamed"));
            CollectionAssert.AreEqual(
                new[] { "show", "--name-status", "--format=", "-z", "--find-renames", "abcdef1" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadCommitDiffUsesShowCommand()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.ReadCommitDiffAsync("/home/user/repo", "abcdef1").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("diff --git"));
            CollectionAssert.AreEqual(
                new[] { "show", "--format=", "--no-ext-diff", "--binary", "abcdef1" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadDiffCanLoadStagedDiffExplicitly()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);
            var entry = new GitChangedFileEntry
            {
                FilePath = "src/app.cs",
                HasStagedChanges = true
            };

            var result = service.ReadDiffAsync("/home/user/repo", entry, GitDiffKind.Staged).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(
                new[] { "diff", "--cached", "--no-ext-diff", "--binary", "--", "src/app.cs" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ReadDiffCanLoadUnstagedDiffExplicitly()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);
            var entry = new GitChangedFileEntry
            {
                FilePath = "src/app.cs",
                HasUnstagedChanges = true
            };

            var result = service.ReadDiffAsync("/home/user/repo", entry, GitDiffKind.Unstaged).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(
                new[] { "diff", "--no-ext-diff", "--binary", "--", "src/app.cs" },
                runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_BranchOperationsPassBranchAsArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var checkoutResult = service.CheckoutBranchAsync("/home/user/repo", "feature/demo").GetAwaiter().GetResult();
            Assert.That(checkoutResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "switch", "feature/demo" }, runner.LastArguments);

            var checkoutRemoteResult = service.CheckoutRemoteBranchAsync("/home/user/repo", "origin/feature/demo").GetAwaiter().GetResult();
            Assert.That(checkoutRemoteResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "switch", "--track", "origin/feature/demo" }, runner.LastArguments);

            var createResult = service.CreateBranchAsync("/home/user/repo", "feature/new").GetAwaiter().GetResult();
            Assert.That(createResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "switch", "-c", "feature/new" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RevertCommitPassesCommitAsArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.RevertCommitAsync("/home/user/repo", "abcdef1").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "revert", "--no-edit", "abcdef1" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_CherryPickPassesCommitAsArgument()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CherryPickAsync("/home/user/repo", "abcdef1").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "cherry-pick", "abcdef1" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_CommitAmendUsesNoEditWhenMessageIsEmpty()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CommitAmendAsync("/home/user/repo", string.Empty).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "commit", "--amend", "--no-edit" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_CommitAmendUsesMessageWhenProvided()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CommitAmendAsync("/home/user/repo", "Rewrite last commit").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "commit", "--amend", "-m", "Rewrite last commit" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_CommitUsesSubjectAndBodyArguments()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CommitAsync("/home/user/repo", "Subject", "Body").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "commit", "-m", "Subject", "-m", "Body" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_CommitAmendUsesSubjectAndBodyArguments()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var result = service.CommitAmendAsync("/home/user/repo", "Subject", "Body").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "commit", "--amend", "-m", "Subject", "-m", "Body" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_StashOperationsPassArguments()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var pushResult = service.StashPushAsync("/home/user/repo", "save work", true).GetAwaiter().GetResult();
            Assert.That(pushResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "push", "--include-untracked", "-m", "save work" }, runner.LastArguments);

            var pushPathsResult = service.StashPushPathsAsync("/home/user/repo", "selected work", false, new[] { "src/app file.txt" }).GetAwaiter().GetResult();
            Assert.That(pushPathsResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "push", "-m", "selected work", "--", "src/app file.txt" }, runner.LastArguments);

            var branchResult = service.StashBranchAsync("/home/user/repo", "stash@{0}", "feature/stash").GetAwaiter().GetResult();
            Assert.That(branchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "branch", "feature/stash", "stash@{0}" }, runner.LastArguments);

            var createWorktreeResult = service.CreateWorktreeAsync("/home/user/repo", "/home/user/repo-feature", "feature/worktree", "HEAD").GetAwaiter().GetResult();
            Assert.That(createWorktreeResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "worktree", "add", "-b", "feature/worktree", "/home/user/repo-feature", "HEAD" }, runner.LastArguments);

            var removeWorktreeResult = service.RemoveWorktreeAsync("/home/user/repo", "/home/user/repo-feature", false).GetAwaiter().GetResult();
            Assert.That(removeWorktreeResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "worktree", "remove", "/home/user/repo-feature" }, runner.LastArguments);

            var pruneWorktreeResult = service.PruneWorktreesAsync("/home/user/repo").GetAwaiter().GetResult();
            Assert.That(pruneWorktreeResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "worktree", "prune" }, runner.LastArguments);

            var integrityCheckResult = service.RunRepositoryIntegrityCheckAsync("/home/user/repo").GetAwaiter().GetResult();
            Assert.That(integrityCheckResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "fsck", "--full" }, runner.LastArguments);

            var maintenanceResult = service.RunRepositoryMaintenanceAsync("/home/user/repo").GetAwaiter().GetResult();
            Assert.That(maintenanceResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "maintenance", "run" }, runner.LastArguments);

            var applyResult = service.StashApplyAsync("/home/user/repo", "stash@{0}").GetAwaiter().GetResult();
            Assert.That(applyResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "apply", "stash@{0}" }, runner.LastArguments);

            var popResult = service.StashPopAsync("/home/user/repo", "stash@{0}").GetAwaiter().GetResult();
            Assert.That(popResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "pop", "stash@{0}" }, runner.LastArguments);

            var dropResult = service.StashDropAsync("/home/user/repo", "stash@{0}").GetAwaiter().GetResult();
            Assert.That(dropResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "stash", "drop", "stash@{0}" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_RemoteAndUpstreamOperationsPassArguments()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);
            var compareBranches = service.CompareBranchesAsync("/home/user/repo", "HEAD", "feature/demo").GetAwaiter().GetResult();
            Assert.That(compareBranches.CurrentOnlyCount, Is.EqualTo(2));
            Assert.That(compareBranches.TargetOnlyCount, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { "rev-list", "--left-right", "--count", "HEAD...feature/demo" }, runner.LastArguments);


            var remotes = service.ReadRemotesAsync("/home/user/repo").GetAwaiter().GetResult();
            var origin = remotes.FirstOrDefault(item => item.Name == "origin");
            Assert.That(remotes.Count, Is.EqualTo(2));
            Assert.That(origin, Is.Not.Null);
            Assert.That(origin.FetchUrl, Is.EqualTo("https://example.com/fetch.git"));
            Assert.That(origin.PushUrl, Is.EqualTo("https://example.com/push.git"));

            var setUrlResult = service.SetRemoteUrlAsync("/home/user/repo", "origin", "https://example.com/new.git").GetAwaiter().GetResult();
            Assert.That(setUrlResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "remote", "set-url", "origin", "https://example.com/new.git" }, runner.LastArguments);

            var setPushUrlResult = service.SetRemotePushUrlAsync("/home/user/repo", "origin", "https://example.com/push-new.git").GetAwaiter().GetResult();
            Assert.That(setPushUrlResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "remote", "set-url", "--push", "origin", "https://example.com/push-new.git" }, runner.LastArguments);

            var removeResult = service.RemoveRemoteAsync("/home/user/repo", "origin").GetAwaiter().GetResult();
            Assert.That(removeResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "remote", "remove", "origin" }, runner.LastArguments);

            var setUpstreamResult = service.SetUpstreamAsync("/home/user/repo", "main", "origin/main").GetAwaiter().GetResult();
            Assert.That(setUpstreamResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "--set-upstream-to", "origin/main", "main" }, runner.LastArguments);

            var unsetUpstreamResult = service.UnsetUpstreamAsync("/home/user/repo", "main").GetAwaiter().GetResult();
            Assert.That(unsetUpstreamResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "--unset-upstream", "main" }, runner.LastArguments);

            var renameCurrentBranchResult = service.RenameCurrentBranchAsync("/home/user/repo", "feature/renamed").GetAwaiter().GetResult();
            Assert.That(renameCurrentBranchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "-m", "feature/renamed" }, runner.LastArguments);

            var renameBranchResult = service.RenameBranchAsync("/home/user/repo", "feature/old", "feature/new").GetAwaiter().GetResult();
            Assert.That(renameBranchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "-m", "feature/old", "feature/new" }, runner.LastArguments);

            var deleteBranchResult = service.DeleteBranchAsync("/home/user/repo", "feature/delete-me").GetAwaiter().GetResult();
            Assert.That(deleteBranchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "-d", "feature/delete-me" }, runner.LastArguments);

            var forceDeleteBranchResult = service.ForceDeleteBranchAsync("/home/user/repo", "feature/delete-me").GetAwaiter().GetResult();
            Assert.That(forceDeleteBranchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "branch", "-D", "feature/delete-me" }, runner.LastArguments);

            var deleteRemoteBranchResult = service.DeleteRemoteBranchAsync("/home/user/repo", "origin", "feature/delete-me").GetAwaiter().GetResult();
            Assert.That(deleteRemoteBranchResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "push", "origin", "--delete", "feature/delete-me" }, runner.LastArguments);
        }

        [Test]
        public void RuntimeGitService_ResetCommitPassesModeAndCommitAsArguments()
        {
            var runner = new FakeGitCommandRunner();
            var service = new GitRepositoryService(runner, path => path.Trim(), true);

            var softResult = service.ResetCommitAsync("/home/user/repo", "abcdef1", GitResetMode.Soft).GetAwaiter().GetResult();
            Assert.That(softResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "reset", "--soft", "abcdef1" }, runner.LastArguments);

            var mixedResult = service.ResetCommitAsync("/home/user/repo", "abcdef1", GitResetMode.Mixed).GetAwaiter().GetResult();
            Assert.That(mixedResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "reset", "--mixed", "abcdef1" }, runner.LastArguments);

            var hardResult = service.ResetCommitAsync("/home/user/repo", "abcdef1", GitResetMode.Hard).GetAwaiter().GetResult();
            Assert.That(hardResult.Success, Is.True);
            CollectionAssert.AreEqual(new[] { "reset", "--hard", "abcdef1" }, runner.LastArguments);
        }

        private static void ConfigureTestIdentity(string workingDirectory)
        {
            RunGit(workingDirectory, "config user.email \"tests@example.com\"");
            RunGit(workingDirectory, "config user.name \"MyCodex Tests\"");
        }

        private static void RunGit(string workingDirectory, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.Start();
                var stderr = process.StandardError.ReadToEnd();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Git command failed: " + arguments + Environment.NewLine + stderr);
                }
            }
        }

        private static string ReadGitOutput(string workingDirectory, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Git command failed: " + arguments + Environment.NewLine + stderr);
                }

                return stdout;
            }
        }

        private static int RunGitAllowFailure(string workingDirectory, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.Start();
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string NormalizeNewLines(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string NormalizePathForComparison(string value)
        {
            return (value ?? string.Empty).TrimEnd('\\', '/').Replace('\\', '/');
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-GitRepositoryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(filePath, FileAttributes.Normal);
                    }
                    catch
                    {
                    }
                }

                Directory.Delete(path, true);
            }
        }

        private sealed class BlockingCancellationGitCommandRunner : IGitCommandRunner
        {
            public ManualResetEventSlim RunStarted { get; } = new ManualResetEventSlim();

            public CancellationToken LastCancellationToken { get; private set; }

            public GitCommandResult Run(
                string workingDirectory,
                IReadOnlyList<string> arguments,
                int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                LastCancellationToken = cancellationToken;
                RunStarted.Set();
                cancellationToken.WaitHandle.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }

            public GitCommandResult Run(
                string workingDirectory,
                IReadOnlyList<string> arguments,
                int timeoutMilliseconds)
            {
                return Run(workingDirectory, arguments, timeoutMilliseconds, CancellationToken.None);
            }
        }

        private sealed class FakeGitCommandRunner : IGitCommandRunner
        {
            public GitCommandResult Run(
                string workingDirectory,
                System.Collections.Generic.IReadOnlyList<string> arguments,
                int timeoutMilliseconds,
                System.Threading.CancellationToken cancellationToken)
            {
                return Run(workingDirectory, arguments, timeoutMilliseconds);
            }

            public IReadOnlyList<string> LastArguments { get; private set; }

            public List<IReadOnlyList<string>> Commands { get; } = new List<IReadOnlyList<string>>();

            public int CallCount { get; private set; }

            public string StatusOutput { get; set; } = "?? src/app file.txt\0";

            public string StatusError { get; set; } = string.Empty;

            public int StatusExitCode { get; set; }

            public string UnstagedNumstatOutput { get; set; } = "1\t0\tsrc/app file.txt\0";

            public string StagedNumstatOutput { get; set; } = string.Empty;

            public string RemoteUrl { get; set; }

            public string RemoteListOutput { get; set; } = "origin\thttps://example.com/fetch.git (fetch)\norigin\thttps://example.com/push.git (push)\nbackup\tgit@example.com:backup.git (fetch)\nbackup\tgit@example.com:backup.git (push)\n";

            public string RemoteListError { get; set; } = string.Empty;

            public int RemoteListExitCode { get; set; }

            public string LogError { get; set; } = string.Empty;

            public int LogExitCode { get; set; }

            public string BranchError { get; set; } = string.Empty;

            public int BranchExitCode { get; set; }

            public string StashListError { get; set; } = string.Empty;

            public int StashListExitCode { get; set; }

            public string WorktreeListError { get; set; } = string.Empty;

            public int WorktreeListExitCode { get; set; }

            public string MergeOutput { get; set; } = string.Empty;

            public string RebaseOutput { get; set; } = string.Empty;

            public bool CherryPickInProgress { get; set; }

            public bool RevertInProgress { get; set; }

            public bool MergeInProgress { get; set; }

            public bool RebaseInProgress { get; set; }

            public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds)
            {
                CallCount++;
                LastArguments = arguments != null ? arguments.ToArray() : new string[0];
                Commands.Add(LastArguments);
                var command = LastArguments.Count > 0 ? LastArguments[0] : string.Empty;
                var output = string.Empty;
                var error = string.Empty;
                var exitCode = 0;

                if (command == "rev-parse" && LastArguments.Contains("--is-inside-work-tree"))
                {
                    output = "true\n";
                }
                else if (command == "rev-parse" && LastArguments.Contains("--verify") && LastArguments.Contains("CHERRY_PICK_HEAD"))
                {
                    exitCode = CherryPickInProgress ? 0 : 1;
                }
                else if (command == "rev-parse" && LastArguments.Contains("--verify") && LastArguments.Contains("REVERT_HEAD"))
                {
                    exitCode = RevertInProgress ? 0 : 1;
                }
                else if (command == "rev-parse" && LastArguments.Contains("--verify") && LastArguments.Contains("MERGE_HEAD"))
                {
                    exitCode = MergeInProgress ? 0 : 1;
                }
                else if (command == "rev-parse" && LastArguments.Contains("--verify") && LastArguments.Contains("REBASE_HEAD"))
                {
                    exitCode = RebaseInProgress ? 0 : 1;
                }
                else if (command == "symbolic-ref")
                {
                    output = "main\n";
                }
                else if (command == "merge")
                {
                    output = MergeOutput;
                }
                else if (command == "rebase")
                {
                    output = RebaseOutput;
                }
                else if (command == "rev-parse" && LastArguments.Contains("--short"))
                {
                    output = "abc1234\n";
                }
                else if (command == "rev-parse" && LastArguments.Contains("@{u}"))
                {
                    exitCode = 1;
                }
                else if (command == "rev-list")
                {
                    output = "2\t3\n";
                }
                else if (command == "config")
                {
                    if (string.IsNullOrWhiteSpace(RemoteUrl))
                    {
                        exitCode = 1;
                    }
                    else
                    {
                        output = RemoteUrl + "\n";
                    }
                }
                else if (command == "status")
                {
                    output = StatusOutput;
                    error = StatusError;
                    exitCode = StatusExitCode;
                }
                else if (command == "count-objects")
                {
                    output = "count: 12\nsize: 48 KiB\nin-pack: 10\npacks: 1\nsize-pack: 20 KiB\nprune-packable: 0\ngarbage: 0\nsize-garbage: 0 bytes\n";
                }
                else if (command == "remote" && LastArguments.Count > 1 && LastArguments[1] == "-v")
                {
                    output = RemoteListOutput;
                    error = RemoteListError;
                    exitCode = RemoteListExitCode;
                }
                else if (command == "remote")
                {
                    output = string.Empty;
                }
                else if (command == "branch")
                {
                    output = string.Empty;
                }
                else if (command == "add")
                {
                    output = string.Empty;
                }
                else if (command == "commit")
                {
                    output = string.Empty;
                }
                else if (command == "fetch")
                {
                    output = string.Empty;
                }
                else if (command == "pull")
                {
                    output = string.Empty;
                }
                else if (command == "push")
                {
                    output = string.Empty;
                }
                else if (command == "clean")
                {
                    output = string.Empty;
                }
                else if (command == "diff" && LastArguments.Contains("--numstat") && LastArguments.Contains("--cached"))
                {
                    output = StagedNumstatOutput;
                }
                else if (command == "diff" && LastArguments.Contains("--numstat"))
                {
                    output = UnstagedNumstatOutput;
                }
                else if (command == "diff" && LastArguments.Contains("--no-index"))
                {
                    output = "diff --git a/src/app file.txt b/src/app file.txt\nnew file mode 100644\n--- /dev/null\n+++ b/src/app file.txt\n@@ -0,0 +1 @@\n+hello\n";
                    exitCode = 1;
                }
                else if (command == "diff" && LastArguments.Contains("--cc"))
                {
                    output = "diff --cc src/app.cs\n";
                }
                else if (command == "log" && LastArguments.Contains("--format=%B"))
                {
                    output = "Previous subject\n\nPrevious body line 1\nPrevious body line 2\n";
                }
                else if (command == "log")
                {
                    output = "abcdef1234567890\u001fabcdef1\u001fMyCodex Tests\u001f2026-01-01T00:00:00+00:00\u001fInitial commit\u001e";
                    error = LogError;
                    exitCode = LogExitCode;
                }
                else if (command == "show" && LastArguments.Contains("--name-status"))
                {
                    output = "M\0src/app.cs\0R100\0old.txt\0new.txt\0";
                }
                else if (command == "show")
                {
                    output = "diff --git a/src/app.cs b/src/app.cs\n";
                }
                else if (command == "for-each-ref")
                {
                    output = "main\t*\torigin/main\t[ahead 1]\trefs/heads/main\norigin/HEAD\t \t\t\trefs/remotes/origin/HEAD\norigin/feature/demo\t \t\t\trefs/remotes/origin/feature/demo\n";
                    error = BranchError;
                    exitCode = BranchExitCode;
                }
                else if (command == "worktree" && LastArguments.Count > 1 && LastArguments[1] == "list")
                {
                    output = "worktree /home/user/repo\nHEAD abcdef1234567890\nbranch refs/heads/main\n\nworktree /home/user/repo-feature\nHEAD 1234567890abcdef\nbranch refs/heads/feature/demo\nprunable gitdir file points to non-existent location\n\n";
                    error = WorktreeListError;
                    exitCode = WorktreeListExitCode;
                }
                else if (command == "worktree")
                {
                    output = string.Empty;
                }
                else if (command == "stash" && LastArguments.Count > 1 && LastArguments[1] == "list")
                {
                    output = "stash@{0}\u001fOn main: demo stash\u001e";
                    error = StashListError;
                    exitCode = StashListExitCode;
                }
                else if (command == "stash" && LastArguments.Count > 1 && LastArguments[1] == "show" && LastArguments.Contains("--name-status"))
                {
                    output = "M\0src/app.cs\0";
                }
                else if (command == "stash" && LastArguments.Count > 1 && LastArguments[1] == "show")
                {
                    output = "diff --git a/src/app.cs b/src/app.cs\n";
                }
                else if (command == "stash")
                {
                    output = string.Empty;
                }
                else if (command == "switch")
                {
                    output = string.Empty;
                }
                else if (command == "revert")
                {
                    output = string.Empty;
                }

                return new GitCommandResult
                {
                    ExitCode = exitCode,
                    StandardOutput = output,
                    StandardError = error
                };
            }
        }
    }
}

