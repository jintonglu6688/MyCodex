using System;
using System.IO;

namespace MyCodex.Application.Services
{
    public static class GitClonePathPlanner
    {
        public static string InferTargetFolderName(string repositoryUrl)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return string.Empty;
            }

            var value = repositoryUrl.Trim().TrimEnd('/', '\\');
            var slashIndex = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            var colonIndex = value.LastIndexOf(':');
            var separatorIndex = Math.Max(slashIndex, colonIndex);
            if (separatorIndex >= 0 && separatorIndex < value.Length - 1)
            {
                value = value.Substring(separatorIndex + 1);
            }

            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '-');
            }

            return value.Trim();
        }
    }
}
