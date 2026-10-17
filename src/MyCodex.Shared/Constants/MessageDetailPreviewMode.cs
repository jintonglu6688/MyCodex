using System;

namespace MyCodex.Shared.Constants
{
    public static class MessageDetailPreviewMode
    {
        public const string Web = "web";
        public const string Text = "text";

        public static string Normalize(string mode)
        {
            var value = string.IsNullOrWhiteSpace(mode) ? Web : mode.Trim();
            if (string.Equals(value, Text, StringComparison.OrdinalIgnoreCase))
            {
                return Text;
            }

            return Web;
        }
    }
}
