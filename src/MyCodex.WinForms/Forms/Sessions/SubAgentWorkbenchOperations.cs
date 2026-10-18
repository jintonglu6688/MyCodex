using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SubAgentWorkbenchOperations
    {
        public WorkspaceInfo Workspace { get; set; }

        public SessionHistoryLoadWorkflow HistoryLoadWorkflow { get; set; }

        public ChatWorkbenchHostCallbacks WorkbenchCallbacks { get; set; }

        public Func<ChatSession, ChatWorkbenchSendRequest, Task<ChatWorkbenchSendResult>> SendAsync { get; set; }

        public Func<ChatSession, Task> StopAsync { get; set; }

        public Func<ChatSession, bool> CanStop { get; set; }

        public Func<ChatSession, bool> IsWorking { get; set; }

        public Func<IReadOnlyList<ChatSession>, bool, bool> ConfirmDelete { get; set; }

        public Func<IReadOnlyList<ChatSession>, Task> DeleteAsync { get; set; }

        public Action PersistState { get; set; }

        public Func<Task> RefreshAsync { get; set; }

        public Func<string, AppServerThreadSummary> GetSummary { get; set; }

        public Func<string, string> GetRecentWork { get; set; }

        public Func<string, string> GetAgentStatus { get; set; }
    }
}
