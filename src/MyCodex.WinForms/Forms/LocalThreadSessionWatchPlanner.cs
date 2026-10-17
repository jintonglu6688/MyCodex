using System;
using System.Collections.Generic;
using System.IO;

namespace MyCodex.WinForms.Forms
{
    internal static class LocalThreadSessionWatchPlanner
    {
        internal static IReadOnlyList<string> BuildWatchRoots(
            IEnumerable<string> codexHomePaths,
            Func<string, bool> directoryExists)
        {
            var roots = new List<string>();
            if (codexHomePaths == null)
            {
                return roots;
            }

            directoryExists = directoryExists ?? Directory.Exists;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var codexHomePath in codexHomePaths)
            {
                if (string.IsNullOrWhiteSpace(codexHomePath))
                {
                    continue;
                }

                string sessionsRoot;
                try
                {
                    sessionsRoot = Path.GetFullPath(Path.Combine(codexHomePath.Trim(), "sessions"))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    continue;
                }

                if (!seen.Add(sessionsRoot) || !directoryExists(sessionsRoot))
                {
                    continue;
                }

                roots.Add(sessionsRoot);
            }

            return roots;
        }
    }
}
