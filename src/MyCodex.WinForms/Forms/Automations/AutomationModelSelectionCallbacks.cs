using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationModelSelectionCallbacks
    {
        public Func<Task<IReadOnlyList<AppServerModelOption>>> LoadModelsAsync { get; set; }

        public Func<string, bool> SupportsReasoningEffortForModel { get; set; }
    }
}
