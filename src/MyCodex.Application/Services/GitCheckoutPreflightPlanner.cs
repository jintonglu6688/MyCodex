using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public enum GitCheckoutPreflightDecision
    {
        Proceed = 0,
        BlockConflicts = 1,
        BlockActiveGitOperation = 2,
        PromptDirtyChanges = 3
    }

    public static class GitCheckoutPreflightPlanner
    {
        public static GitCheckoutPreflightDecision Evaluate(GitRepositoryStatusSnapshot status)
        {
            if (status == null || !status.IsGitRepository)
            {
                return GitCheckoutPreflightDecision.Proceed;
            }

            if (status.HasConflicts)
            {
                return GitCheckoutPreflightDecision.BlockConflicts;
            }

            if (status.HasSequencerOperation || status.HasMergeOrRebaseOperation)
            {
                return GitCheckoutPreflightDecision.BlockActiveGitOperation;
            }

            return status.Files.Count > 0
                ? GitCheckoutPreflightDecision.PromptDirtyChanges
                : GitCheckoutPreflightDecision.Proceed;
        }
    }
}
