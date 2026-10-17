using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private const int WmHotKey = 0x0312;
        private MainWindowHotKeyRegistration _mainWindowHotKeyRegistration;
        private string _mainWindowHotKeyRegistrationError;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterMainWindowHotKey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterMainWindowHotKey();
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotKey && _mainWindowHotKeyRegistration != null &&
                _mainWindowHotKeyRegistration.IsRegistered &&
                m.WParam == (IntPtr)_mainWindowHotKeyRegistration.RegistrationId)
            {
                if (!_isExitRequested && !IsDisposed)
                {
                    HandleMainWindowHotKey();
                }

                return;
            }

            base.WndProc(ref m);
        }

        private void RegisterMainWindowHotKey()
        {
            if (IsDisposed || !IsHandleCreated) return;
            string error;
            if (!TryUpdateMainWindowHotKey(_appConfigSnapshot?.MainWindowHotKeyEnabled != false,
                GetMainWindowHotKeyModifiers(), GetMainWindowHotKeyVirtualKey(), () => { }, out error))
                SetLowPriorityStatus(error);
        }

        private void UnregisterMainWindowHotKey()
        {
            _mainWindowHotKeyRegistration?.Clear();
        }

        private int GetMainWindowHotKeyModifiers()
        {
            return MainWindowHotKeyConfig.NormalizeModifiers(
                _appConfigSnapshot == null
                    ? MainWindowHotKeyConfig.DefaultModifiers
                    : _appConfigSnapshot.MainWindowHotKeyModifiers);
        }

        private int GetMainWindowHotKeyVirtualKey()
        {
            return MainWindowHotKeyConfig.NormalizeVirtualKey(
                _appConfigSnapshot == null
                    ? MainWindowHotKeyConfig.DefaultVirtualKey
                    : _appConfigSnapshot.MainWindowHotKeyVirtualKey);
        }

        private void RefreshMainWindowHotKeyRegistration()
        {
            RegisterMainWindowHotKey();
            RefreshMainWindowHotKeyMenuText();
        }

        private bool TryUpdateMainWindowHotKey(bool enabled, int modifiers, int virtualKey, Action persist, out string error)
        {
            modifiers = MainWindowHotKeyConfig.NormalizeModifiers(modifiers);
            virtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(virtualKey);
            error = null;
            if (IsDisposed || !IsHandleCreated) return false;
            if (_mainWindowHotKeyRegistration == null)
                _mainWindowHotKeyRegistration = new MainWindowHotKeyRegistration(
                    (id, flags, key) => MainWindowHotKeyNativeMethods.RegisterHotKey(Handle, id, flags, key),
                    id => { if (IsHandleCreated) MainWindowHotKeyNativeMethods.UnregisterHotKey(Handle, id); });
            var config = _appConfigSnapshot ?? (_appConfigSnapshot = new AppConfigSnapshot());
            var previousEnabled = config.MainWindowHotKeyEnabled;
            var previousModifiers = config.MainWindowHotKeyModifiers;
            var previousVirtualKey = config.MainWindowHotKeyVirtualKey;
            try
            {
                Func<bool> update = () => _mainWindowHotKeyRegistration.TryUpdate(enabled, modifiers, virtualKey, () =>
                {
                    config.MainWindowHotKeyEnabled = enabled;
                    config.MainWindowHotKeyModifiers = modifiers;
                    config.MainWindowHotKeyVirtualKey = virtualKey;
                    persist();
                });
                var success = _targetCatalog == null ? update() : _targetCatalog.WithHotKeyReservation(
                    _fixedTarget.Id, enabled, modifiers, virtualKey, update);
                if (success)
                {
                    _mainWindowHotKeyRegistrationError = null;
                    RefreshMainWindowHotKeyMenuText();
                    return true;
                }
                error = string.Format(UiLocalization.Text("MainForm.GlobalHotKey.Status.Unavailable",
                    "Global shortcut {0} is unavailable. Another application or channel may be using it."),
                    MainWindowHotKeyShortcut.Format(modifiers, virtualKey));
            }
            catch (TargetHotKeyConflictException ex)
            {
                error = string.Format(UiLocalization.Text("MainForm.GlobalHotKey.Status.Conflict",
                    "Shortcut {0} is configured for {1}."), MainWindowHotKeyShortcut.Format(modifiers, virtualKey),
                    string.Join(", ", ex.Targets.Select(TargetManagerForm.DisplayName)));
            }
            catch (Exception ex) { error = ex.Message; }
            config.MainWindowHotKeyEnabled = previousEnabled;
            config.MainWindowHotKeyModifiers = previousModifiers;
            config.MainWindowHotKeyVirtualKey = previousVirtualKey;
            _mainWindowHotKeyRegistrationError = error;
            if (_mainWindowHotKeyRegistration.IsRegistered)
                error = string.Format(UiLocalization.Text("MainForm.GlobalHotKey.Status.ActiveAfterFailure",
                    "{0} Still using {1}."), error, MainWindowHotKeyShortcut.Format(
                        _mainWindowHotKeyRegistration.Modifiers, _mainWindowHotKeyRegistration.VirtualKey));
            Trace.TraceWarning(error);
            AppendLog(error);
            RefreshMainWindowHotKeyMenuText();
            return false;
        }

        private string GetMainWindowHotKeyStateText()
        {
            var state = _mainWindowHotKeyRegistration?.IsRegistered == true
                ? string.Format(UiLocalization.Text("MainForm.GlobalHotKey.Status.Registered", "Registered: {0}"),
                    MainWindowHotKeyShortcut.Format(_mainWindowHotKeyRegistration.Modifiers, _mainWindowHotKeyRegistration.VirtualKey))
                : _appConfigSnapshot?.MainWindowHotKeyEnabled == false
                    ? UiLocalization.Text("MainForm.GlobalHotKey.Status.NotSet", "Not set")
                    : UiLocalization.Text("MainForm.GlobalHotKey.Status.NotRegistered", "Registration failed");
            return string.IsNullOrWhiteSpace(_mainWindowHotKeyRegistrationError)
                ? state : _mainWindowHotKeyRegistrationError + " " + state;
        }

        private void RefreshMainWindowHotKeyMenuText()
        {
            if (_showMainWindowTrayMenuItem == null || _hideMainWindowTrayMenuItem == null)
            {
                return;
            }

            var shortcut = GetMainWindowHotKeyStateText().Replace("&", "&&");
            _showMainWindowTrayMenuItem.Text = string.Format(
                UiLocalization.Text("MainForm.Tray.ShowMainWindow", "Show Main Window ({0})(&S)"),
                shortcut);
            _hideMainWindowTrayMenuItem.Text = string.Format(
                UiLocalization.Text("MainForm.Tray.HideMainWindow", "Hide Main Window ({0})(&H)"),
                shortcut);
            RefreshTargetTrayMenuText();
        }

        private void HandleMainWindowHotKey()
        {
            if (TryActivateTargetModalWindow()) return;

            if (!Enabled)
            {
                return;
            }

            if (MainWindowHotKeyNativeMethods.GetForegroundWindow() == Handle)
            {
                HideMainWindowToTray();
                return;
            }

            ShowMainWindowFromTray();
        }

        private static class MainWindowHotKeyNativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();
        }
    }
}
