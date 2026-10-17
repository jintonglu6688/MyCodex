using System;

namespace MyCodex.WinForms.Forms
{
    internal sealed class DisposableAction : IDisposable
    {
        private Action _dispose;

        public DisposableAction(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            var dispose = _dispose;
            if (dispose == null)
            {
                return;
            }

            _dispose = null;
            dispose();
        }
    }
}
