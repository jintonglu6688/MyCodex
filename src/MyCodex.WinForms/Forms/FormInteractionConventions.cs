using System;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class FormInteractionConventions
    {
        internal static void ConfigureRefreshButton(Button button, bool keepTabStop = false)
        {
            if (button == null)
            {
                return;
            }

            button.TabStop = keepTabStop;
        }

        internal static void SpeakRefresh(IScreenReaderService screenReaderService)
        {
            if (screenReaderService == null)
            {
                return;
            }

            screenReaderService.Speak(
                UiLocalization.Text("Common.Action.Refresh", "Refresh"),
                true,
                false,
                true);
        }

        internal static string ApplyMnemonic(string text, char mnemonic)
        {
            var normalizedText = text ?? string.Empty;
            var normalizedMnemonic = char.ToUpperInvariant(mnemonic);
            var marker = "(&" + normalizedMnemonic + ")";

            var existingMarkerIndex = normalizedText.LastIndexOf("(&", StringComparison.Ordinal);
            if (existingMarkerIndex >= 0)
            {
                var suffix = normalizedText.Substring(existingMarkerIndex);
                if (string.Equals(suffix, marker + ")", StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedText;
                }

                var closingIndex = normalizedText.IndexOf(')', existingMarkerIndex);
                if (closingIndex > existingMarkerIndex)
                {
                    normalizedText = normalizedText.Substring(0, existingMarkerIndex);
                }
            }

            return normalizedText.TrimEnd() + marker;
        }
    }
}
