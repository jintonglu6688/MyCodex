using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace MyCodex.WinForms.Forms
{
    internal sealed class NotepadPlusPlusExecutableResolver
    {
        private readonly Func<string, bool> _fileExists;
        private readonly Func<Environment.SpecialFolder, string> _getFolderPath;
        private readonly Func<IEnumerable<string>> _registryCandidateProvider;

        public NotepadPlusPlusExecutableResolver()
            : this(File.Exists, Environment.GetFolderPath, ReadRegistryCandidates)
        {
        }

        internal NotepadPlusPlusExecutableResolver(
            Func<string, bool> fileExists,
            Func<Environment.SpecialFolder, string> getFolderPath,
            Func<IEnumerable<string>> registryCandidateProvider)
        {
            _fileExists = fileExists ?? File.Exists;
            _getFolderPath = getFolderPath ?? Environment.GetFolderPath;
            _registryCandidateProvider = registryCandidateProvider ?? ReadRegistryCandidates;
        }

        public string Resolve()
        {
            foreach (var candidate in EnumerateCandidates())
            {
                if (!string.IsNullOrWhiteSpace(candidate) && _fileExists(candidate))
                {
                    return candidate.Trim();
                }
            }

            return null;
        }

        private IEnumerable<string> EnumerateCandidates()
        {
            foreach (var candidate in _registryCandidateProvider())
            {
                yield return candidate;
            }

            foreach (var folder in new[]
            {
                _getFolderPath(Environment.SpecialFolder.ProgramFiles),
                _getFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                _getFolderPath(Environment.SpecialFolder.LocalApplicationData)
            })
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                yield return Path.Combine(folder, "Notepad++", "notepad++.exe");
                if (folder.EndsWith("Programs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return Path.Combine(folder, "Programs", "Notepad++", "notepad++.exe");
            }
        }

        private static IEnumerable<string> ReadRegistryCandidates()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var candidate in ReadRegistryCandidates(RegistryHive.CurrentUser, view))
                {
                    yield return candidate;
                }

                foreach (var candidate in ReadRegistryCandidates(RegistryHive.LocalMachine, view))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> ReadRegistryCandidates(RegistryHive hive, RegistryView view)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
            {
                foreach (var candidate in ReadAppPathsCandidates(baseKey))
                {
                    yield return candidate;
                }

                foreach (var candidate in ReadUninstallCandidates(baseKey))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<string> ReadAppPathsCandidates(RegistryKey baseKey)
        {
            using (var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe", false))
            {
                if (key == null)
                {
                    yield break;
                }

                var defaultValue = key.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    yield return defaultValue.Trim('"');
                }

                var pathValue = key.GetValue("Path") as string;
                if (!string.IsNullOrWhiteSpace(pathValue))
                {
                    yield return Path.Combine(pathValue.Trim('"'), "notepad++.exe");
                }
            }
        }

        private static IEnumerable<string> ReadUninstallCandidates(RegistryKey baseKey)
        {
            using (var key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Notepad++", false))
            {
                if (key == null)
                {
                    yield break;
                }

                var installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(installLocation))
                {
                    yield return Path.Combine(installLocation.Trim('"'), "notepad++.exe");
                }
            }
        }
    }
}
