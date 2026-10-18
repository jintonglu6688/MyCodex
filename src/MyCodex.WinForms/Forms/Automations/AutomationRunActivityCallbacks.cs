using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationRunActivityCallbacks
    {
        public Func<string, IReadOnlyList<string>> GetActivityItems { get; set; }

        public Func<string, Action<IReadOnlyList<string>>, IDisposable> SubscribeActivityUpdates { get; set; }
    }
}
