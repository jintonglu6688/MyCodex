using System;
using System.IO;

namespace MyCodex.Application.Services
{
    public sealed class UnifiedDiffLineCounts
    {
        internal UnifiedDiffLineCounts(int additions, int deletions)
        {
            Additions = additions;
            Deletions = deletions;
        }

        public int Additions { get; }

        public int Deletions { get; }
    }

    public static class UnifiedDiffLineCounter
    {
        public static UnifiedDiffLineCounts Count(string diffText)
        {
            var additions = 0;
            var deletions = 0;

            using (var reader = new StringReader(diffText ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("+++", StringComparison.Ordinal) ||
                        line.StartsWith("---", StringComparison.Ordinal) ||
                        line.StartsWith("@@", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.StartsWith("+", StringComparison.Ordinal))
                    {
                        additions++;
                    }
                    else if (line.StartsWith("-", StringComparison.Ordinal))
                    {
                        deletions++;
                    }
                }
            }

            return new UnifiedDiffLineCounts(additions, deletions);
        }
    }
}
