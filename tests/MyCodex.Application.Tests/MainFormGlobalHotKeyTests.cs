using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormGlobalHotKeyTests
    {
        [Test]
        public void GlobalHotKey_UsesWin32RegistrationWithNoRepeatAndHandleLifecycle()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.GlobalHotKey.cs");

            var registered = new List<uint>();
            var binding = new MainWindowHotKeyRegistration((id, modifiers, key) => { registered.Add(modifiers); return true; }, id => { });
            Assert.That(binding.TryUpdate(true, MainWindowHotKeyConfig.DefaultModifiers, MainWindowHotKeyConfig.DefaultVirtualKey, () => { }), Is.True);
            Assert.That(registered, Is.EqualTo(new[] { (uint)MainWindowHotKeyConfig.DefaultModifiers | MainWindowHotKeyRegistration.ModNoRepeat }));
            Assert.That(source, Does.Contain("_appConfigSnapshot.MainWindowHotKeyModifiers"));
            Assert.That(source, Does.Contain("_appConfigSnapshot.MainWindowHotKeyVirtualKey"));
            Assert.That(source, Does.Contain("RegisterHotKey"));
            Assert.That(source, Does.Contain("UnregisterHotKey"));
            Assert.That(source, Does.Contain("protected override void OnHandleCreated"));
            Assert.That(source, Does.Contain("protected override void OnHandleDestroyed"));
            Assert.That(source, Does.Contain("private const int WmHotKey = 0x0312;"));
            Assert.That(source, Does.Not.Contain("SetWindowsHookEx"));
        }

        [Test]
        public void GlobalHotKey_OnlyHandlesItsOwnMessageAndDoesNotAnnounceThroughTts()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.GlobalHotKey.cs");

            Assert.That(source, Does.Contain("m.Msg == WmHotKey && _mainWindowHotKeyRegistration != null"));
            Assert.That(source, Does.Contain("m.WParam == (IntPtr)_mainWindowHotKeyRegistration.RegistrationId"));
            Assert.That(source, Does.Contain("if (!_isExitRequested && !IsDisposed)"));
            Assert.That(source, Does.Not.Contain("Speak("));
            Assert.That(source, Does.Not.Contain("UIA"));
        }

        [Test]
        public void GlobalHotKey_UsesExistingTrayFlowAndLeavesDisabledMainWindowAlone()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.GlobalHotKey.cs");
            var handler = RepositorySource.ExtractMethod(source, "private void HandleMainWindowHotKey()");

            Assert.That(handler, Does.Contain("TryActivateTargetModalWindow()"));
            Assert.That(handler, Does.Contain("if (!Enabled)"));
            Assert.That(handler, Does.Contain("HideMainWindowToTray();"));
            Assert.That(handler, Does.Contain("ShowMainWindowFromTray();"));
            Assert.That(handler, Does.Contain("GetForegroundWindow() == Handle"));
            Assert.That(handler, Does.Not.Contain("_modeTabControl"));
            Assert.That(handler, Does.Not.Contain("SelectedTab"));
            Assert.That(handler, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(handler, Does.Not.Contain("SelectInteractionModeTab"));
        }

        [Test]
        public void GlobalHotKeyFailureAndTrayLabelsAreLocalized()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.GlobalHotKey.cs");
            var resources = RepositorySource.ReadFile("src", "MyCodex.WinForms", "Resources", "Strings.resx");
            var chineseResources = RepositorySource.ReadFile("src", "MyCodex.WinForms", "Resources", "Strings.zh-CN.resx");
            var englishManual = RepositorySource.ReadFile("help", "UserManual.en.html");
            var chineseManual = RepositorySource.ReadFile("help", "用户手册.html");

            Assert.That(source, Does.Contain("MainForm.GlobalHotKey.Status.Unavailable"));
            Assert.That(source, Does.Contain("Trace.TraceWarning(error);"));
            Assert.That(source, Does.Contain("AppendLog(error);"));
            Assert.That(resources, Does.Contain("MainForm.GlobalHotKey.Status.Unavailable"));
            Assert.That(chineseResources, Does.Contain("MainForm.GlobalHotKey.Status.Unavailable"));
            Assert.That(resources, Does.Contain("Show Main Window ({0})"));
            Assert.That(chineseResources, Does.Contain("（{0}）"));
            Assert.That(englishManual, Does.Contain("Shift+Alt+M"));
            Assert.That(englishManual, Does.Contain("Settings -&gt; Options... -&gt; Appearance"));
            Assert.That(chineseManual, Does.Contain("Shift+Alt+M"));
        }

        [Test]
        public void ShortcutCapture_AcceptsModifierAndNonModifierKey()
        {
            int modifiers;
            int virtualKey;

            var accepted = MainWindowHotKeyShortcut.TryCreate(
                Keys.Control | Keys.Shift | Keys.F9,
                false,
                out modifiers,
                out virtualKey);

            Assert.That(accepted, Is.True);
            Assert.That(modifiers, Is.EqualTo(
                MainWindowHotKeyConfig.ModifierControl |
                MainWindowHotKeyConfig.ModifierShift));
            Assert.That(virtualKey, Is.EqualTo((int)Keys.F9));
        }

        [TestCase(Keys.M)]
        [TestCase(Keys.Shift | Keys.Tab)]
        public void ShortcutCapture_RejectsMissingModifierOrFocusNavigation(Keys keyData)
        {
            int modifiers;
            int virtualKey;

            Assert.That(MainWindowHotKeyShortcut.TryCreate(keyData, false, out modifiers, out virtualKey), Is.False);
        }

        [Test]
        public void ShortcutCapture_AcceptsWindowsModifier()
        {
            int modifiers;
            int virtualKey;

            Assert.That(
                MainWindowHotKeyShortcut.TryCreate(Keys.F9, true, out modifiers, out virtualKey),
                Is.True);
            Assert.That(modifiers, Is.EqualTo(MainWindowHotKeyConfig.ModifierWindows));
            Assert.That(virtualKey, Is.EqualTo((int)Keys.F9));
            Assert.That(MainWindowHotKeyShortcut.Format(modifiers, virtualKey), Is.EqualTo("Win+F9"));
            Assert.That(
                MainWindowHotKeyShortcut.TryCreate(Keys.LWin, true, out modifiers, out virtualKey),
                Is.False);
        }

        [Test]
        public void ShortcutFormat_PreservesExistingDefault()
        {
            Assert.That(
                MainWindowHotKeyShortcut.Format(
                    MainWindowHotKeyConfig.DefaultModifiers,
                    MainWindowHotKeyConfig.DefaultVirtualKey),
                Is.EqualTo("Shift+Alt+M"));
        }

        [Test]
        public void ReplacementFailure_LeavesOriginalBindingAndDoesNotSaveRequestedKey()
        {
            var registered = new Dictionary<int, uint>();
            var binding = new MainWindowHotKeyRegistration((id, modifiers, key) =>
            {
                if (key == (uint)Keys.F9) return false;
                registered.Add(id, key);
                return true;
            }, id => registered.Remove(id));
            binding.TryUpdate(true, MainWindowHotKeyConfig.DefaultModifiers, (int)Keys.M, () => { });
            var saved = false;
            Assert.That(binding.TryUpdate(true, MainWindowHotKeyConfig.ModifierControl, (int)Keys.F9, () => saved = true), Is.False);
            Assert.That(saved, Is.False);
            Assert.That(binding.IsRegistered, Is.True);
            Assert.That(binding.VirtualKey, Is.EqualTo((int)Keys.M));
            Assert.That(registered.Values, Is.EqualTo(new[] { (uint)Keys.M }));
        }

        [Test]
        public void SaveFailure_ReleasesOnlyCandidateAndReplacementRetryCommitsBeforeOldKeyIsReleased()
        {
            var registered = new Dictionary<int, uint>();
            var binding = new MainWindowHotKeyRegistration((id, modifiers, key) =>
            { registered.Add(id, key); return true; }, id => registered.Remove(id));
            binding.TryUpdate(true, MainWindowHotKeyConfig.DefaultModifiers, (int)Keys.M, () => { });
            Assert.Throws<IOException>(() => binding.TryUpdate(true, MainWindowHotKeyConfig.ModifierControl,
                (int)Keys.F9, () => { throw new IOException("Save failed"); }));
            Assert.That(registered.Values, Is.EqualTo(new[] { (uint)Keys.M }));
            Assert.That(binding.VirtualKey, Is.EqualTo((int)Keys.M));
            Assert.That(binding.TryUpdate(true, MainWindowHotKeyConfig.ModifierControl, (int)Keys.F9,
                () => Assert.That(registered.Count, Is.EqualTo(2), "The working key remains registered until persistence succeeds.")), Is.True);
            Assert.That(registered.Values, Is.EqualTo(new[] { (uint)Keys.F9 }));
            Assert.That(binding.VirtualKey, Is.EqualTo((int)Keys.F9));
            Assert.That(binding.RegistrationId, Is.EqualTo(MainWindowHotKeyRegistration.SecondId));
        }

        [Test]
        public void DisablingAndDisposal_OnlyReleaseThisTargetsRegistration()
        {
            var firstKeys = new Dictionary<int, uint>();
            var secondKeys = new Dictionary<int, uint>();
            var first = new MainWindowHotKeyRegistration((id, modifiers, key) => { firstKeys.Add(id, key); return true; }, id => firstKeys.Remove(id));
            var second = new MainWindowHotKeyRegistration((id, modifiers, key) => { secondKeys.Add(id, key); return true; }, id => secondKeys.Remove(id));
            first.TryUpdate(true, MainWindowHotKeyConfig.DefaultModifiers, (int)Keys.M, () => { });
            second.TryUpdate(true, MainWindowHotKeyConfig.ModifierControl, (int)Keys.F9, () => { });
            var saved = false;
            first.TryUpdate(false, MainWindowHotKeyConfig.DefaultModifiers, (int)Keys.M, () => saved = true);
            first.Clear();
            Assert.That(saved, Is.True);
            Assert.That(first.IsRegistered, Is.False);
            Assert.That(firstKeys, Is.Empty);
            Assert.That(secondKeys.Values, Is.EqualTo(new[] { (uint)Keys.F9 }));
            Assert.That(second.IsRegistered, Is.True);
        }
    }
}
