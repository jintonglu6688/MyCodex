using System;

namespace MyCodex.Shared.Helpers
{
    public static class AssistantMessageUtility
    {
        public static bool HasDisplayableContent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return !IsPlaceholderOnly(text);
        }

        private static bool IsPlaceholderOnly(string text)
        {
            var trimmed = text.Trim();
            var placeholderCharacterCount = 0;
            var sawEllipsis = false;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var character = trimmed[i];
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                if (!IsPlaceholderCharacter(character))
                {
                    return false;
                }

                if (character == '…')
                {
                    sawEllipsis = true;
                }

                placeholderCharacterCount++;
            }

            return sawEllipsis || placeholderCharacterCount >= 3;
        }

        private static bool IsPlaceholderCharacter(char character)
        {
            return character == '.' ||
                   character == '…' ||
                   character == '．';
        }
    }
}
