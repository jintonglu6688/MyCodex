using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MyCodex.Application.Services
{
    // The choices offered for voice settings; stored values outside them fall back to the defaults.
    public static class VoiceChatPreferences
    {
        public const int DefaultTalkVirtualKey = 0x77;
        public const int DefaultOutputVolumePercent = 100;

        // F6, F7, F8, F9, F11, F12: F1 (help), F2/F3/F5 (used by MyCodex lists) and F4/F10 (system) are excluded.
        public static IReadOnlyList<int> TalkVirtualKeys { get; } = new[] { 0x75, 0x76, 0x77, 0x78, 0x7A, 0x7B };

        public static IReadOnlyList<int> OutputVolumePercents { get; } = new[] { 100, 150, 200 };

        public static int NormalizeTalkVirtualKey(int virtualKey)
        {
            return TalkVirtualKeys.Contains(virtualKey) ? virtualKey : DefaultTalkVirtualKey;
        }

        public static int NormalizeOutputVolumePercent(int percent)
        {
            return OutputVolumePercents.Contains(percent) ? percent : DefaultOutputVolumePercent;
        }

        public static string DescribeTalkKey(int virtualKey)
        {
            return "F" + (NormalizeTalkVirtualKey(virtualKey) - 0x6F).ToString(CultureInfo.InvariantCulture);
        }

        public static string NormalizeVoice(string voice, IEnumerable<string> available)
        {
            return !string.IsNullOrWhiteSpace(voice) && available != null && available.Contains(voice, StringComparer.Ordinal) ? voice : null;
        }
    }
}
