namespace MyCodex.Domain.Enums
{
    public enum CustomApprovalPermissionKind
    {
        Unknown = 0,
        GitInspect = 10,
        GitStage = 11,
        GitLocalCommit = 12,
        GitInitialize = 13,
        GitConfigurationChange = 14,
        GitWorkingTreeChange = 15,
        GitRemoteRead = 16,
        GitRemoteWrite = 17,
        GitDestructive = 18,
        CommandBuild = 30,
        CommandTest = 31,
        CommandDependencyInstall = 32,
        CommandGeneric = 33,
        FileRead = 50,
        FileCreate = 51,
        FileModify = 52,
        FileDelete = 53,
        FileMove = 54,
        FileMixed = 55,
        FileUnknown = 56,
        PermissionExtraReadRoots = 70,
        PermissionExtraWriteRoots = 71,
        PermissionNetwork = 72,
        PermissionMixed = 73,
        RequestUnknown = 90
    }
}
