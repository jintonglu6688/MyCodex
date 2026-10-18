using System;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task<SessionDefaultsResolution> PrepareSendSettingsAsync(ChatSession session, WorkspaceInfo workspace)
        {
            try { return await ReadSessionModelSettingsAsync(session, workspace); }
            catch (OperationCanceledException) { return null; }
            catch (Exception error)
            {
                TraceSendDiagnosticError("Configuration", error);
                SetHighPriorityStatus(error is InvalidOperationException ? error.Message : UiLocalization.Text("MainForm.ModelConfig.ReadFailed",
                    "Could not refresh official model settings. Try again before sending."));
                return null;
            }
        }

        private void ScheduleSendMetadataUpdate(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId)) return;
            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (workspace == null) return;
            var scope = BuildModelConfigurationScope();
            var thread = session.ThreadId;
            var title = session.Title;
            var path = workspace.RootPath;
            _ = _sessionThreadService.SynchronizeMetadataAsync(session, workspace, scope,
                () => !IsDisposed && !Disposing && scope == BuildModelConfigurationScope() &&
                    ReferenceEquals(FindSessionByThreadId(thread), session) && session.Title == title &&
                    session.ThreadId == thread && workspace.RootPath == path);
        }
    }
}
