using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace MyCodex.Application.Tests.SourceGuards
{
    /// <summary>
    /// Locates repository files for source guard tests. Production C# files are found by file name,
    /// so moving a file between folders inside src does not break the tests that read it.
    /// </summary>
    internal static class RepositorySource
    {
        private const string SolutionFileName = "MyCodex.sln";
        private static readonly Lazy<string> RootPath = new Lazy<string>(LocateRoot);
        private static readonly Lazy<SourceIndex> Index = new Lazy<SourceIndex>(() => new SourceIndex(Path.Combine(Root, "src")));

        public static string Root
        {
            get { return RootPath.Value; }
        }

        public static string GetPath(params string[] parts)
        {
            return Path.GetFullPath(Path.Combine(new[] { Root }.Concat(parts ?? new string[0]).ToArray()));
        }

        public static string ReadFile(params string[] parts)
        {
            return File.ReadAllText(GetPath(parts));
        }

        /// <summary>Returns the full path of the production C# file with this name, or null when it does not exist.</summary>
        public static string FindSourceFile(string fileName)
        {
            return Index.Value.Find(fileName);
        }

        public static string ReadSourceFile(string fileName)
        {
            var path = FindSourceFile(fileName);
            if (path == null)
            {
                throw new FileNotFoundException("Production source file was not found under src.", fileName);
            }

            return File.ReadAllText(path);
        }

        /// <summary>Returns production C# files in one project whose names match the pattern, ordered by file name.</summary>
        public static IReadOnlyList<string> FindSourceFiles(string projectName, string searchPattern)
        {
            var projectRoot = GetPath("src", projectName);
            return Directory.GetFiles(projectRoot, searchPattern, SearchOption.AllDirectories)
                .Where(path => !SourceIndex.IsBuildOutput(path))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>Returns every production C# file under src, ordered by path.</summary>
        public static IReadOnlyList<string> FindAllSourceFiles()
        {
            return Index.Value.AllPaths;
        }

        public static string ReadSourceFiles(string projectName, string searchPattern)
        {
            var builder = new StringBuilder();
            foreach (var path in FindSourceFiles(projectName, searchPattern))
            {
                builder.AppendLine(File.ReadAllText(path));
            }

            return builder.ToString();
        }

        /// <summary>Returns the text from the signature through the matching closing brace of its body.</summary>
        public static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Method not found: " + signature);
            var braceStart = source.IndexOf('{', start);
            Assert.That(braceStart, Is.GreaterThanOrEqualTo(0), "Method body not found: " + signature);
            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}' && --depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }

            Assert.Fail("Method body is incomplete: " + signature);
            return string.Empty;
        }

        /// <summary>Returns the text from the signature up to the next private member declaration.</summary>
        public static string ExtractUntilNextPrivateMember(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature + " should exist.");
            var nextMethod = source.IndexOf("\r\n        private ", start + signature.Length, StringComparison.Ordinal);
            if (nextMethod < 0)
            {
                nextMethod = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
            }

            Assert.That(nextMethod, Is.GreaterThan(start), signature + " should be followed by another method.");
            return source.Substring(start, nextMethod - start);
        }

        public static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string LocateRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository root containing " + SolutionFileName + ".");
            }

            return directory.FullName;
        }

        private sealed class SourceIndex
        {
            private readonly Dictionary<string, List<string>> _pathsByName =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public SourceIndex(string sourceRoot)
            {
                foreach (var path in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                {
                    if (IsBuildOutput(path))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(path);
                    List<string> paths;
                    if (!_pathsByName.TryGetValue(name, out paths))
                    {
                        paths = new List<string>();
                        _pathsByName.Add(name, paths);
                    }

                    paths.Add(path);
                }

                AllPaths = _pathsByName.Values
                    .SelectMany(paths => paths)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            public IReadOnlyList<string> AllPaths { get; }

            public string Find(string fileName)
            {
                List<string> paths;
                if (string.IsNullOrWhiteSpace(fileName) || !_pathsByName.TryGetValue(fileName, out paths))
                {
                    return null;
                }

                if (paths.Count > 1)
                {
                    throw new InvalidOperationException(
                        "Source file name is not unique under src: " + fileName + " (" + string.Join(", ", paths) + ")");
                }

                return paths[0];
            }

            public static bool IsBuildOutput(string path)
            {
                var separator = Path.DirectorySeparatorChar;
                return path.IndexOf(separator + "bin" + separator, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       path.IndexOf(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
