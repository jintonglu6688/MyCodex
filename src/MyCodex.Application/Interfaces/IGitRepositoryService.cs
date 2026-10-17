namespace MyCodex.Application.Interfaces
{
    public interface IGitRepositoryService :
        IGitStatusService,
        IGitDiffReader,
        IGitCommitHistoryService,
        IGitBranchService,
        IGitRemoteService,
        IGitStashService,
        IGitWorktreeService,
        IGitRepositoryMaintenanceService
    {
    }
}
