using System;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal sealed class MainWindowHotKeyRegistration
    {
        internal const int FirstId = 0x4D43;
        internal const int SecondId = 0x4D44;
        internal const uint ModNoRepeat = 0x4000;
        private readonly Func<int, uint, uint, bool> _register;
        private readonly Action<int> _unregister;

        internal MainWindowHotKeyRegistration(Func<int, uint, uint, bool> register, Action<int> unregister)
        {
            _register = register ?? throw new ArgumentNullException(nameof(register));
            _unregister = unregister ?? throw new ArgumentNullException(nameof(unregister));
        }

        internal bool IsRegistered { get; private set; }
        internal int RegistrationId { get; private set; } = FirstId;
        internal int Modifiers { get; private set; }
        internal int VirtualKey { get; private set; }

        internal bool TryUpdate(bool enabled, int modifiers, int virtualKey, Action save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            modifiers = MainWindowHotKeyConfig.NormalizeModifiers(modifiers);
            virtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(virtualKey);
            if (!enabled || (IsRegistered && Modifiers == modifiers && VirtualKey == virtualKey))
            {
                save();
                if (!enabled) Clear();
                return true;
            }

            var nextId = IsRegistered && RegistrationId == FirstId ? SecondId : FirstId;
            if (!_register(nextId, (uint)modifiers | ModNoRepeat, (uint)virtualKey)) return false;
            try { save(); }
            catch
            {
                _unregister(nextId);
                throw;
            }
            if (IsRegistered) _unregister(RegistrationId);
            RegistrationId = nextId;
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            IsRegistered = true;
            return true;
        }

        internal void Clear()
        {
            if (!IsRegistered) return;
            _unregister(RegistrationId);
            IsRegistered = false;
        }
    }
}
