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
            var service = RepositorySource.ReadSourceFile("UnixGitCloneService.cs");
            var workflow = RepositorySource.ReadSourceFile("UnixGitCloneWorkflow.cs");

            Assert.That(wsl, Does.Contain("new UnixGitCloneService("));
            Assert.That(remote, Does.Contain("new UnixGitCloneService("));
            Assert.That(service, Does.Contain("UnixGitCloneScript.CloneScript"));
            Assert.That(service, Does.Contain("UnixGitCloneWorkflow.ValidateRequest"));
            Assert.That(service, Does.Contain("UnixGitCloneWorkflow.BuildResult"));
            Assert.That(workflow, Does.Contain("UnixGitCloneScript.NormalizeRequired"));
            Assert.That(workflow, Does.Contain("UnixGitCloneScript.ExtractCloneTargetPath"));
            Assert.That(workflow, Does.Contain("GitOutputSanitizer.RedactSensitiveText"));
            Assert.That(wsl, Does.Not.Contain("CloneScript"));
            Assert.That(remote, Does.Not.Contain("CloneScript"));
        }

        [Test]
        public void SshKeyServices_UseSharedUnixSshKeyService()
        {
            var wsl = RepositorySource.ReadSourceFile("WslGitSshKeyService.cs");
            var remote = RepositorySource.ReadSourceFile("RemoteMacGitSshKeyService.cs");

            Assert.That(wsl, Does.Contain("new UnixGitSshKeyService("));
            Assert.That(remote, Does.Contain("new UnixGitSshKeyService("));
            Assert.That(wsl, Does.Not.Contain("ReadKeysScript"));
            Assert.That(remote, Does.Not.Contain("ReadKeysScript"));
            Assert.That(wsl, Does.Not.Contain("ParseKeys"));
            Assert.That(remote, Does.Not.Contain("ParseKeys"));
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
            var operations = RepositorySource.ReadSourceFile("UnixRuntimeFileSystemOperations.cs");

            Assert.That(wsl, Does.Contain("new UnixRuntimeFileSystemOperations("));
            Assert.That(remote, Does.Contain("new UnixRuntimeFileSystemOperations("));
            Assert.That(operations, Does.Contain("UnixRuntimeFileSystemScripts.EnsureDirectory(_remoteShellPrefix)"));
            Assert.That(wsl, Does.Not.Contain("UnixRuntimeFileSystemScripts"));
            Assert.That(remote, Does.Not.Contain("UnixRuntimeFileSystemScripts"));
        }

        [Test]
        public void FileSystemBridges_UseSharedUnixCore()
        {
            var wsl = RepositorySource.ReadSourceFile("WslFileSystemBridge.cs");
            var remote = RepositorySource.ReadSourceFile("RemoteMacFileSystemBridge.cs");
            var core = RepositorySource.ReadSourceFile("UnixFileSystemBridgeCore.cs");

            Assert.That(wsl, Does.Contain("new UnixFileSystemBridgeCore("));
            Assert.That(remote, Does.Contain("new UnixFileSystemBridgeCore("));
            Assert.That(core, Does.Contain("UnixFileSystemBridgeParsers.ParseDirectoryListing"));
            Assert.That(wsl, Does.Not.Contain("UnixFileSystemBridgeParsers"));
            Assert.That(remote, Does.Not.Contain("UnixFileSystemBridgeParsers"));
        }
    }
}
