using System;

namespace MyCodex.Shared.Constants
{
    public static class TextFileOpenMode
    {
        public const string Internal = "internal";
        public const string System = "system";
        public const string VsCode = "vscode";
        public const string NotepadPlusPlus = "notepadPlusPlus";
        public const string Custom = "custom";

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Internal;
            }

            var trimmed = value.Trim();
            if (string.Equals(trimmed, Internal, StringComparison.OrdinalIgnoreCase))
            {
                return Internal;
            }

            if (string.Equals(trimmed, System, StringComparison.OrdinalIgnoreCase))
            {
                return System;
            }

            if (string.Equals(trimmed, VsCode, StringComparison.OrdinalIgnoreCase))
            {
                return VsCode;
            }

            if (string.Equals(trimmed, NotepadPlusPlus, StringComparison.OrdinalIgnoreCase))
            {
                return NotepadPlusPlus;
            }

            if (string.Equals(trimmed, Custom, StringComparison.OrdinalIgnoreCase))
            {
                return Custom;
            }

            return Internal;
        }

        public static bool IsExternal(string value)
        {
            var mode = Normalize(value);
            return !string.Equals(mode, Internal, StringComparison.OrdinalIgnoreCase);
        }
    }
}
