using System.Globalization;

namespace MyCodex.WinForms.Forms
{
    internal static class TokenCountFormatter
    {
        internal static string Format(long value)
        {
            if (value >= 100000000L)
            {
                return FormatScaled(value, 100000000M, "亿");
            }

            if (value >= 10000L)
            {
                return FormatScaled(value, 10000M, "万");
            }

            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatScaled(long value, decimal divisor, string unit)
        {
            var scaled = value / divisor;
            return scaled.ToString("0.##", CultureInfo.CurrentCulture) + unit;
        }
    }
}
