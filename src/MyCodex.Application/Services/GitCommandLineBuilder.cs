using System.Collections.Generic;
using System.Linq;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public static class GitCommandLineBuilder
    {
        public static string BuildArguments(IReadOnlyList<string> arguments)
        {
            return arguments == null
                ? string.Empty
                : string.Join(" ", arguments.Select(QuoteArgument).ToArray());
        }

        public static string QuoteArgument(string value)
        {
            return WindowsCommandLineArgumentEscaper.Quote(value);
        }
    }
}
