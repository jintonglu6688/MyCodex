using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ApplicationLayerBoundaryTests
    {
        private static readonly string[] ForbiddenTokens =
        {
            "using System.Diagnostics;",
            "ProcessStartInfo",
            "Process.Start(",
            "new Process(",
            "taskkill.exe",
            "ssh-keygen"
        };

        private static readonly string[] ForbiddenServiceFiles =
        {
            "ProcessExecutionRunner.cs",
            "ProcessTreeTerminator.cs",
            "GitCommandRunner.cs",
            "GitCloneService.cs",
            "GitEnvironmentService.cs",
            "GitSshKeyService.cs"
        };

        [Test]
        public void ApplicationLayer_DoesNotStartExternalProcesses()
        {
            var applicationRoot = FindApplicationRoot();
            var violations = new List<string>();
            foreach (var filePath in Directory.GetFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = GetRelativePath(applicationRoot, filePath);
                var text = File.ReadAllText(filePath);
                foreach (var token in ForbiddenTokens)
                {
                    if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
                    {
                        violations.Add(relativePath + " contains " + token);
                    }
                }
            }

            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void ApplicationLayer_DoesNotContainConcreteProcessServiceFiles()
        {
            var serviceRoot = Path.Combine(FindApplicationRoot(), "Services");
            var existingFiles = ForbiddenServiceFiles
                .Where(fileName => File.Exists(Path.Combine(serviceRoot, fileName)))
                .ToArray();

            Assert.That(existingFiles, Is.Empty);
        }

        private static string FindApplicationRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "src", "MyCodex.Application");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate src\\MyCodex.Application from the test directory.");
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(AppendDirectorySeparator(root));
            var pathUri = new Uri(path);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }
    }
}
