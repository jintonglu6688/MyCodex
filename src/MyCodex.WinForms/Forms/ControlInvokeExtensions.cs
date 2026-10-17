using System;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    internal static class ControlInvokeExtensions
    {
        public static bool BeginInvokeSafe(this Control control, Action action)
        {
            if (control == null || action == null || control.IsDisposed || control.Disposing)
            {
                return false;
            }

            if (control.IsHandleCreated)
            {
                try
                {
                    control.BeginInvoke(action);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            EventHandler handleCreatedHandler = null;
            handleCreatedHandler = delegate
            {
                control.HandleCreated -= handleCreatedHandler;
                if (control.IsDisposed || control.Disposing)
                {
                    return;
                }

                try
                {
                    control.BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                }
            };

            control.HandleCreated += handleCreatedHandler;

            if (control.IsDisposed || control.Disposing)
            {
                control.HandleCreated -= handleCreatedHandler;
                return false;
            }

            if (control.IsHandleCreated)
            {
                control.HandleCreated -= handleCreatedHandler;
                try
                {
                    control.BeginInvoke(action);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
