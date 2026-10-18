using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationInputSelectionCallbacks
    {
        public Func<IWin32Window, Task<AutomationInputItemSnapshot>> SelectSkillAsync { get; set; }

        public Func<IWin32Window, Task<AutomationInputItemSnapshot>> SelectPluginAsync { get; set; }
    }
}
