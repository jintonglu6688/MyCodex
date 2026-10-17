using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationRunActionCallbacks
    {
        public Func<AutomationRunSnapshot, bool> CanStopRun { get; set; }

        public Func<AutomationRunSnapshot, Task> StopRunAsync { get; set; }

        public Func<AutomationRunSnapshot, bool> CanOpenThread { get; set; }

        public Func<AutomationRunSnapshot, Task> OpenThreadAsync { get; set; }

        public Func<AutomationRunSnapshot, Task> DeleteLocalHistoryAsync { get; set; }

        public Func<string, Task> OpenPathAsync { get; set; }

        public Func<AutomationRunSnapshot, Task<IReadOnlyList<ChatHistoryItem>>> LoadConversationAsync { get; set; }
    }
}
