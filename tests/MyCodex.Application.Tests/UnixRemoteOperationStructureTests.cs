using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.Placement)]
    [TestFixture]
    public sealed class UnixRemoteOperationStructureTests
    {
        [Test]
        public void CloneServices_UseSharedUnixCloneScript()
        {
            var wsl = RepositorySource.ReadSourceFile("WslGitCloneService.cs");
            var remote = RepositorySource.ReadSourceFile("RemoteMacGitCloneService.cs");
            var workflow = RepositorySource.ReadSourceFile("UnixGitCloneWorkflow.cs");

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
            var factory = RepositorySource.ReadSourceFile("UnixGitRepositoryServiceFactory.cs");

            Assert.That(factory, Does.Contain("new GitRepositoryService(runner, NormalizeRuntimeWorkspacePath, true, true, false)"));
            Assert.That(factory, Does.Contain("SharedLocalization.Text(\"GitRepositoryService.Error.WorkspacePathRequired\""));
            Assert.That(factory, Does.Contain("workspacePath.Trim().Replace('\\\\', '/')"));
        }

        [Test]
        public void RuntimeFileOperations_UseSharedUnixScriptBuilder()
        {
            var wsl = RepositorySource.ReadSourceFile("WslRuntimeFileSystemOperations.cs");
            var remote = RepositorySource.ReadSourceFile("RemoteMacRuntimeFileSystemOperations.cs");

            Assert.That(wsl, Does.Contain("UnixRuntimeFileSystemScripts.CreateDirectory(false)"));
            Assert.That(remote, Does.Contain("UnixRuntimeFileSystemScripts.CreateDirectory(true)"));
            Assert.That(wsl, Does.Not.Contain("private const string CreateDirectoryScript"));
            Assert.That(remote, Does.Not.Contain("private const string CreateDirectoryScript"));
        }

        [Test]
        public void FileSystemBridges_UseSharedParsers()
        {
            var wsl = RepositorySource.ReadSourceFile("WslFileSystemBridge.cs");
            var remote = RepositorySource.ReadSourceFile("RemoteMacFileSystemBridge.cs");

            Assert.That(wsl, Does.Contain("UnixFileSystemBridgeParsers.ParseDirectoryListing"));
            Assert.That(remote, Does.Contain("UnixFileSystemBridgeParsers.ParseDirectoryListing"));
            Assert.That(wsl, Does.Not.Contain("private static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ParseDirectoryListing"));
            Assert.That(remote, Does.Not.Contain("private static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ParseDirectoryListing"));
        }
    }
}
