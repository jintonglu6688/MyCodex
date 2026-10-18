using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class CustomApprovalPermissionDisplayText
    {
        internal static string Get(CustomApprovalPermissionKind permission)
        {
            switch (permission)
            {
                case CustomApprovalPermissionKind.GitInspect:
                    return UiLocalization.Text("CustomApprovalPermission.GitInspect", "Git inspect");
                case CustomApprovalPermissionKind.GitStage:
                    return UiLocalization.Text("CustomApprovalPermission.GitStage", "Git stage");
                case CustomApprovalPermissionKind.GitLocalCommit:
                    return UiLocalization.Text("CustomApprovalPermission.GitLocalCommit", "Git local commit");
                case CustomApprovalPermissionKind.GitInitialize:
                    return UiLocalization.Text("CustomApprovalPermission.GitInitialize", "Git initialize repository");
                case CustomApprovalPermissionKind.GitConfigurationChange:
                    return UiLocalization.Text("CustomApprovalPermission.GitConfigurationChange", "Git configuration change");
                case CustomApprovalPermissionKind.GitWorkingTreeChange:
                    return UiLocalization.Text("CustomApprovalPermission.GitWorkingTreeChange", "Git working tree change");
                case CustomApprovalPermissionKind.GitRemoteRead:
                    return UiLocalization.Text("CustomApprovalPermission.GitRemoteRead", "Git remote read");
                case CustomApprovalPermissionKind.GitRemoteWrite:
                    return UiLocalization.Text("CustomApprovalPermission.GitRemoteWrite", "Git remote write");
                case CustomApprovalPermissionKind.GitDestructive:
                    return UiLocalization.Text("CustomApprovalPermission.GitDestructive", "Git destructive operation");
                case CustomApprovalPermissionKind.CommandBuild:
                    return UiLocalization.Text("CustomApprovalPermission.CommandBuild", "Build command");
                case CustomApprovalPermissionKind.CommandTest:
                    return UiLocalization.Text("CustomApprovalPermission.CommandTest", "Test command");
                case CustomApprovalPermissionKind.CommandDependencyInstall:
                    return UiLocalization.Text("CustomApprovalPermission.CommandDependencyInstall", "Dependency install command");
                case CustomApprovalPermissionKind.CommandGeneric:
                    return UiLocalization.Text("CustomApprovalPermission.CommandGeneric", "Generic or unclassified command");
                case CustomApprovalPermissionKind.FileRead:
                    return UiLocalization.Text("CustomApprovalPermission.FileRead", "Read file");
                case CustomApprovalPermissionKind.FileCreate:
                    return UiLocalization.Text("CustomApprovalPermission.FileCreate", "Create file");
                case CustomApprovalPermissionKind.FileModify:
                    return UiLocalization.Text("CustomApprovalPermission.FileModify", "Write or modify file");
                case CustomApprovalPermissionKind.FileDelete:
                    return UiLocalization.Text("CustomApprovalPermission.FileDelete", "Delete file");
                case CustomApprovalPermissionKind.FileMove:
                    return UiLocalization.Text("CustomApprovalPermission.FileMove", "Move or rename file");
                case CustomApprovalPermissionKind.FileMixed:
                    return UiLocalization.Text("CustomApprovalPermission.FileMixed", "Mixed file change");
                case CustomApprovalPermissionKind.FileUnknown:
                    return UiLocalization.Text("CustomApprovalPermission.FileUnknown", "Unknown file change");
                case CustomApprovalPermissionKind.PermissionExtraReadRoots:
                    return UiLocalization.Text("CustomApprovalPermission.PermissionExtraReadRoots", "Extra read roots");
                case CustomApprovalPermissionKind.PermissionExtraWriteRoots:
                    return UiLocalization.Text("CustomApprovalPermission.PermissionExtraWriteRoots", "Extra write roots");
                case CustomApprovalPermissionKind.PermissionNetwork:
                    return UiLocalization.Text("CustomApprovalPermission.PermissionNetwork", "Network access");
                case CustomApprovalPermissionKind.PermissionMixed:
                    return UiLocalization.Text("CustomApprovalPermission.PermissionMixed", "Mixed permission request");
                default:
                    return UiLocalization.Text("CustomApprovalPermission.RequestUnknown", "Unknown request");
            }
        }
    }
}
