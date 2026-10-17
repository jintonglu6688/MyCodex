using System;
using System.Collections.Generic;
using System.IO;

namespace MyCodex.Application.Services
{
    public static class OfficeCliIntegrationPlanner
    {
        public const string DirectoryName = "OfficeCLI";
        public const string ExecutableFileName = "officecli.exe";
        public const string SkillName = "officecli";

        public static OfficeCliIntegrationPaths BuildPaths(
            string localApplicationDataPath,
            string userProfilePath,
            string codexHomePath)
        {
            var localAppData = NormalizeRequiredPath(localApplicationDataPath, nameof(localApplicationDataPath));
            var userProfile = NormalizeRequiredPath(userProfilePath, nameof(userProfilePath));
            var installDirectory = Path.Combine(localAppData, DirectoryName);

            return new OfficeCliIntegrationPaths
            {
                InstallDirectory = installDirectory,
                ExecutablePath = Path.Combine(installDirectory, ExecutableFileName),
                SkillDirectories = BuildSkillDirectories(userProfile, codexHomePath)
            };
        }

        public static string PrependDirectoryToPath(string currentPath, string directory)
        {
            var normalizedDirectory = NormalizeRequiredPath(directory, nameof(directory));
            var values = new List<string> { normalizedDirectory };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeForComparison(normalizedDirectory)
            };

            var entries = (currentPath ?? string.Empty).Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = (entries[i] ?? string.Empty).Trim();
                if (entry.Length == 0)
                {
                    continue;
                }

                var comparison = NormalizeForComparison(entry);
                if (comparison.Length == 0 || !seen.Add(comparison))
                {
                    continue;
                }

                values.Add(entry);
            }

            return string.Join(Path.PathSeparator.ToString(), values.ToArray());
        }

        public static bool TryApplyRuntimeEnvironment(
            IDictionary<string, string> environmentVariables,
            string officeCliExecutablePath,
            Func<string, bool> fileExists)
        {
            if (environmentVariables == null ||
                string.IsNullOrWhiteSpace(officeCliExecutablePath) ||
                fileExists == null ||
                !fileExists(officeCliExecutablePath))
            {
                return false;
            }

            var executablePath = Path.GetFullPath(officeCliExecutablePath.Trim());
            var directory = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string currentPath;
            if (!environmentVariables.TryGetValue("PATH", out currentPath))
            {
                currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            }

            environmentVariables["PATH"] = PrependDirectoryToPath(currentPath, directory);
            environmentVariables["MYCODEX_OFFICECLI_PATH"] = executablePath;
            return true;
        }

        public static OfficeCliIntegrationStatus EvaluateStatus(
            OfficeCliIntegrationPaths paths,
            string userPathValue,
            Func<string, bool> fileExists)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (fileExists == null)
            {
                throw new ArgumentNullException(nameof(fileExists));
            }

            var missingSkillDirectories = new List<string>();
            var skillDirectories = paths.SkillDirectories ?? new string[0];
            for (var i = 0; i < skillDirectories.Count; i++)
            {
                var directory = skillDirectories[i];
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var skillFilePath = Path.Combine(directory, "SKILL.md");
                if (!fileExists(skillFilePath))
                {
                    missingSkillDirectories.Add(directory);
                }
            }

            return new OfficeCliIntegrationStatus
            {
                InstallDirectory = paths.InstallDirectory,
                ExecutablePath = paths.ExecutablePath,
                IsExecutableInstalled = !string.IsNullOrWhiteSpace(paths.ExecutablePath) && fileExists(paths.ExecutablePath),
                IsUserPathConfigured = ContainsPathDirectory(userPathValue, paths.InstallDirectory),
                SkillDirectories = new List<string>(skillDirectories),
                MissingSkillDirectories = missingSkillDirectories
            };
        }

        private static IReadOnlyList<string> BuildSkillDirectories(string userProfilePath, string codexHomePath)
        {
            var directories = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSkillDirectory(directories, seen, codexHomePath);
            AddSkillDirectory(directories, seen, Path.Combine(userProfilePath, ".codex"));
            AddSkillDirectory(directories, seen, Path.Combine(userProfilePath, ".agents"));
            return directories;
        }

        private static bool ContainsPathDirectory(string pathValue, string directory)
        {
            if (string.IsNullOrWhiteSpace(pathValue) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var expected = NormalizeForComparison(directory);
            var entries = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < entries.Length; i++)
            {
                if (string.Equals(NormalizeForComparison(entries[i]), expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddSkillDirectory(ICollection<string> directories, ISet<string> seen, string homePath)
        {
            if (string.IsNullOrWhiteSpace(homePath))
            {
                return;
            }

            var directory = Path.Combine(Path.GetFullPath(homePath.Trim()), "skills", SkillName);
            if (seen.Add(NormalizeForComparison(directory)))
            {
                directories.Add(directory);
            }
        }

        private static string NormalizeRequiredPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", parameterName);
            }

            return Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string NormalizeForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}
