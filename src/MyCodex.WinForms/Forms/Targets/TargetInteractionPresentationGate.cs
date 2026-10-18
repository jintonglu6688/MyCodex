using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    internal static class TargetInteractionPresentationGate
    {
        internal static async Task<bool> WaitAsync(Form lifetime, RemoteInteractionDialogRegistry dialogs,
            string requestId, Func<bool> canPresent, Func<bool> isCurrent)
        {
            if (lifetime.IsDisposed || !isCurrent()) return false;
            if (canPresent()) return true;

            var ready = new TaskCompletionSource<bool>();
            EventHandler disposed = (sender, args) => ready.TrySetResult(false);
            using (var timer = new Timer { Interval = 100 })
            using (dialogs.Register(requestId, () => ready.TrySetResult(false)))
            {
                timer.Tick += (sender, args) =>
                {
                    if (lifetime.IsDisposed || !isCurrent()) ready.TrySetResult(false);
                    else if (canPresent()) ready.TrySetResult(true);
                };
                lifetime.Disposed += disposed;
                try
                {
                    timer.Start();
                    return await ready.Task;
                }
                finally { lifetime.Disposed -= disposed; }
            }
        }
    }
}
