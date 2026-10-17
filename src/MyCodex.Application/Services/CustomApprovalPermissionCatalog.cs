using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public static class CustomApprovalPermissionCatalog
    {
        private static readonly CustomApprovalPermissionKind[] OrderedPermissions =
        {
            CustomApprovalPermissionKind.GitInspect,
            CustomApprovalPermissionKind.GitStage,
            CustomApprovalPermissionKind.GitLocalCommit,
            CustomApprovalPermissionKind.GitInitialize,
            CustomApprovalPermissionKind.GitConfigurationChange,
            CustomApprovalPermissionKind.GitWorkingTreeChange,
            CustomApprovalPermissionKind.GitRemoteRead,
            CustomApprovalPermissionKind.GitRemoteWrite,
            CustomApprovalPermissionKind.GitDestructive,
            CustomApprovalPermissionKind.CommandBuild,
            CustomApprovalPermissionKind.CommandTest,
            CustomApprovalPermissionKind.CommandDependencyInstall,
            CustomApprovalPermissionKind.CommandGeneric,
            CustomApprovalPermissionKind.FileRead,
            CustomApprovalPermissionKind.FileCreate,
            CustomApprovalPermissionKind.FileModify,
            CustomApprovalPermissionKind.FileDelete,
            CustomApprovalPermissionKind.FileMove,
            CustomApprovalPermissionKind.FileMixed,
            CustomApprovalPermissionKind.FileUnknown,
            CustomApprovalPermissionKind.PermissionExtraReadRoots,
            CustomApprovalPermissionKind.PermissionExtraWriteRoots,
            CustomApprovalPermissionKind.PermissionNetwork,
            CustomApprovalPermissionKind.PermissionMixed,
            CustomApprovalPermissionKind.RequestUnknown
        };

        public static CustomApprovalPermissionKind[] GetOrderedPermissions()
        {
            return (CustomApprovalPermissionKind[])OrderedPermissions.Clone();
        }

        public static bool TryMapCategory(ApprovalRequestCategory category, out CustomApprovalPermissionKind permission)
        {
            switch (category)
            {
                case ApprovalRequestCategory.GitInspect:
                    permission = CustomApprovalPermissionKind.GitInspect;
                    return true;
                case ApprovalRequestCategory.GitStage:
                    permission = CustomApprovalPermissionKind.GitStage;
                    return true;
                case ApprovalRequestCategory.GitLocalCommit:
                    permission = CustomApprovalPermissionKind.GitLocalCommit;
                    return true;
                case ApprovalRequestCategory.GitInitialize:
                    permission = CustomApprovalPermissionKind.GitInitialize;
                    return true;
                case ApprovalRequestCategory.GitLocalConfiguration:
                    permission = CustomApprovalPermissionKind.GitConfigurationChange;
                    return true;
                case ApprovalRequestCategory.GitLocalWorkingTreeChange:
                    permission = CustomApprovalPermissionKind.GitWorkingTreeChange;
                    return true;
                case ApprovalRequestCategory.GitRemoteRead:
                    permission = CustomApprovalPermissionKind.GitRemoteRead;
                    return true;
                case ApprovalRequestCategory.GitRemoteWrite:
                    permission = CustomApprovalPermissionKind.GitRemoteWrite;
                    return true;
                case ApprovalRequestCategory.GitDestructive:
                    permission = CustomApprovalPermissionKind.GitDestructive;
                    return true;
                case ApprovalRequestCategory.CommandBuild:
                    permission = CustomApprovalPermissionKind.CommandBuild;
                    return true;
                case ApprovalRequestCategory.CommandTest:
                    permission = CustomApprovalPermissionKind.CommandTest;
                    return true;
                case ApprovalRequestCategory.CommandDependencyInstall:
                    permission = CustomApprovalPermissionKind.CommandDependencyInstall;
                    return true;
                case ApprovalRequestCategory.CommandGeneric:
                    permission = CustomApprovalPermissionKind.CommandGeneric;
                    return true;
                case ApprovalRequestCategory.FileRead:
                    permission = CustomApprovalPermissionKind.FileRead;
                    return true;
                case ApprovalRequestCategory.FileCreate:
                    permission = CustomApprovalPermissionKind.FileCreate;
                    return true;
                case ApprovalRequestCategory.FileWrite:
                case ApprovalRequestCategory.FileModify:
                    permission = CustomApprovalPermissionKind.FileModify;
                    return true;
                case ApprovalRequestCategory.FileDelete:
                    permission = CustomApprovalPermissionKind.FileDelete;
                    return true;
                case ApprovalRequestCategory.FileMove:
                    permission = CustomApprovalPermissionKind.FileMove;
                    return true;
                case ApprovalRequestCategory.FileMixed:
                    permission = CustomApprovalPermissionKind.FileMixed;
                    return true;
                case ApprovalRequestCategory.FileUnknown:
                    permission = CustomApprovalPermissionKind.FileUnknown;
                    return true;
                case ApprovalRequestCategory.PermissionExtraReadRoots:
                    permission = CustomApprovalPermissionKind.PermissionExtraReadRoots;
                    return true;
                case ApprovalRequestCategory.PermissionExtraWriteRoots:
                    permission = CustomApprovalPermissionKind.PermissionExtraWriteRoots;
                    return true;
                case ApprovalRequestCategory.PermissionNetwork:
                    permission = CustomApprovalPermissionKind.PermissionNetwork;
                    return true;
                case ApprovalRequestCategory.PermissionMixed:
                    permission = CustomApprovalPermissionKind.PermissionMixed;
                    return true;
                case ApprovalRequestCategory.Unknown:
                    permission = CustomApprovalPermissionKind.RequestUnknown;
                    return true;
                default:
                    permission = CustomApprovalPermissionKind.RequestUnknown;
                    return false;
            }
        }
    }
}
