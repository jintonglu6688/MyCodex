namespace MyCodex.Application.DTOs
{
    public enum ApprovalRequestCategory
    {
        Unknown = 0,
        CommandGeneric = 1,
        CommandBuild = 2,
        CommandTest = 3,
        CommandDependencyInstall = 4,
        GitInspect = 10,
        GitStage = 11,
        GitLocalCommit = 12,
        GitLocalWorkingTreeChange = 13,
        GitRemoteRead = 14,
        GitRemoteWrite = 15,
        GitDestructive = 16,
        GitInitialize = 17,
        GitLocalConfiguration = 18,
        FileRead = 30,
        FileWrite = 31,
        FileCreate = 32,
        FileModify = 33,
        FileDelete = 34,
        FileMove = 35,
        FileMixed = 36,
        FileUnknown = 37,
        PermissionExtraReadRoots = 50,
        PermissionExtraWriteRoots = 51,
        PermissionNetwork = 52,
        PermissionMixed = 53
    }
}
