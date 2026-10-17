using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixRemoteOperationStructureTests
    {
        [Test]
        public void CloneServices_UseSharedUnixCloneScript()
        {
            var wsl = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "WslGitCloneService.cs");
            var remote = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "RemoteMacGitCloneService.cs");
            var workflow = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "UnixGitCloneWorkflow.cs");

            Assert.That(wsl, Does.Contain("UnixGitCloneScript.CloneScript"));
            Assert.That(remote, Does.Contain("UnixGitCloneScript.CloneScript"));
            Assert.That(wsl, Does.Not.Contain("private const string CloneScript"));
            Assert.That(remote, Does.Not.Contain("private const string CloneScript"));
            Assert.That(workflow, Does.Contain("UnixGitCloneScript.NormalizeRequired"));
            Assert.That(workflow, Does.Contain("UnixGitCloneScript.ExtractCloneTargetPath"));
            Assert.That(workflow, Does.Contain("GitOutputSanitizer.RedactSensitiveText"));
            Assert.That(wsl, Does.Contain("UnixGitCloneWorkflow"));
            Assert.That(remote, Does.Contain("UnixGitCloneWorkflow"));
            Assert.That(wsl, Does.Not.Contain("ExtractCloneTargetPath"));
            Assert.That(remote, Does.Not.Contain("ExtractCloneTargetPath"));
        }

        [Test]
        public void UnixGitRepositoryServices_UseFactoryInsteadOfForwardingWrappers()
        {
            var factory = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "UnixGitRepositoryServiceFactory.cs");
            var root = FindRepositoryRoot();

            Assert.That(File.Exists(Path.Combine(root, "src", "MyCodex.Infrastructure", "AppServer", "WslGitRepositoryService.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(root, "src", "MyCodex.Infrastructure", "AppServer", "RemoteMacGitRepositoryService.cs")), Is.False);
            Assert.That(factory, Does.Contain("new GitRepositoryService(runner, NormalizeRuntimeWorkspacePath, true, true, false)"));
            Assert.That(factory, Does.Contain("SharedLocalization.Text(\"GitRepositoryService.Error.WorkspacePathRequired\""));
            Assert.That(factory, Does.Contain("workspacePath.Trim().Replace('\\\\', '/')"));
        }

        [Test]
        public void RuntimeFileOperations_UseSharedUnixScriptBuilder()
        {
            var wsl = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "WslRuntimeFileSystemOperations.cs");
            var remote = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "RemoteMacRuntimeFileSystemOperations.cs");

            Assert.That(wsl, Does.Contain("UnixRuntimeFileSystemScripts.CreateDirectory(false)"));
            Assert.That(remote, Does.Contain("UnixRuntimeFileSystemScripts.CreateDirectory(true)"));
            Assert.That(wsl, Does.Not.Contain("private const string CreateDirectoryScript"));
            Assert.That(remote, Does.Not.Contain("private const string CreateDirectoryScript"));
        }

        [Test]
        public void FileSystemBridges_UseSharedParsers()
        {
            var wsl = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "WslFileSystemBridge.cs");
            var remote = ReadSource("src", "MyCodex.Infrastructure", "AppServer", "RemoteMacFileSystemBridge.cs");

            Assert.That(wsl, Does.Contain("UnixFileSystemBridgeParsers.ParseDirectoryListing"));
            Assert.That(remote, Does.Contain("UnixFileSystemBridgeParsers.ParseDirectoryListing"));
            Assert.That(wsl, Does.Not.Contain("private static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ParseDirectoryListing"));
            Assert.That(remote, Does.Not.Contain("private static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ParseDirectoryListing"));
        }

        private static string ReadSource(params string[] parts)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, Path.Combine(parts));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not locate source file.", Path.Combine(parts));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MyCodex.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
