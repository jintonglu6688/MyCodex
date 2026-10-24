using System;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    // Window-local talk key: sees its down and up before the focused control, so it never types or moves focus.
    internal sealed class PushToTalkKeyFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private readonly Func<bool> _isOwnerActive;
        private readonly Func<Keys> _talkKey;
        private readonly Func<bool, bool> _handle;

        // handle receives true on key down (auto-repeats included) and false on key up; it returns whether the key was consumed.
        internal PushToTalkKeyFilter(Func<bool> isOwnerActive, Func<Keys> talkKey, Func<bool, bool> handle)
        {
            _isOwnerActive = isOwnerActive ?? throw new ArgumentNullException(nameof(isOwnerActive));
            _talkKey = talkKey ?? throw new ArgumentNullException(nameof(talkKey));
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public bool PreFilterMessage(ref Message m)
        {
            var down = m.Msg == WmKeyDown || m.Msg == WmSysKeyDown;
            var up = m.Msg == WmKeyUp || m.Msg == WmSysKeyUp;
            if ((!down && !up) || (Keys)((int)(long)m.WParam & 0xFFFF) != _talkKey() || !_isOwnerActive())
            {
                return false;
            }

            return _handle(down);
        }
    }
}
