using System.Text;

namespace MyCodex.Shared.Helpers
{
    public static class WindowsCommandLineArgumentEscaper
    {
        public static string Quote(string argument)
        {
            return Quote(argument, false);
        }

        public static string Quote(string argument, bool alwaysQuote)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            if (argument.Length == 0)
            {
                return "\"\"";
            }

            var requiresQuotes = alwaysQuote;
            for (var i = 0; i < argument.Length; i++)
            {
                var value = argument[i];
                if (char.IsWhiteSpace(value) || value == '"')
                {
                    requiresQuotes = true;
                    break;
                }
            }

            if (!requiresQuotes)
            {
                return argument;
            }

            var builder = new StringBuilder();
            builder.Append('"');

            var backslashCount = 0;
            for (var i = 0; i < argument.Length; i++)
            {
                var value = argument[i];
                if (value == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (value == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                    backslashCount = 0;
                    continue;
                }

                builder.Append('\\', backslashCount);
                backslashCount = 0;
                builder.Append(value);
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
