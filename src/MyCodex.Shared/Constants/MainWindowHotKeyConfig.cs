namespace MyCodex.Shared.Constants
{
    public static class MainWindowHotKeyConfig
    {
        public const int ModifierAlt = 0x0001;
        public const int ModifierControl = 0x0002;
        public const int ModifierShift = 0x0004;
        public const int ModifierWindows = 0x0008;
        public const int AllowedModifiers = ModifierAlt | ModifierControl | ModifierShift | ModifierWindows;
        public const int DefaultModifiers = ModifierAlt | ModifierShift;
        public const int DefaultVirtualKey = 0x4D;

        public static int NormalizeModifiers(int value)
        {
            var normalized = value & AllowedModifiers;
            return normalized == 0 ? DefaultModifiers : normalized;
        }

        public static int NormalizeVirtualKey(int value)
        {
            return IsValidVirtualKey(value) ? value : DefaultVirtualKey;
        }

        public static bool IsValidVirtualKey(int value)
        {
            return value > 0 &&
                   value <= 0xFE &&
                   value != 0x10 &&
                   value != 0x11 &&
                   value != 0x12 &&
                   value != 0x5B &&
                   value != 0x5C &&
                   (value < 0xA0 || value > 0xA5);
        }
    }
}
