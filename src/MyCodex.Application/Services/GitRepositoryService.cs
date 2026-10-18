using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class GitRepositoryService : IGitRepositoryService
    {
        private readonly WorkspaceService _workspaceService;
        private readonly IGitCommandRunner _runner;
        private readonly Func<string, string> _normalizeWorkspacePath;
        private readonly bool _useUnixFilePaths;
        private readonly bool _readExtendedStatusDetails;
        private readonly bool _detectRenames;
        private static readonly AsyncLocal<CancellationToken> _activeCancellationToken = new AsyncLocal<CancellationToken>();

        public GitRepositoryService(IGitCommandRunner runner)
            : this(runner, null, false)
        {
        }

        public GitRepositoryService(
            IGitCommandRunner runner,
            Func<string, string> normalizeWorkspacePath,
            bool useUnixFilePaths)
            : this(runner, normalizeWorkspacePath, useUnixFilePaths, true)
        {
        }

        public GitRepositoryService(
            IGitCommandRunner runner,
            Func<string, string> normalizeWorkspacePath,
            bool useUnixFilePaths,
            bool readExtendedStatusDetails)
            : this(runner, normalizeWorkspacePath, useUnixFilePaths, readExtendedStatusDetails, true)
        {
        }

        public GitRepositoryService(
            IGitCommandRunner runner,
            Func<string, string> normalizeWorkspacePath,
            bool useUnixFilePaths,
            bool readExtendedStatusDetails,
            bool detectRenames)
        {
            _workspaceService = new WorkspaceService();
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _normalizeWorkspacePath = normalizeWorkspacePath;
            _useUnixFilePaths = useUnixFilePaths;
            _readExtendedStatusDetails = readExtendedStatusDetails;
            _detectRenames = detectRenames;
        }

        public Task<GitRepositoryStatusSnapshot> ReadStatusAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadStatus(workspacePath), cancellationToken);
        }

        public Task<IReadOnlyList<GitCommitHistoryEntry>> ReadCommitHistoryAsync(string workspacePath, int maxCount, CancellationToken cancellationToken = default)
        {
            return ReadCommitHistoryAsync(workspacePath, null, maxCount, cancellationToken);
        }

        public Task<IReadOnlyList<GitCommitHistoryEntry>> ReadCommitHistoryAsync(string workspacePath, string revision, int maxCount, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadCommitHistory(workspacePath, revision, maxCount), cancellationToken);
        }

        public Task<IReadOnlyList<GitCommitFileEntry>> ReadCommitFilesAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadCommitFiles(workspacePath, commitRef), cancellationToken);
        }

        public Task<GitOperationResult> ReadCommitDiffAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadCommitDiff(workspacePath, commitRef), cancellationToken);
        }

        public Task<GitCommitMessageEntry> ReadCommitMessageAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadCommitMessage(workspacePath, commitRef), cancellationToken);
        }

        public Task<IReadOnlyList<GitBranchEntry>> ReadBranchesAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadBranches(workspacePath), cancellationToken);
        }

        public Task<GitBranchComparison> CompareBranchesAsync(string workspacePath, string currentRef, string targetRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => CompareBranches(workspacePath, currentRef, targetRef), cancellationToken);
        }

        public Task<IReadOnlyList<GitRemoteEntry>> ReadRemotesAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadRemotes(workspacePath), cancellationToken);
        }

        public Task<IReadOnlyList<GitStashEntry>> ReadStashesAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadStashes(workspacePath), cancellationToken);
        }

        public Task<IReadOnlyList<GitWorktreeEntry>> ReadWorktreesAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadWorktrees(workspacePath), cancellationToken);
        }

        public Task<IReadOnlyList<GitCommitFileEntry>> ReadStashFilesAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadStashFiles(workspacePath, stashRef), cancellationToken);
        }

        public Task<GitOperationResult> ReadStashDiffAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadStashDiff(workspacePath, stashRef), cancellationToken);
        }

        public Task<GitOperationResult> ReadStashFileDiffAsync(string workspacePath, string stashRef, string filePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadStashFileDiff(workspacePath, stashRef, filePath), cancellationToken);
        }

        public Task<GitOperationResult> InitializeRepositoryAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("init"),
                SharedLocalization.Text("GitRepositoryService.Operation.Initialize", "Initialized repository.")), cancellationToken);
        }

        public Task<GitOperationResult> AddRemoteAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                var normalizedRemoteUrl = NormalizeRemoteUrl(remoteUrl);
                return RunOperation(
                    workspacePath,
                    Args("remote", "add", normalizedRemoteName, normalizedRemoteUrl),
                    SharedLocalization.Text("GitRepositoryService.Operation.AddRemote", "Added remote repository."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> SetRemoteUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                var normalizedRemoteUrl = NormalizeRemoteUrl(remoteUrl);
                return RunOperation(
                    workspacePath,
                    Args("remote", "set-url", normalizedRemoteName, normalizedRemoteUrl),
                    SharedLocalization.Text("GitRepositoryService.Operation.SetRemoteUrl", "Updated remote repository URL."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> SetRemotePushUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                var normalizedRemoteUrl = NormalizeRemoteUrl(remoteUrl);
                return RunOperation(
                    workspacePath,
                    Args("remote", "set-url", "--push", normalizedRemoteName, normalizedRemoteUrl),
                    SharedLocalization.Text("GitRepositoryService.Operation.SetRemotePushUrl", "Updated remote repository push URL."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> RemoveRemoteAsync(string workspacePath, string remoteName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                return RunOperation(
                    workspacePath,
                    Args("remote", "remove", normalizedRemoteName),
                    SharedLocalization.Text("GitRepositoryService.Operation.RemoveRemote", "Removed remote repository."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> SetUpstreamAsync(string workspacePath, string branchName, string upstream, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                var normalizedUpstream = NormalizeRevisionRef(upstream);
                if (string.IsNullOrWhiteSpace(normalizedUpstream))
                {
                    throw new ArgumentException(
                        SharedLocalization.Text("GitRepositoryService.Error.UpstreamRequired", "Upstream cannot be empty."),
                        nameof(upstream));
                }

                return RunOperation(
                    workspacePath,
                    Args("branch", "--set-upstream-to", normalizedUpstream, normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.SetUpstream", "Updated branch upstream."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> UnsetUpstreamAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("branch", "--unset-upstream", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.UnsetUpstream", "Removed branch upstream."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StageAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments => Args("add", "--").Concat(pathArguments).ToArray(),
                SharedLocalization.Text("GitRepositoryService.Operation.Stage", "Staged selected file(s).")), cancellationToken);
        }

        public Task<GitOperationResult> MarkConflictsResolvedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments => Args("add", "--").Concat(pathArguments).ToArray(),
                SharedLocalization.Text("GitRepositoryService.Operation.MarkConflictsResolved", "Marked selected conflict(s) as resolved.")), cancellationToken);
        }

        public Task<GitOperationResult> StageAllAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("add", "--all"),
                SharedLocalization.Text("GitRepositoryService.Operation.StageAll", "Staged all changes.")), cancellationToken);
        }

        public Task<GitOperationResult> UnstageAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments => Args("reset", "--").Concat(pathArguments).ToArray(),
                SharedLocalization.Text("GitRepositoryService.Operation.Unstage", "Unstaged selected file(s).")), cancellationToken);
        }

        public Task<GitOperationResult> UnstageAllAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("reset", "--", "."),
                SharedLocalization.Text("GitRepositoryService.Operation.UnstageAll", "Unstaged all changes.")), cancellationToken);
        }

        public Task<GitOperationResult> CommitAsync(string workspacePath, string message, CancellationToken cancellationToken = default)
        {
            return CommitAsync(workspacePath, message, null, cancellationToken);
        }

        public Task<GitOperationResult> CommitAsync(string workspacePath, string subject, string body, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                return RunOperation(
                    workspacePath,
                    BuildCommitArguments(false, subject, body),
                    SharedLocalization.Text("GitRepositoryService.Operation.Commit", "Committed staged changes."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> CommitAmendAsync(string workspacePath, string message, CancellationToken cancellationToken = default)
        {
            return CommitAmendAsync(workspacePath, message, null, cancellationToken);
        }

        public Task<GitOperationResult> CommitAmendAsync(string workspacePath, string subject, string body, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                return RunOperation(
                    workspacePath,
                    BuildCommitArguments(true, subject, body),
                    SharedLocalization.Text("GitRepositoryService.Operation.CommitAmend", "Amended the previous commit."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> CheckoutBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("switch", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.CheckoutBranch", "Switched branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> MergeBranchIntoCurrentAsync(string workspacePath, string branchName, bool squash, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                var arguments = squash
                    ? Args("merge", "--squash", normalizedBranchName)
                    : Args("merge", "--no-ff", normalizedBranchName);
                var result = RunOperation(
                    workspacePath,
                    arguments,
                    squash
                        ? SharedLocalization.Text("GitRepositoryService.Operation.MergeBranchSquash", "Squashed selected branch into the current branch.")
                        : SharedLocalization.Text("GitRepositoryService.Operation.MergeBranch", "Merged selected branch into the current branch."),
                    60000);
                if (IsAlreadyUpToDateResult(result))
                {
                    result.DisplayMessage = SharedLocalization.Text(
                        squash
                            ? "GitRepositoryService.Operation.MergeBranchSquashNoChanges"
                            : "GitRepositoryService.Operation.MergeBranchNoChanges",
                        squash
                            ? "The current branch already contains the selected branch. No squash merge was needed."
                            : "The current branch already contains the selected branch. No merge was needed.");
                }

                return result;
            }, cancellationToken);
        }

        public Task<GitOperationResult> RebaseCurrentBranchOntoAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                var result = RunOperation(
                    workspacePath,
                    Args("rebase", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.RebaseCurrentBranch", "Rebased the current branch onto the selected branch."),
                    60000);
                if (IsAlreadyUpToDateResult(result))
                {
                    result.DisplayMessage = SharedLocalization.Text(
                        "GitRepositoryService.Operation.RebaseCurrentBranchNoChanges",
                        "The current branch is already up to date with the selected branch. No rebase was needed.");
                }

                return result;
            }, cancellationToken);
        }

        public Task<GitOperationResult> RevertCommitAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedCommitRef = NormalizeCommitRef(commitRef);
                return RunOperation(
                    workspacePath,
                    Args("revert", "--no-edit", normalizedCommitRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.RevertCommit", "Reverted selected commit."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> RevertCommitsAsync(string workspacePath, IReadOnlyList<string> commitRefs, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedCommitRefs = NormalizeCommitRefs(commitRefs);
                var arguments = Args("revert", "--no-edit").Concat(normalizedCommitRefs).ToArray();
                return RunOperation(
                    workspacePath,
                    arguments,
                    SharedLocalization.Text("GitRepositoryService.Operation.RevertCommits", "Reverted selected commits."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> CherryPickAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedCommitRef = NormalizeCommitRef(commitRef);
                return RunOperation(
                    workspacePath,
                    Args("cherry-pick", normalizedCommitRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.CherryPick", "Cherry-picked selected commit."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> ContinueSequencerAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunSequencerOperation(
                workspacePath,
                "--continue",
                SharedLocalization.Text("GitRepositoryService.Operation.SequencerContinue", "Continued current Git operation.")), cancellationToken);
        }

        public Task<GitOperationResult> AbortSequencerAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunSequencerOperation(
                workspacePath,
                "--abort",
                SharedLocalization.Text("GitRepositoryService.Operation.SequencerAbort", "Aborted current Git operation.")), cancellationToken);
        }

        public Task<GitOperationResult> SkipSequencerAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunSequencerOperation(
                workspacePath,
                "--skip",
                SharedLocalization.Text("GitRepositoryService.Operation.SequencerSkip", "Skipped current Git operation step.")), cancellationToken);
        }

        public Task<GitOperationResult> ResetCommitAsync(string workspacePath, string commitRef, GitResetMode mode, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedCommitRef = NormalizeCommitRef(commitRef);
                var modeArgument = NormalizeResetMode(mode);
                return RunOperation(
                    workspacePath,
                    Args("reset", modeArgument, normalizedCommitRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.ResetCommit", "Rolled back current branch to the selected commit."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StashPushAsync(string workspacePath, string message, bool includeUntracked, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var stashMessage = string.IsNullOrWhiteSpace(message)
                    ? SharedLocalization.Text("GitRepositoryService.Stash.DefaultMessage", "MyCodex stash")
                    : message.Trim();
                var arguments = includeUntracked
                    ? Args("stash", "push", "--include-untracked", "-m", stashMessage)
                    : Args("stash", "push", "-m", stashMessage);
                return RunOperation(
                    workspacePath,
                    arguments,
                    SharedLocalization.Text("GitRepositoryService.Operation.StashPush", "Saved changes to stash."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StashPushPathsAsync(string workspacePath, string message, bool includeUntracked, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments =>
                {
                    var stashMessage = string.IsNullOrWhiteSpace(message)
                        ? SharedLocalization.Text("GitRepositoryService.Stash.DefaultMessage", "MyCodex stash")
                        : message.Trim();
                    var arguments = new List<string> { "stash", "push" };
                    if (includeUntracked)
                    {
                        arguments.Add("--include-untracked");
                    }

                    arguments.Add("-m");
                    arguments.Add(stashMessage);
                    arguments.Add("--");
                    arguments.AddRange(pathArguments);
                    return arguments.ToArray();
                },
                SharedLocalization.Text("GitRepositoryService.Operation.StashPushPaths", "Saved selected file(s) to stash.")), cancellationToken);
        }

        public Task<GitOperationResult> StashApplyAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedStashRef = NormalizeStashRef(stashRef);
                return RunOperation(
                    workspacePath,
                    Args("stash", "apply", normalizedStashRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.StashApply", "Applied selected stash."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StashPopAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedStashRef = NormalizeStashRef(stashRef);
                return RunOperation(
                    workspacePath,
                    Args("stash", "pop", normalizedStashRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.StashPop", "Popped selected stash."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StashDropAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedStashRef = NormalizeStashRef(stashRef);
                return RunOperation(
                    workspacePath,
                    Args("stash", "drop", normalizedStashRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.StashDrop", "Dropped selected stash."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> StashBranchAsync(string workspacePath, string stashRef, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedStashRef = NormalizeStashRef(stashRef);
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("stash", "branch", normalizedBranchName, normalizedStashRef),
                    SharedLocalization.Text("GitRepositoryService.Operation.StashBranch", "Created branch from selected stash."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> CreateWorktreeAsync(string workspacePath, string targetPath, string branchName, string startPoint, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedTargetPath = NormalizeWorktreePath(targetPath);
                var normalizedBranchName = NormalizeBranchName(branchName);
                var normalizedStartPoint = string.IsNullOrWhiteSpace(startPoint) ? "HEAD" : NormalizeCommitRef(startPoint);
                return RunOperation(
                    workspacePath,
                    Args("worktree", "add", "-b", normalizedBranchName, normalizedTargetPath, normalizedStartPoint),
                    SharedLocalization.Text("GitRepositoryService.Operation.CreateWorktree", "Created worktree."),
                    60000);
            }, cancellationToken);
        }

        public Task<GitOperationResult> RemoveWorktreeAsync(string workspacePath, string worktreePath, bool force, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedWorktreePath = NormalizeWorktreePath(worktreePath);
                var arguments = force
                    ? Args("worktree", "remove", "--force", normalizedWorktreePath)
                    : Args("worktree", "remove", normalizedWorktreePath);
                return RunOperation(
                    workspacePath,
                    arguments,
                    SharedLocalization.Text("GitRepositoryService.Operation.RemoveWorktree", "Removed worktree."),
                    60000);
            }, cancellationToken);
        }

        public Task<GitOperationResult> PruneWorktreesAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(
                workspacePath,
                Args("worktree", "prune"),
                SharedLocalization.Text("GitRepositoryService.Operation.PruneWorktrees", "Pruned stale worktree records."),
                60000), cancellationToken);
        }

        public Task<GitOperationResult> CheckoutRemoteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("switch", "--track", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.CheckoutRemoteBranch", "Created local tracking branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> CreateBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("switch", "-c", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.CreateBranch", "Created and switched to new branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> RenameCurrentBranchAsync(string workspacePath, string newBranchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(newBranchName);
                return RunOperation(
                    workspacePath,
                    Args("branch", "-m", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.RenameCurrentBranch", "Renamed current branch."));
            }, cancellationToken);
        }
        public Task<GitOperationResult> RenameBranchAsync(string workspacePath, string oldBranchName, string newBranchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedOldBranchName = NormalizeBranchName(oldBranchName);
                var normalizedNewBranchName = NormalizeBranchName(newBranchName);
                return RunOperation(
                    workspacePath,
                    Args("branch", "-m", normalizedOldBranchName, normalizedNewBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.RenameBranch", "Renamed local branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> DeleteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("branch", "-d", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.DeleteBranch", "Deleted local branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> ForceDeleteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("branch", "-D", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.ForceDeleteBranch", "Force deleted local branch."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> DeleteRemoteBranchAsync(string workspacePath, string remoteName, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                var normalizedBranchName = NormalizeBranchName(branchName);
                return RunOperation(
                    workspacePath,
                    Args("push", normalizedRemoteName, "--delete", normalizedBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.DeleteRemoteBranch", "Deleted remote branch."),
                    60000);
            }, cancellationToken);
        }

        public Task<GitOperationResult> CheckoutPullRequestAsync(
            string workspacePath,
            string remoteName,
            int pullRequestNumber,
            string localBranchName,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                if (pullRequestNumber <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(pullRequestNumber),
                        SharedLocalization.Text("GitRepositoryService.Error.PullRequestNumberRequired", "Pull request number must be greater than zero."));
                }

                var normalizedRemoteName = NormalizeRemoteName(remoteName);
                var normalizedLocalBranchName = NormalizeBranchName(localBranchName);
                var fetchResult = RunOperation(
                    workspacePath,
                    Args("fetch", normalizedRemoteName, "pull/" + pullRequestNumber + "/head:" + normalizedLocalBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.FetchPullRequest", "Fetched pull request branch."),
                    60000);
                if (!fetchResult.Success)
                {
                    return fetchResult;
                }

                return RunOperation(
                    workspacePath,
                    Args("switch", normalizedLocalBranchName),
                    SharedLocalization.Text("GitRepositoryService.Operation.CheckoutPullRequest", "Checked out pull request."));
            }, cancellationToken);
        }

        public Task<GitOperationResult> PushAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("push"),
                SharedLocalization.Text("GitRepositoryService.Operation.Push", "Pushed current branch."), 60000), cancellationToken);
        }

        public Task<GitOperationResult> PushForceWithLeaseAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("push", "--force-with-lease"),
                SharedLocalization.Text("GitRepositoryService.Operation.PushForceWithLease", "Force-pushed current branch with lease."), 60000), cancellationToken);
        }

        public Task<GitOperationResult> PushSetUpstreamAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        {
            return RunAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    throw new ArgumentException(
                        SharedLocalization.Text("GitRepositoryService.Error.BranchRequired", "Branch name cannot be empty."),
                        nameof(branchName));
                }

                return RunOperation(
                    workspacePath,
                    Args("push", "-u", "origin", branchName.Trim()),
                    SharedLocalization.Text("GitRepositoryService.Operation.PushSetUpstream", "Set upstream and pushed current branch."),
                    60000);
            }, cancellationToken);
        }

        public Task<GitOperationResult> FetchAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("fetch", "--prune"),
                SharedLocalization.Text("GitRepositoryService.Operation.Fetch", "Fetched remote updates."), 60000), cancellationToken);
        }

        public Task<GitOperationResult> PullAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(workspacePath, Args("pull"),
                SharedLocalization.Text("GitRepositoryService.Operation.Pull", "Pulled remote updates."), 60000), cancellationToken);
        }

        public Task<GitOperationResult> DiscardUnstagedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments => Args("restore", "--").Concat(pathArguments).ToArray(),
                SharedLocalization.Text("GitRepositoryService.Operation.Discard", "Discarded selected unstaged change(s).")), cancellationToken);
        }

        public Task<GitOperationResult> DeleteUntrackedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunPathOperation(
                workspacePath,
                paths,
                pathArguments => Args("clean", "-f", "--").Concat(pathArguments).ToArray(),
                SharedLocalization.Text("GitRepositoryService.Operation.DeleteUntracked", "Deleted selected untracked file(s).")), cancellationToken);
        }

        public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default)
        {
            return ReadDiffAsync(workspacePath, entry, GitDiffKind.Auto, cancellationToken);
        }

        public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, GitDiffKind diffKind, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadDiff(workspacePath, entry, diffKind), cancellationToken);
        }

        public Task<GitOperationResult> ReadConflictDetailsAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => ReadConflictDetails(workspacePath, entry), cancellationToken);
        }

        public Task<IReadOnlyList<GitRepositoryDiagnosticEntry>> RunRepositoryDiagnosticsAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunRepositoryDiagnostics(workspacePath), cancellationToken);
        }

        public Task<GitOperationResult> RunRepositoryIntegrityCheckAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(
                workspacePath,
                Args("fsck", "--full"),
                SharedLocalization.Text("GitRepositoryService.Operation.RepositoryIntegrityCheck", "Repository integrity check completed."),
                120000), cancellationToken);
        }

        public Task<GitOperationResult> RunRepositoryMaintenanceAsync(string workspacePath, CancellationToken cancellationToken = default)
        {
            return RunAsync(() => RunOperation(
                workspacePath,
                Args("maintenance", "run"),
                SharedLocalization.Text("GitRepositoryService.Operation.RepositoryMaintenance", "Repository maintenance completed."),
                120000), cancellationToken);
        }

        private static Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            var previousToken = _activeCancellationToken.Value;
            _activeCancellationToken.Value = cancellationToken;

            try
            {
                return Task.Run(action, cancellationToken);
            }
            finally
            {
                _activeCancellationToken.Value = previousToken;
            }
        }

        private GitCommandResult RunCommand(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds)
        {
            return _runner.Run(workingDirectory, arguments, timeoutMilliseconds, _activeCancellationToken.Value);
        }

        private GitRepositoryStatusSnapshot ReadStatus(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var snapshot = new GitRepositoryStatusSnapshot
            {
                WorkspacePath = normalizedPath
            };

            var statusResult = RunCommand(
                normalizedPath,
                Args("status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all", _detectRenames ? "--renames" : "--no-renames"),
                15000);
            if (!statusResult.Success)
            {
                var gitInfo = ReadLocalGitInfo(normalizedPath);
                snapshot.IsGitRepository = gitInfo != null && gitInfo.IsGitRepository;
                snapshot.CanInitializeRepository = !snapshot.IsGitRepository;
                snapshot.StatusErrorMessage = TextValueHelper.FirstNonEmptyTrimmed(
                    statusResult.StandardError,
                    statusResult.StandardOutput,
                    SharedLocalization.Text("GitRepositoryService.Error.StatusFailed", "Failed to read Git status."));
                return snapshot;
            }

            snapshot.IsGitRepository = true;
            snapshot.CanInitializeRepository = false;
            PopulateStatusFromPorcelainOutput(normalizedPath, snapshot, statusResult.StandardOutput);

            if (_readExtendedStatusDetails)
            {
                PopulateStatusLineStats(normalizedPath, snapshot);
                snapshot.HeadSha = ReadOptionalValue(normalizedPath, Args("rev-parse", "--short", "HEAD"));
                if (string.IsNullOrWhiteSpace(snapshot.Branch) && !string.IsNullOrWhiteSpace(snapshot.HeadSha))
                {
                    snapshot.Branch = "detached@" + snapshot.HeadSha;
                }

                snapshot.OriginUrl = ReadOptionalValue(normalizedPath, Args("config", "--get", "remote.origin.url"));
                if (string.IsNullOrWhiteSpace(snapshot.Upstream))
                {
                    snapshot.Upstream = ReadOptionalValue(normalizedPath, Args("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"));
                }

                if (snapshot.AheadCount == null || snapshot.BehindCount == null)
                {
                    PopulateAheadBehind(normalizedPath, snapshot);
                }

                PopulateSequencerState(normalizedPath, snapshot);
                snapshot.StashCount = ReadStashCount(normalizedPath);
            }

            PopulateStatusCounters(snapshot);
            return snapshot;
        }

        private WorkspaceGitInfoSnapshot ReadLocalGitInfo(string normalizedPath)
        {
            if (_normalizeWorkspacePath != null || WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedPath))
            {
                return null;
            }

            try
            {
                return _workspaceService.ReadGitInfo(normalizedPath);
            }
            catch
            {
                return null;
            }
        }

        private IReadOnlyList<GitCommitHistoryEntry> ReadCommitHistory(string workspacePath, string revision, int maxCount)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var boundedMaxCount = Math.Max(1, Math.Min(200, maxCount));
            var arguments = new List<string> { "log" };
            if (!string.IsNullOrWhiteSpace(revision))
            {
                arguments.Add(NormalizeRevisionRef(revision));
            }

            arguments.AddRange(new[]
            {
                "--date=iso-strict",
                "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s%x1e",
                "-n",
                boundedMaxCount.ToString()
            });
            var result = RunCommand(
                normalizedPath,
                arguments,
                20000);
            if (!result.Success)
            {
                if (IsEmptyCommitHistoryResult(result))
                {
                    return new GitCommitHistoryEntry[0];
                }

                ThrowGitReadFailure(
                    "GitRepositoryService.Error.ReadCommitHistoryFailed",
                    "Failed to read Git commit history.",
                    result);
            }

            return ParseCommitHistory(result.StandardOutput);
        }

        private IReadOnlyList<GitCommitFileEntry> ReadCommitFiles(string workspacePath, string commitRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedCommitRef = NormalizeCommitRef(commitRef);
            var result = RunCommand(
                normalizedPath,
                Args("show", "--name-status", "--format=", "-z", "--find-renames", normalizedCommitRef),
                20000);
            if (!result.Success)
            {
                return new GitCommitFileEntry[0];
            }

            return ParseCommitFiles(result.StandardOutput);
        }

        private GitOperationResult ReadCommitDiff(string workspacePath, string commitRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedCommitRef = NormalizeCommitRef(commitRef);
            var result = RunCommand(
                normalizedPath,
                Args("show", "--format=", "--no-ext-diff", "--binary", normalizedCommitRef),
                20000);
            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = result.Success ? (result.StandardOutput ?? string.Empty).Trim() : result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? SharedLocalization.Text("GitRepositoryService.Operation.ReadCommitDiff", "Loaded commit diff.")
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private GitCommitMessageEntry ReadCommitMessage(string workspacePath, string commitRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedCommitRef = NormalizeCommitRef(commitRef);
            var result = RunCommand(
                normalizedPath,
                Args("log", "-1", "--format=%B", normalizedCommitRef),
                15000);
            if (!result.Success)
            {
                throw new InvalidOperationException(TextValueHelper.FirstNonEmptyTrimmed(
                    result.StandardError,
                    result.StandardOutput,
                    SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed.")));
            }

            return ParseCommitMessage(result.StandardOutput);
        }

        private static GitCommitMessageEntry ParseCommitMessage(string output)
        {
            var normalized = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new GitCommitMessageEntry();
            }

            var lines = normalized.Split('\n');
            var bodyLines = lines
                .Skip(1)
                .SkipWhile(line => string.IsNullOrWhiteSpace(line))
                .ToArray();
            return new GitCommitMessageEntry
            {
                Subject = (lines[0] ?? string.Empty).Trim(),
                Body = string.Join(Environment.NewLine, bodyLines).TrimEnd()
            };
        }

        private GitBranchComparison CompareBranches(string workspacePath, string currentRef, string targetRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedCurrentRef = NormalizeRevisionRef(currentRef);
            var normalizedTargetRef = NormalizeRevisionRef(targetRef);
            if (string.IsNullOrWhiteSpace(normalizedCurrentRef) || string.IsNullOrWhiteSpace(normalizedTargetRef))
            {
                throw new ArgumentException(SharedLocalization.Text("GitRepositoryService.Error.RevisionRequired", "Revision cannot be empty."));
            }

            var result = RunCommand(
                normalizedPath,
                Args("rev-list", "--left-right", "--count", normalizedCurrentRef + "..." + normalizedTargetRef),
                15000);
            if (!result.Success)
            {
                ThrowGitReadFailure(
                    "GitRepositoryService.Error.CompareBranchesFailed",
                    "Failed to compare Git branches.",
                    result);
            }

            var parts = (result.StandardOutput ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var currentOnly = 0;
            var targetOnly = 0;
            if (parts.Length > 0)
            {
                int.TryParse(parts[0], out currentOnly);
            }

            if (parts.Length > 1)
            {
                int.TryParse(parts[1], out targetOnly);
            }

            return new GitBranchComparison
            {
                CurrentRef = normalizedCurrentRef,
                TargetRef = normalizedTargetRef,
                CurrentOnlyCount = currentOnly,
                TargetOnlyCount = targetOnly
            };
        }

        private IReadOnlyList<GitBranchEntry> ReadBranches(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(
                normalizedPath,
                Args("for-each-ref", "refs/heads", "refs/remotes", "--format=%(refname:short)%09%(HEAD)%09%(upstream:short)%09%(upstream:track)%09%(refname)"),
                15000);
            if (!result.Success)
            {
                ThrowGitReadFailure(
                    "GitRepositoryService.Error.ReadBranchesFailed",
                    "Failed to read Git branches.",
                    result);
            }

            return ParseBranches(result.StandardOutput);
        }

        private IReadOnlyList<GitRemoteEntry> ReadRemotes(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(
                normalizedPath,
                Args("remote", "-v"),
                15000);
            if (!result.Success)
            {
                ThrowGitReadFailure(
                    "GitRepositoryService.Error.ReadRemotesFailed",
                    "Failed to read Git remotes.",
                    result);
            }

            return ParseRemotes(result.StandardOutput);
        }

        private IReadOnlyList<GitStashEntry> ReadStashes(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(
                normalizedPath,
                Args("stash", "list", "--format=%gd%x1f%gs%x1e"),
                15000);
            if (!result.Success)
            {
                ThrowGitReadFailure(
                    "GitRepositoryService.Error.ReadStashesFailed",
                    "Failed to read Git stashes.",
                    result);
            }

            return ParseStashes(result.StandardOutput);
        }

        private IReadOnlyList<GitWorktreeEntry> ReadWorktrees(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(
                normalizedPath,
                Args("worktree", "list", "--porcelain"),
                15000);
            if (!result.Success)
            {
                ThrowGitReadFailure(
                    "GitRepositoryService.Error.ReadWorktreesFailed",
                    "Failed to read Git worktrees.",
                    result);
            }

            return ParseWorktrees(result.StandardOutput);
        }

        private IReadOnlyList<GitRepositoryDiagnosticEntry> RunRepositoryDiagnostics(string workspacePath)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var entries = new List<GitRepositoryDiagnosticEntry>();

            var version = RunCommand(normalizedPath, Args("--version"), 15000);
            AddDiagnostic(
                entries,
                version.Success ? "ok" : "error",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.GitVersion.Title", "Git executable"),
                version.Success
                    ? TextValueHelper.FirstNonEmptyTrimmed(version.StandardOutput, SharedLocalization.Text("GitRepositoryService.Diagnostics.GitVersion.Available", "Git is available."))
                    : TextValueHelper.FirstNonEmptyTrimmed(version.StandardError, version.StandardOutput, SharedLocalization.Text("GitRepositoryService.Diagnostics.GitVersion.Unavailable", "Git is not available.")),
                version.Success ? string.Empty : SharedLocalization.Text("GitRepositoryService.Diagnostics.GitVersion.Suggestion", "Install Git or check the runtime Git configuration."));

            var repository = RunCommand(normalizedPath, Args("rev-parse", "--is-inside-work-tree"), 15000);
            var isRepository = repository.Success && string.Equals((repository.StandardOutput ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
            AddDiagnostic(
                entries,
                isRepository ? "ok" : "error",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Repository.Title", "Repository"),
                isRepository
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.Repository.Valid", "Current workspace is inside a Git work tree.")
                    : TextValueHelper.FirstNonEmptyTrimmed(repository.StandardError, repository.StandardOutput, SharedLocalization.Text("GitRepositoryService.Diagnostics.Repository.Invalid", "Current workspace is not a Git repository.")),
                isRepository ? string.Empty : SharedLocalization.Text("GitRepositoryService.Diagnostics.Repository.Suggestion", "Initialize a repository or open an existing Git workspace."));
            if (!isRepository)
            {
                AddRepositoryOwnershipDiagnostic(entries, repository.StandardOutput, repository.StandardError);
                return entries;
            }

            AddRepositoryOwnershipDiagnostic(entries, repository.StandardOutput, repository.StandardError);

            var remotes = RunCommand(normalizedPath, Args("remote", "-v"), 15000);
            AddDiagnostic(
                entries,
                remotes.Success && !string.IsNullOrWhiteSpace(remotes.StandardOutput) ? "ok" : "warning",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Remote.Title", "Remotes"),
                remotes.Success && !string.IsNullOrWhiteSpace(remotes.StandardOutput)
                    ? remotes.StandardOutput.Trim()
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Remote.Empty", "No remote repositories are configured."),
                remotes.Success && !string.IsNullOrWhiteSpace(remotes.StandardOutput)
                    ? string.Empty
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Remote.Suggestion", "Add an origin remote before using pull or push workflows."));

            var upstream = RunCommand(normalizedPath, Args("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"), 15000);
            AddDiagnostic(
                entries,
                upstream.Success && !string.IsNullOrWhiteSpace(upstream.StandardOutput) ? "ok" : "warning",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Upstream.Title", "Upstream"),
                upstream.Success && !string.IsNullOrWhiteSpace(upstream.StandardOutput)
                    ? upstream.StandardOutput.Trim()
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Upstream.Empty", "Current branch has no upstream."),
                upstream.Success && !string.IsNullOrWhiteSpace(upstream.StandardOutput)
                    ? string.Empty
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Upstream.Suggestion", "Publish the branch or set an upstream before pulling and pushing normally."));

            var branch = RunCommand(normalizedPath, Args("symbolic-ref", "--short", "-q", "HEAD"), 15000);
            AddDiagnostic(
                entries,
                branch.Success && !string.IsNullOrWhiteSpace(branch.StandardOutput) ? "ok" : "warning",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Head.Title", "HEAD state"),
                branch.Success && !string.IsNullOrWhiteSpace(branch.StandardOutput)
                    ? string.Format(SharedLocalization.Text("GitRepositoryService.Diagnostics.Head.Branch.Template", "Current branch: {0}"), branch.StandardOutput.Trim())
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Head.Detached", "Repository is in detached HEAD state."),
                branch.Success && !string.IsNullOrWhiteSpace(branch.StandardOutput)
                    ? string.Empty
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Head.Suggestion", "Switch to a branch before normal development or publishing."));

            var operation = ResolveSequencerCommandName(normalizedPath);
            if (string.IsNullOrWhiteSpace(operation) && GitRefExists(normalizedPath, "MERGE_HEAD"))
            {
                operation = "merge";
            }

            AddDiagnostic(
                entries,
                string.IsNullOrWhiteSpace(operation) ? "ok" : "warning",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Operation.Title", "Unfinished operation"),
                string.IsNullOrWhiteSpace(operation)
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.Operation.None", "No merge, cherry-pick, or revert operation is in progress.")
                    : string.Format(SharedLocalization.Text("GitRepositoryService.Diagnostics.Operation.Active.Template", "A {0} operation is in progress."), operation),
                string.IsNullOrWhiteSpace(operation)
                    ? string.Empty
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Operation.Suggestion", "Resolve conflicts, continue, skip, or abort the current operation before starting another Git workflow."));

            AddWorkingTreeDiagnostic(normalizedPath, entries);
            AddObjectDatabaseDiagnostic(normalizedPath, entries);
            AddIndexLockDiagnostic(normalizedPath, entries);

            return entries;
        }

        private void AddRepositoryOwnershipDiagnostic(IList<GitRepositoryDiagnosticEntry> entries, string standardOutput, string standardError)
        {
            var detail = TextValueHelper.FirstNonEmptyTrimmed(standardError, standardOutput, string.Empty);
            var hasOwnershipProblem = ContainsRepositoryOwnershipError(detail);
            AddDiagnostic(
                entries,
                hasOwnershipProblem ? "error" : "ok",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Ownership.Title", "Repository ownership"),
                hasOwnershipProblem
                    ? detail
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Ownership.Ok", "No safe.directory ownership problem was detected."),
                hasOwnershipProblem
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.Ownership.Suggestion", "If you trust this repository, follow the Git error message and add the workspace path to safe.directory.")
                    : string.Empty);
        }

        private void AddWorkingTreeDiagnostic(string workspacePath, IList<GitRepositoryDiagnosticEntry> entries)
        {
            GitRepositoryStatusSnapshot snapshot;
            try
            {
                snapshot = ReadStatus(workspacePath);
            }
            catch (Exception ex)
            {
                AddDiagnostic(
                    entries,
                    "error",
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.Title", "Working tree"),
                    ex.Message,
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.ReadFailedSuggestion", "Refresh the Git Workbench and check whether the repository path is still available."));
                return;
            }

            if (snapshot == null || !snapshot.IsGitRepository)
            {
                AddDiagnostic(
                    entries,
                    "error",
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.Title", "Working tree"),
                    snapshot == null || string.IsNullOrWhiteSpace(snapshot.StatusErrorMessage)
                        ? SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.ReadFailed", "Failed to read working tree status.")
                        : snapshot.StatusErrorMessage,
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.ReadFailedSuggestion", "Refresh the Git Workbench and check whether the repository path is still available."));
                return;
            }

            var dirtyCount = snapshot.StagedCount + snapshot.UnstagedCount + snapshot.UntrackedCount + snapshot.ConflictedCount;
            var hasDivergence = (snapshot.AheadCount.GetValueOrDefault() > 0) || (snapshot.BehindCount.GetValueOrDefault() > 0);
            var hasWork = dirtyCount > 0 || snapshot.StashCount > 0 || hasDivergence;
            var severity = snapshot.ConflictedCount > 0 ? "warning" : hasWork ? "warning" : "ok";
            var detail = string.Format(
                SharedLocalization.Text(
                    "GitRepositoryService.Diagnostics.WorkingTree.Detail.Template",
                    "Branch: {0}\r\nUpstream: {1}\r\nStaged: {2}\r\nUnstaged: {3}\r\nUntracked: {4}\r\nConflicts: {5}\r\nStashes: {6}\r\nAhead: {7}\r\nBehind: {8}"),
                TextValueHelper.FirstNonEmptyTrimmed(snapshot.Branch, SharedLocalization.Text("GitRepositoryService.Diagnostics.Value.Unknown", "(unknown)")),
                TextValueHelper.FirstNonEmptyTrimmed(snapshot.Upstream, SharedLocalization.Text("GitRepositoryService.Diagnostics.Value.None", "(none)")),
                snapshot.StagedCount,
                snapshot.UnstagedCount,
                snapshot.UntrackedCount,
                snapshot.ConflictedCount,
                snapshot.StashCount,
                snapshot.AheadCount.HasValue ? snapshot.AheadCount.Value.ToString() : SharedLocalization.Text("GitRepositoryService.Diagnostics.Value.Unknown", "(unknown)"),
                snapshot.BehindCount.HasValue ? snapshot.BehindCount.Value.ToString() : SharedLocalization.Text("GitRepositoryService.Diagnostics.Value.Unknown", "(unknown)"));
            AddDiagnostic(
                entries,
                severity,
                SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.Title", "Working tree"),
                detail,
                BuildWorkingTreeSuggestion(snapshot));
        }

        private void AddObjectDatabaseDiagnostic(string workspacePath, IList<GitRepositoryDiagnosticEntry> entries)
        {
            var result = RunCommand(workspacePath, Args("count-objects", "-vH"), 15000);
            if (!result.Success)
            {
                AddDiagnostic(
                    entries,
                    "warning",
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.Title", "Object database"),
                    TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.Unavailable", "Object database statistics are unavailable.")),
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.UnavailableSuggestion", "Run diagnostics again later, or use the command line if Git reports repository integrity errors."));
                return;
            }

            var looseObjects = ParseLooseObjectCount(result.StandardOutput);
            var needsMaintenance = looseObjects >= 1000;
            AddDiagnostic(
                entries,
                needsMaintenance ? "warning" : "ok",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.Title", "Object database"),
                string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.Empty", "No object database statistics were returned.")
                    : result.StandardOutput.Trim(),
                needsMaintenance
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.ObjectDatabase.MaintenanceSuggestion", "The repository has many loose objects. Consider running Git maintenance or gc from an advanced maintenance workflow after current Git operations finish.")
                    : string.Empty);
        }

        private void AddIndexLockDiagnostic(string workspacePath, IList<GitRepositoryDiagnosticEntry> entries)
        {
            if (_useUnixFilePaths)
            {
                AddDiagnostic(
                    entries,
                    "warning",
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.Title", "Lock files"),
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.RuntimeSkipped", "Lock file existence cannot be checked directly in this runtime."),
                    SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.RuntimeSuggestion", "If Git reports an index lock, inspect the runtime repository manually before deleting any lock file."));
                return;
            }

            var lockPathResult = RunCommand(workspacePath, Args("rev-parse", "--git-path", "index.lock"), 15000);
            var lockPath = lockPathResult.Success ? (lockPathResult.StandardOutput ?? string.Empty).Trim() : string.Empty;
            var exists = !string.IsNullOrWhiteSpace(lockPath) && File.Exists(lockPath);
            AddDiagnostic(
                entries,
                exists ? "warning" : "ok",
                SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.Title", "Lock files"),
                exists
                    ? string.Format(SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.Exists.Template", "Index lock exists: {0}"), lockPath)
                    : SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.None", "No index lock file was detected."),
                exists
                    ? SharedLocalization.Text("GitRepositoryService.Diagnostics.Lock.Suggestion", "Do not delete the lock file until you confirm no Git process is running.")
                    : string.Empty);
        }

        private static void AddDiagnostic(IList<GitRepositoryDiagnosticEntry> entries, string severity, string title, string detail, string suggestion)
        {
            entries.Add(new GitRepositoryDiagnosticEntry
            {
                Severity = severity ?? string.Empty,
                Title = title ?? string.Empty,
                Detail = detail ?? string.Empty,
                Suggestion = suggestion ?? string.Empty
            });
        }

        private static bool ContainsRepositoryOwnershipError(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.IndexOf("safe.directory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("dubious ownership", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildWorkingTreeSuggestion(GitRepositoryStatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }

            if (snapshot.ConflictedCount > 0)
            {
                return SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.ConflictSuggestion", "Resolve conflicts before committing, pulling, pushing, or continuing sequencer operations.");
            }

            if (snapshot.StagedCount + snapshot.UnstagedCount + snapshot.UntrackedCount > 0)
            {
                return SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.DirtySuggestion", "Review the changed files, then commit, stash, or discard intentional changes.");
            }

            if (snapshot.BehindCount.GetValueOrDefault() > 0)
            {
                return SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.BehindSuggestion", "Pull or fetch remote updates before continuing work that depends on the latest upstream state.");
            }

            if (snapshot.AheadCount.GetValueOrDefault() > 0)
            {
                return SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.AheadSuggestion", "Push local commits when you are ready to publish them.");
            }

            if (snapshot.StashCount > 0)
            {
                return SharedLocalization.Text("GitRepositoryService.Diagnostics.WorkingTree.StashSuggestion", "Review stashes if you expect a clean temporary-work state.");
            }

            return string.Empty;
        }

        private static int ParseLooseObjectCount(string output)
        {
            var lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("count:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int count;
                if (int.TryParse(line.Substring("count:".Length).Trim(), out count))
                {
                    return count;
                }
            }

            return 0;
        }

        private IReadOnlyList<GitCommitFileEntry> ReadStashFiles(string workspacePath, string stashRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedStashRef = NormalizeStashRef(stashRef);
            var result = RunCommand(
                normalizedPath,
                Args("stash", "show", "--name-status", "-z", normalizedStashRef),
                20000);
            if (!result.Success)
            {
                return new GitCommitFileEntry[0];
            }

            return ParseCommitFiles(result.StandardOutput);
        }

        private GitOperationResult ReadStashDiff(string workspacePath, string stashRef)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedStashRef = NormalizeStashRef(stashRef);
            var result = RunCommand(
                normalizedPath,
                Args("stash", "show", "--patch", "--no-ext-diff", "--binary", normalizedStashRef),
                20000);
            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = result.Success ? (result.StandardOutput ?? string.Empty).Trim() : result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? SharedLocalization.Text("GitRepositoryService.Operation.ReadStashDiff", "Loaded stash diff.")
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private GitOperationResult ReadStashFileDiff(string workspacePath, string stashRef, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(SharedLocalization.Text("GitRepositoryService.Error.PathsRequired", "At least one path is required."), nameof(filePath));
            }

            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var normalizedStashRef = NormalizeStashRef(stashRef);
            var result = RunCommand(
                normalizedPath,
                Args("diff", "--no-ext-diff", "--binary", normalizedStashRef + "^1", normalizedStashRef, "--", filePath.Trim()),
                20000);
            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = result.Success ? (result.StandardOutput ?? string.Empty).Trim() : result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? SharedLocalization.Text("GitRepositoryService.Operation.ReadStashFileDiff", "Loaded stash file diff.")
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private static IReadOnlyList<GitBranchEntry> ParseBranches(string output)
        {
            var entries = new List<GitBranchEntry>();
            var records = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < records.Length; i++)
            {
                var fields = records[i]
                    .Split(new[] { '\t' }, 5);
                if (fields.Length < 1 || string.IsNullOrWhiteSpace(fields[0]))
                {
                    continue;
                }

                var fullRefName = fields.Length > 4 ? fields[4].Trim() : string.Empty;
                if (fullRefName.StartsWith("refs/remotes/", StringComparison.Ordinal) &&
                    fullRefName.EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    continue;
                }

                var trackingStatus = fields.Length > 3 ? fields[3].Trim() : string.Empty;
                int? aheadCount;
                int? behindCount;
                ParseTrackingCounts(trackingStatus, out aheadCount, out behindCount);
                entries.Add(new GitBranchEntry
                {
                    Name = fields[0].Trim(),
                    IsCurrent = fields.Length > 1 && string.Equals(fields[1].Trim(), "*", StringComparison.Ordinal),
                    IsRemote = fullRefName.StartsWith("refs/remotes/", StringComparison.Ordinal),
                    Upstream = fields.Length > 2 ? fields[2].Trim() : string.Empty,
                    TrackingStatus = trackingStatus,
                    AheadCount = aheadCount,
                    BehindCount = behindCount
                });
            }

            return entries
                .OrderByDescending(item => item.IsCurrent)
                .ThenBy(item => item.IsRemote)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void ParseTrackingCounts(string trackingStatus, out int? aheadCount, out int? behindCount)
        {
            aheadCount = null;
            behindCount = null;
            var normalized = (trackingStatus ?? string.Empty).Trim().Trim('[', ']');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            var parts = normalized.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = (parts[i] ?? string.Empty).Trim();
                var tokens = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int value;
                if (tokens.Length >= 2 &&
                    int.TryParse(tokens[1], out value))
                {
                    if (string.Equals(tokens[0], "ahead", StringComparison.OrdinalIgnoreCase))
                    {
                        aheadCount = value;
                    }
                    else if (string.Equals(tokens[0], "behind", StringComparison.OrdinalIgnoreCase))
                    {
                        behindCount = value;
                    }
                }
            }
        }

        private static IReadOnlyList<GitRemoteEntry> ParseRemotes(string output)
        {
            var entries = new Dictionary<string, GitRemoteEntry>(StringComparer.OrdinalIgnoreCase);
            var lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = (lines[i] ?? string.Empty).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    continue;
                }

                GitRemoteEntry entry;
                if (!entries.TryGetValue(parts[0], out entry))
                {
                    entry = new GitRemoteEntry
                    {
                        Name = parts[0].Trim()
                    };
                    entries[entry.Name] = entry;
                }

                var url = parts[1].Trim();
                var kind = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                if (string.Equals(kind, "(push)", StringComparison.OrdinalIgnoreCase))
                {
                    entry.PushUrl = url;
                }
                else
                {
                    entry.FetchUrl = url;
                }
            }

            foreach (var entry in entries.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.PushUrl))
                {
                    entry.PushUrl = entry.FetchUrl ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(entry.FetchUrl))
                {
                    entry.FetchUrl = entry.PushUrl ?? string.Empty;
                }
            }

            return entries.Values
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IReadOnlyList<GitCommitHistoryEntry> ParseCommitHistory(string output)
        {
            var entries = new List<GitCommitHistoryEntry>();
            var records = (output ?? string.Empty)
                .Split(new[] { '\u001e' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < records.Length; i++)
            {
                var fields = records[i]
                    .Trim('\r', '\n')
                    .Split(new[] { '\u001f' }, 5);
                if (fields.Length < 5 || string.IsNullOrWhiteSpace(fields[0]))
                {
                    continue;
                }

                entries.Add(new GitCommitHistoryEntry
                {
                    FullSha = fields[0].Trim(),
                    ShortSha = fields[1].Trim(),
                    AuthorName = fields[2].Trim(),
                    AuthorDateText = fields[3].Trim(),
                    Subject = fields[4].Trim()
                });
            }

            return entries;
        }

        private IReadOnlyList<GitCommitFileEntry> ParseCommitFiles(string output)
        {
            var entries = new List<GitCommitFileEntry>();
            var tokens = SplitNullSeparated(output);
            for (var i = 0; i < tokens.Length;)
            {
                var status = (tokens[i++] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(status) || i >= tokens.Length)
                {
                    continue;
                }

                var originalPath = string.Empty;
                var path = NormalizeGitFilePath(tokens[i++]);
                if (status.StartsWith("R", StringComparison.OrdinalIgnoreCase) ||
                    status.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                {
                    originalPath = path;
                    path = i < tokens.Length ? NormalizeGitFilePath(tokens[i++]) : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                entries.Add(new GitCommitFileEntry
                {
                    FilePath = path,
                    OriginalFilePath = originalPath,
                    ChangeKind = MapNameStatusToChangeKind(status)
                });
            }

            return entries;
        }

        private static IReadOnlyList<GitStashEntry> ParseStashes(string output)
        {
            var entries = new List<GitStashEntry>();
            var records = (output ?? string.Empty)
                .Split(new[] { '\u001e' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < records.Length; i++)
            {
                var fields = records[i]
                    .Trim('\r', '\n')
                    .Split(new[] { '\u001f' }, 2);
                if (fields.Length < 1 || string.IsNullOrWhiteSpace(fields[0]))
                {
                    continue;
                }

                entries.Add(new GitStashEntry
                {
                    RefName = fields[0].Trim(),
                    Subject = fields.Length > 1 ? fields[1].Trim() : string.Empty
                });
            }

            return entries;
        }

        private static IReadOnlyList<GitWorktreeEntry> ParseWorktrees(string output)
        {
            var entries = new List<GitWorktreeEntry>();
            GitWorktreeEntry current = null;
            var lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i] ?? string.Empty;
                if (line.Length == 0)
                {
                    if (current != null)
                    {
                        entries.Add(current);
                        current = null;
                    }

                    continue;
                }

                if (line.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        entries.Add(current);
                    }

                    current = new GitWorktreeEntry { Path = line.Substring("worktree ".Length).Trim() };
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                {
                    current.Head = line.Substring("HEAD ".Length).Trim();
                }
                else if (line.StartsWith("branch ", StringComparison.Ordinal))
                {
                    var branch = line.Substring("branch ".Length).Trim();
                    current.Branch = branch.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? branch.Substring("refs/heads/".Length)
                        : branch;
                }
                else if (string.Equals(line, "bare", StringComparison.Ordinal))
                {
                    current.IsBare = true;
                }
                else if (string.Equals(line, "detached", StringComparison.Ordinal))
                {
                    current.IsDetached = true;
                }
                else if (line.StartsWith("prunable", StringComparison.Ordinal))
                {
                    current.IsPrunable = true;
                    current.PruneReason = line.Length > "prunable".Length
                        ? line.Substring("prunable".Length).Trim()
                        : string.Empty;
                }
            }

            if (current != null)
            {
                entries.Add(current);
            }

            return entries;
        }

        private void PopulateStatusFromPorcelainOutput(
            string workspacePath,
            GitRepositoryStatusSnapshot snapshot,
            string porcelainOutput)
        {
            var entries = SplitNullSeparated(porcelainOutput);
            for (var i = 0; i < entries.Length; i++)
            {
                var entryText = entries[i];
                if (string.IsNullOrWhiteSpace(entryText))
                {
                    continue;
                }

                if (entryText.StartsWith("## ", StringComparison.Ordinal))
                {
                    PopulateBranchStatus(snapshot, entryText.Substring(3));
                    continue;
                }

                if (entryText.Length < 4)
                {
                    continue;
                }

                var x = entryText[0];
                var y = entryText[1];
                var path = NormalizeGitFilePath(entryText.Substring(3));
                var originalPath = IsRenameStatus(x, y) && i + 1 < entries.Length
                    ? NormalizeGitFilePath(entries[++i])
                    : null;
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (IsConflictStatus(x, y))
                {
                    snapshot.Files.Add(BuildChangedFileEntry(workspacePath, path, originalPath, x, y, "conflict"));
                    continue;
                }

                if (x == '?' && y == '?')
                {
                    snapshot.Files.Add(BuildChangedFileEntry(workspacePath, path, originalPath, x, y, "untracked"));
                    continue;
                }

                if (x != ' ' && x != '?')
                {
                    snapshot.Files.Add(BuildChangedFileEntry(workspacePath, path, originalPath, x, y, "staged"));
                }

                if (y != ' ' && y != '?')
                {
                    snapshot.Files.Add(BuildChangedFileEntry(workspacePath, path, originalPath, x, y, "unstaged"));
                }
            }
        }

        private static void PopulateBranchStatus(GitRepositoryStatusSnapshot snapshot, string branchStatus)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(branchStatus))
            {
                return;
            }

            var text = branchStatus.Trim();
            string aheadBehindText = null;
            var bracketIndex = text.IndexOf(" [", StringComparison.Ordinal);
            if (bracketIndex >= 0)
            {
                aheadBehindText = text.Substring(bracketIndex + 2).TrimEnd(']');
                text = text.Substring(0, bracketIndex).Trim();
            }

            const string noCommitsPrefix = "No commits yet on ";
            if (text.StartsWith(noCommitsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                snapshot.Branch = text.Substring(noCommitsPrefix.Length).Trim();
            }
            else
            {
                var upstreamSeparator = text.IndexOf("...", StringComparison.Ordinal);
                if (upstreamSeparator >= 0)
                {
                    snapshot.Branch = text.Substring(0, upstreamSeparator).Trim();
                    snapshot.Upstream = text.Substring(upstreamSeparator + 3).Trim();
                }
                else if (text.StartsWith("HEAD detached at ", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Branch = "detached@" + text.Substring("HEAD detached at ".Length).Trim();
                }
                else if (text.StartsWith("HEAD detached from ", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Branch = "detached@" + text.Substring("HEAD detached from ".Length).Trim();
                }
                else if (text.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.Branch = "detached";
                }
                else
                {
                    snapshot.Branch = text;
                }
            }

            PopulateAheadBehindFromBranchStatus(snapshot, aheadBehindText);
        }

        private static void PopulateAheadBehindFromBranchStatus(GitRepositoryStatusSnapshot snapshot, string aheadBehindText)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(aheadBehindText))
            {
                return;
            }

            var parts = aheadBehindText.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                int value;
                if (!int.TryParse(parts[i + 1], out value))
                {
                    continue;
                }

                if (string.Equals(parts[i], "ahead", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.AheadCount = value;
                }
                else if (string.Equals(parts[i], "behind", StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.BehindCount = value;
                }
            }
        }

        private GitChangedFileEntry BuildChangedFileEntry(
            string workspacePath,
            string path,
            string originalPath,
            char indexStatus,
            char workTreeStatus,
            string group)
        {
            var entry = new GitChangedFileEntry
            {
                FilePath = path,
                OriginalFilePath = originalPath,
                StatusGroup = group,
                IsStaged = string.Equals(group, "staged", StringComparison.OrdinalIgnoreCase),
                IsUnstaged = string.Equals(group, "unstaged", StringComparison.OrdinalIgnoreCase),
                IsUntracked = string.Equals(group, "untracked", StringComparison.OrdinalIgnoreCase),
                IsConflicted = string.Equals(group, "conflict", StringComparison.OrdinalIgnoreCase),
                HasStagedChanges = indexStatus != ' ' && indexStatus != '?' && !IsConflictStatus(indexStatus, workTreeStatus),
                HasUnstagedChanges = workTreeStatus != ' ' && workTreeStatus != '?' && !IsConflictStatus(indexStatus, workTreeStatus),
                ChangeKind = MapStatusToChangeKind(indexStatus, workTreeStatus, group)
            };

            entry.CanStage = entry.IsUnstaged || entry.IsUntracked;
            entry.CanUnstage = entry.IsStaged;
            entry.CanDiscard = entry.IsUnstaged && !entry.IsConflicted;
            return entry;
        }

        private GitOperationResult ReadDiff(string workspacePath, GitChangedFileEntry entry, GitDiffKind diffKind)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.PathsRequired", "At least one file path is required."),
                    nameof(entry));
            }

            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);

            if (entry.IsUntracked)
            {
                return ReadUntrackedDiff(normalizedPath, entry);
            }

            var resolvedDiffKind = ResolveDiffKind(entry, diffKind);
            var arguments = resolvedDiffKind == GitDiffKind.Staged && !entry.IsConflicted
                ? Args("diff", "--cached", "--no-ext-diff", "--binary", "--", entry.FilePath)
                : Args("diff", "--no-ext-diff", "--binary", "--", entry.FilePath);
            var result = RunCommand(normalizedPath, arguments, 20000);
            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = result.Success ? (result.StandardOutput ?? string.Empty).Trim() : result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? SharedLocalization.Text("GitRepositoryService.Operation.ReadDiff", "Loaded selected diff.")
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private void PopulateStatusLineStats(string workspacePath, GitRepositoryStatusSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Files == null || snapshot.Files.Count == 0)
            {
                return;
            }

            PopulateStatusLineStats(
                snapshot,
                RunCommand(workspacePath, Args("diff", "--numstat", "--no-renames", "-z", "--no-ext-diff"), 15000),
                false);
            PopulateStatusLineStats(
                snapshot,
                RunCommand(workspacePath, Args("diff", "--cached", "--numstat", "--no-renames", "-z", "--no-ext-diff"), 15000),
                true);
        }

        private void PopulateStatusLineStats(
            GitRepositoryStatusSnapshot snapshot,
            GitCommandResult result,
            bool staged)
        {
            if (snapshot == null || result == null || !result.Success || string.IsNullOrEmpty(result.StandardOutput))
            {
                return;
            }

            var targetsByPath = BuildLineStatTargetLookup(snapshot, staged);
            if (targetsByPath.Count == 0)
            {
                return;
            }

            foreach (var record in SplitNullSeparated(result.StandardOutput))
            {
                var parts = record.Split(new[] { '\t' }, StringSplitOptions.None);
                if (parts.Length < 3)
                {
                    continue;
                }

                int added;
                int deleted;
                if (!int.TryParse(parts[0], out added) || !int.TryParse(parts[1], out deleted))
                {
                    continue;
                }

                var path = NormalizeGitFilePath(parts[2]);
                List<GitChangedFileEntry> entries;
                if (!targetsByPath.TryGetValue(path, out entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    entry.AddedLineCount += added;
                    entry.DeletedLineCount += deleted;
                    entry.HasLineStats = true;
                }
            }
        }

        private Dictionary<string, List<GitChangedFileEntry>> BuildLineStatTargetLookup(
            GitRepositoryStatusSnapshot snapshot,
            bool staged)
        {
            var lookup = new Dictionary<string, List<GitChangedFileEntry>>(StringComparer.OrdinalIgnoreCase);
            if (snapshot == null || snapshot.Files == null)
            {
                return lookup;
            }

            foreach (var entry in snapshot.Files)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    continue;
                }

                if (staged)
                {
                    if (!entry.HasStagedChanges && !entry.IsStaged)
                    {
                        continue;
                    }
                }
                else if (!entry.HasUnstagedChanges && !entry.IsUnstaged)
                {
                    continue;
                }

                var path = NormalizeGitFilePath(entry.FilePath);
                List<GitChangedFileEntry> entries;
                if (!lookup.TryGetValue(path, out entries))
                {
                    entries = new List<GitChangedFileEntry>();
                    lookup[path] = entries;
                }

                entries.Add(entry);
            }

            return lookup;
        }

        private static GitDiffKind ResolveDiffKind(GitChangedFileEntry entry, GitDiffKind diffKind)
        {
            if (diffKind == GitDiffKind.Staged || diffKind == GitDiffKind.Unstaged)
            {
                return diffKind;
            }

            return entry != null && entry.IsStaged && !entry.IsConflicted
                ? GitDiffKind.Staged
                : GitDiffKind.Unstaged;
        }

        private GitOperationResult ReadUntrackedDiff(string workspacePath, GitChangedFileEntry entry)
        {
            var result = RunCommand(
                workspacePath,
                Args("diff", "--no-index", "--", "/dev/null", entry.FilePath),
                20000);
            var output = (result.StandardOutput ?? string.Empty).Trim();
            var success = result.Success || (result.ExitCode == 1 && !string.IsNullOrWhiteSpace(output));
            if (success)
            {
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = string.Format(
                        SharedLocalization.Text(
                            "GitRepositoryService.Untracked.Empty.Template",
                            "Untracked file: {0}\r\n\r\nThis file is empty."),
                        entry.FilePath);
                }

                return new GitOperationResult
                {
                    Success = true,
                    ExitCode = result.ExitCode,
                    StandardOutput = output,
                    StandardError = result.StandardError,
                    DisplayMessage = SharedLocalization.Text("GitRepositoryService.Operation.ReadDiff", "Loaded selected diff.")
                };
            }

            return new GitOperationResult
            {
                Success = false,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private GitOperationResult ReadConflictDetails(string workspacePath, GitChangedFileEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.PathsRequired", "At least one file path is required."),
                    nameof(entry));
            }

            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(
                normalizedPath,
                Args("diff", "--cc", "--no-ext-diff", "--", entry.FilePath),
                20000);
            var output = result.Success ? (result.StandardOutput ?? string.Empty).Trim() : result.StandardOutput;
            if (result.Success && string.IsNullOrWhiteSpace(output))
            {
                output = string.Format(
                    SharedLocalization.Text(
                        "GitRepositoryService.Conflict.EmptyDetails.Template",
                        "No conflict diff is available for {0}. Refresh Git status and make sure the file is still conflicted."),
                    entry.FilePath);
            }

            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = output,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? SharedLocalization.Text("GitRepositoryService.Operation.ReadConflictDetails", "Loaded conflict details.")
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private void PopulateAheadBehind(string workspacePath, GitRepositoryStatusSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Upstream))
            {
                return;
            }

            var result = RunCommand(workspacePath, Args("rev-list", "--left-right", "--count", "HEAD...@{u}"), 15000);
            if (!result.Success)
            {
                return;
            }

            var parts = (result.StandardOutput ?? string.Empty)
                .Trim()
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int ahead;
            int behind;
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], out ahead) &&
                int.TryParse(parts[1], out behind))
            {
                snapshot.AheadCount = ahead;
                snapshot.BehindCount = behind;
            }
        }

        private int ReadStashCount(string workspacePath)
        {
            var result = RunCommand(workspacePath, Args("stash", "list", "--format=%gd"), 15000);
            if (!result.Success)
            {
                return 0;
            }

            return (result.StandardOutput ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        private void PopulateSequencerState(string workspacePath, GitRepositoryStatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.IsCherryPickInProgress = GitRefExists(workspacePath, "CHERRY_PICK_HEAD");
            snapshot.IsRevertInProgress = GitRefExists(workspacePath, "REVERT_HEAD");
            snapshot.IsMergeInProgress = GitRefExists(workspacePath, "MERGE_HEAD");
            snapshot.IsRebaseInProgress = GitRefExists(workspacePath, "REBASE_HEAD");
        }

        private bool GitRefExists(string workspacePath, string refName)
        {
            if (string.IsNullOrWhiteSpace(refName))
            {
                return false;
            }

            var result = RunCommand(workspacePath, Args("rev-parse", "-q", "--verify", refName), 15000);
            return result.Success;
        }

        private static void PopulateStatusCounters(GitRepositoryStatusSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.StagedCount = snapshot.Files.Count(item => item.IsStaged);
            snapshot.UnstagedCount = snapshot.Files.Count(item => item.IsUnstaged);
            snapshot.UntrackedCount = snapshot.Files.Count(item => item.IsUntracked);
            snapshot.ConflictedCount = snapshot.Files.Count(item => item.IsConflicted);
            snapshot.HasConflicts = snapshot.ConflictedCount > 0;
            snapshot.IsDetachedHead = !string.IsNullOrWhiteSpace(snapshot.Branch) &&
                                      snapshot.Branch.StartsWith("detached", StringComparison.OrdinalIgnoreCase);
        }

        private string ReadOptionalValue(string workspacePath, IReadOnlyList<string> arguments)
        {
            var result = RunCommand(workspacePath, arguments, 15000);
            return result.Success ? (result.StandardOutput ?? string.Empty).Trim() : null;
        }

        private GitOperationResult RunPathOperation(
            string workspacePath,
            IReadOnlyList<string> paths,
            Func<IReadOnlyList<string>, IReadOnlyList<string>> buildArguments,
            string successMessage)
        {
            if (paths == null || paths.Count == 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.PathsRequired", "At least one file path is required."),
                    nameof(paths));
            }

            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var pathArguments = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .ToArray();
            if (pathArguments.Length == 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.PathsRequired", "At least one file path is required."),
                    nameof(paths));
            }

            return RunOperation(normalizedPath, buildArguments(pathArguments), successMessage);
        }

        private GitOperationResult RunOperation(
            string workspacePath,
            IReadOnlyList<string> arguments,
            string successMessage,
            int timeoutMilliseconds = 15000)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var result = RunCommand(normalizedPath, arguments, timeoutMilliseconds);
            return new GitOperationResult
            {
                Success = result.Success,
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                DisplayMessage = result.Success
                    ? successMessage
                    : TextValueHelper.FirstNonEmptyTrimmed(result.StandardError, result.StandardOutput, SharedLocalization.Text("GitRepositoryService.Error.OperationFailed", "Git operation failed."))
            };
        }

        private GitOperationResult RunSequencerOperation(string workspacePath, string actionArgument, string successMessage)
        {
            var normalizedPath = NormalizeExistingWorkspacePath(workspacePath);
            var commandName = ResolveSequencerCommandName(normalizedPath);
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return new GitOperationResult
                {
                    Success = false,
                    ExitCode = 1,
                    DisplayMessage = SharedLocalization.Text("GitRepositoryService.Error.NoSequencerOperation", "No merge, rebase, cherry-pick, or revert operation is in progress.")
                };
            }

            if (string.Equals(commandName, "merge", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(actionArgument, "--skip", StringComparison.OrdinalIgnoreCase))
            {
                return new GitOperationResult
                {
                    Success = false,
                    ExitCode = 1,
                    DisplayMessage = SharedLocalization.Text("GitRepositoryService.Error.MergeSkipUnsupported", "Skip is not supported for merge operations.")
                };
            }

            return RunOperation(normalizedPath, Args(commandName, actionArgument), successMessage);
        }

        private string ResolveSequencerCommandName(string workspacePath)
        {
            if (GitRefExists(workspacePath, "CHERRY_PICK_HEAD"))
            {
                return "cherry-pick";
            }

            if (GitRefExists(workspacePath, "REVERT_HEAD"))
            {
                return "revert";
            }

            if (GitRefExists(workspacePath, "MERGE_HEAD"))
            {
                return "merge";
            }

            return GitRefExists(workspacePath, "REBASE_HEAD") ? "rebase" : null;
        }

        private string NormalizeExistingWorkspacePath(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.WorkspacePathRequired", "Workspace path cannot be empty."),
                    nameof(workspacePath));
            }

            if (_normalizeWorkspacePath != null)
            {
                return _normalizeWorkspacePath(workspacePath);
            }

            var normalizedPath = _workspaceService.NormalizeWorkspacePath(workspacePath);
            if (!Directory.Exists(normalizedPath))
            {
                throw new DirectoryNotFoundException(
                    SharedLocalization.Text("GitRepositoryService.Error.WorkspaceNotFound", "The specified workspace does not exist."));
            }

            return normalizedPath;
        }

        private static bool IsAlreadyUpToDateResult(GitOperationResult result)
        {
            if (result == null || !result.Success)
            {
                return false;
            }

            var combined = TextValueHelper.FirstNonEmptyTrimmed(result.StandardOutput, result.StandardError);
            return combined.IndexOf("already up to date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("is up to date", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ThrowGitReadFailure(string resourceKey, string fallback, GitCommandResult result)
        {
            var prefix = SharedLocalization.Text(resourceKey, fallback);
            var details = TextValueHelper.FirstNonEmptyTrimmed(
                result == null ? null : result.StandardError,
                result == null ? null : result.StandardOutput);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                ? prefix
                : prefix + Environment.NewLine + details);
        }

        private static bool IsEmptyCommitHistoryResult(GitCommandResult result)
        {
            var text = TextValueHelper.FirstNonEmptyTrimmed(
                result == null ? null : result.StandardError,
                result == null ? null : result.StandardOutput);
            return text.IndexOf("does not have any commits", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("your current branch", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   text.IndexOf("does not have any commits", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeBranchName(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.BranchRequired", "Branch name cannot be empty."),
                    nameof(branchName));
            }

            var normalized = branchName.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.BranchNameInvalid", "Branch name cannot start with a dash."),
                    nameof(branchName));
            }

            return normalized;
        }

        private static string NormalizeResetMode(GitResetMode mode)
        {
            switch (mode)
            {
                case GitResetMode.Soft:
                    return "--soft";
                case GitResetMode.Mixed:
                    return "--mixed";
                case GitResetMode.Hard:
                    return "--hard";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mode),
                        SharedLocalization.Text("GitRepositoryService.Error.ResetModeInvalid", "Reset mode is invalid."));
            }
        }

        private static string NormalizeStashRef(string stashRef)
        {
            if (string.IsNullOrWhiteSpace(stashRef))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.StashRefRequired", "Stash reference cannot be empty."),
                    nameof(stashRef));
            }

            var normalized = stashRef.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.StashRefInvalid", "Stash reference cannot start with a dash."),
                    nameof(stashRef));
            }

            return normalized;
        }

        private static string NormalizeWorktreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.WorktreePathRequired", "Worktree path cannot be empty."),
                    nameof(path));
            }

            var normalized = path.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.WorktreePathInvalid", "Worktree path cannot start with a dash."),
                    nameof(path));
            }

            return normalized;
        }

        private static string NormalizeCommitRef(string commitRef)
        {
            if (string.IsNullOrWhiteSpace(commitRef))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.CommitRefRequired", "Commit ID cannot be empty."),
                    nameof(commitRef));
            }

            var normalized = commitRef.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.CommitRefInvalid", "Commit ID cannot start with a dash."),
                    nameof(commitRef));
            }

            return normalized;
        }

        private static string NormalizeRemoteName(string remoteName)
        {
            if (string.IsNullOrWhiteSpace(remoteName))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.RemoteNameRequired", "Remote name cannot be empty."),
                    nameof(remoteName));
            }

            var normalized = remoteName.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal) ||
                normalized.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.RemoteNameInvalid", "Remote name is invalid."),
                    nameof(remoteName));
            }

            return normalized;
        }

        private static string NormalizeRemoteUrl(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.RemoteUrlRequired", "Remote URL cannot be empty."),
                    nameof(remoteUrl));
            }

            var normalized = remoteUrl.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.RemoteUrlInvalid", "Remote URL is invalid."),
                    nameof(remoteUrl));
            }

            return normalized;
        }

        private static string[] SplitNullSeparated(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsConflictStatus(char x, char y)
        {
            if (x == 'U' || y == 'U')
            {
                return true;
            }

            return (x == 'A' && y == 'A') ||
                   (x == 'D' && y == 'D');
        }

        private static bool IsRenameStatus(char x, char y)
        {
            return x == 'R' || y == 'R';
        }

        private static string MapStatusToChangeKind(char x, char y, string group)
        {
            if (string.Equals(group, "conflict", StringComparison.OrdinalIgnoreCase))
            {
                return "unmerged";
            }

            if (string.Equals(group, "untracked", StringComparison.OrdinalIgnoreCase))
            {
                return "untracked";
            }

            var status = string.Equals(group, "staged", StringComparison.OrdinalIgnoreCase) ? x : y;
            switch (status)
            {
                case 'A':
                    return "added";
                case 'D':
                    return "deleted";
                case 'T':
                    return "type_changed";
                case 'R':
                    return "renamed";
                case 'M':
                default:
                    return "modified";
            }
        }

        private string NormalizeGitFilePath(string value)
        {
            var normalized = UnquoteGitPathToken((value ?? string.Empty).Trim());
            return _useUnixFilePaths
                ? normalized.Replace('\\', '/')
                : normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string UnquoteGitPathToken(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length >= 2 &&
                normalized[0] == '"' &&
                normalized[normalized.Length - 1] == '"')
            {
                return normalized.Substring(1, normalized.Length - 2);
            }

            return normalized;
        }

        private static IReadOnlyList<string> NormalizeCommitRefs(IReadOnlyList<string> commitRefs)
        {
            if (commitRefs == null || commitRefs.Count == 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.CommitRefRequired", "Commit ID cannot be empty."),
                    nameof(commitRefs));
            }

            var normalizedRefs = commitRefs
                .Where(commitRef => !string.IsNullOrWhiteSpace(commitRef))
                .Select(NormalizeCommitRef)
                .ToArray();
            if (normalizedRefs.Length == 0)
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.CommitRefRequired", "Commit ID cannot be empty."),
                    nameof(commitRefs));
            }

            return normalizedRefs;
        }

        private static string NormalizeRevisionRef(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision))
            {
                return null;
            }

            var normalized = revision.Trim();
            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.RevisionInvalid", "Revision cannot start with a dash."),
                    nameof(revision));
            }

            return normalized;
        }

        private static string MapNameStatusToChangeKind(string status)
        {
            var normalized = (status ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return "modified";
            }

            switch (char.ToUpperInvariant(normalized[0]))
            {
                case 'A':
                    return "added";
                case 'D':
                    return "deleted";
                case 'R':
                    return "renamed";
                case 'C':
                    return "copied";
                case 'T':
                    return "type_changed";
                case 'U':
                    return "unmerged";
                case 'M':
                default:
                    return "modified";
            }
        }

        public static void PopulateDiffLineCounts(GitChangedFileEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var counts = UnifiedDiffLineCounter.Count(entry.DiffText);
            entry.AddedLineCount = counts.Additions;
            entry.DeletedLineCount = counts.Deletions;
            if (!string.IsNullOrWhiteSpace(entry.DiffText))
            {
                entry.HasLineStats = true;
            }
        }

        private static string[] Args(params string[] arguments)
        {
            return arguments ?? new string[0];
        }

        private static IReadOnlyList<string> BuildCommitArguments(bool amend, string subject, string body)
        {
            var trimmedSubject = (subject ?? string.Empty).Trim();
            var trimmedBody = (body ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedSubject))
            {
                if (amend && string.IsNullOrWhiteSpace(trimmedBody))
                {
                    return Args("commit", "--amend", "--no-edit");
                }

                throw new ArgumentException(
                    SharedLocalization.Text("GitRepositoryService.Error.CommitMessageRequired", "Commit message cannot be empty."),
                    nameof(subject));
            }

            var arguments = new List<string> { "commit" };
            if (amend)
            {
                arguments.Add("--amend");
            }

            arguments.Add("-m");
            arguments.Add(trimmedSubject);
            if (!string.IsNullOrWhiteSpace(trimmedBody))
            {
                arguments.Add("-m");
                arguments.Add(trimmedBody);
            }

            return arguments;
        }
    }
}
