using System;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionWorkbenchOperations
    {
        internal SessionHistoryLoadWorkflow HistoryLoadWorkflow { get; set; }

        internal ChatWorkbenchHostCallbacks WorkbenchCallbacks { get; set; }

        internal ManualBrowseContext ManualBrowseContext { get; set; }

        internal Func<ChatSession, ChatWorkbenchSendRequest, Task<ChatWorkbenchSendResult>> SendAsync { get; set; }

        internal Action PlayMessageSentSound { get; set; }

        internal Func<bool> CanCloseWithEscape { get; set; }

        internal Action<ChatHistorySelectionSnapshot> SaveMessageSelection { get; set; }

        internal Func<ChatHistorySelectionSnapshot> LoadMessageSelection { get; set; }

        internal Func<ChatSession, Task> StopAsync { get; set; }

        internal Func<ChatSession, bool> IsWorking { get; set; }

        internal Action OpenFileBrowser { get; set; }

        internal Action OpenGitWorkbench { get; set; }

        internal Action OpenSubAgents { get; set; }

        internal Func<bool> CanOpenSubAgents { get; set; }

        internal Action OpenTerminal { get; set; }

        internal Func<ComposeDraftState> LoadDraft { get; set; }

        internal Action<ComposeDraftState> SaveDraft { get; set; }

        internal Action ClearDraft { get; set; }

        internal Action DiscardDraft { get; set; }

        internal Action PersistState { get; set; }
    }
}
