using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    /// <summary>
    /// Windows can activate a minimized window without restoring it, for example on Alt+Tab. No child control
    /// can take keyboard focus then, so Tab and every other key do nothing until the window is restored.
    /// </summary>
    internal sealed class MinimizedActivationRestorer
    {
        private const int ShowWindowRestore = 9;
        private readonly Form _form;

        internal MinimizedActivationRestorer(Form form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _form.Activated += OnActivated;
        }

        internal Func<IntPtr> ForegroundWindowProvider { get; set; } = NativeMethods.GetForegroundWindow;

        private void OnActivated(object sender, EventArgs e)
        {
            if (_form.WindowState == FormWindowState.Minimized)
            {
                // Let the activation finish; a normal restore may still be on its way.
                _form.BeginInvokeSafe(RestoreIfStillMinimized);
            }
        }

        internal void RestoreIfStillMinimized()
        {
            if (_form.IsDisposed || !_form.IsHandleCreated || !_form.Visible ||
                _form.WindowState != FormWindowState.Minimized ||
                ForegroundWindowProvider() != _form.Handle)
            {
                return;
            }

            // SW_RESTORE returns a maximized window to maximized, unlike setting WindowState to Normal.
            NativeMethods.ShowWindow(_form.Handle, ShowWindowRestore);
            if (!_form.ContainsFocus)
            {
                FocusInnermostActiveControl();
            }
        }

        private void FocusInnermostActiveControl()
        {
            Control target = _form.ActiveControl;
            var container = target as ContainerControl;
            while (container != null && container.ActiveControl != null)
            {
                target = container.ActiveControl;
                container = target as ContainerControl;
            }

            if (target == null || !target.CanFocus || !target.Focus())
            {
                _form.SelectNextControl(null, true, true, true, false);
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int command);
        }
    }
}
