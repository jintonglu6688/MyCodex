using System.Text.RegularExpressions;

namespace MyCodex.Application.Services
{
    public static class GitOutputSanitizer
    {
        private static readonly Regex UrlCredentialRegex = new Regex(
            "(https?://)([^\\s/@:]+):([^\\s/@]+)@",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GitHubTokenRegex = new Regex(
            "(github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9_]+|glpat-[A-Za-z0-9_\\-]+)",
            RegexOptions.Compiled);

        private static readonly Regex NamedSecretRegex = new Regex(
            "\\b(access[_-]?token|token|api[_-]?key|password|passwd|secret)\\b(\\s*[:=]\\s*)([^\\s\"']+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string RedactSensitiveText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var value = UrlCredentialRegex.Replace(text, "$1***:***@");
            value = GitHubTokenRegex.Replace(value, "***");
            value = NamedSecretRegex.Replace(value, "$1$2***");
            return value;
        }
    }
}
