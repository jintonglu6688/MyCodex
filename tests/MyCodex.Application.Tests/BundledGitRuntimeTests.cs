using System;
using System.IO;
using MyCodex.Infrastructure.Git;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class BundledGitRuntimeTests
    {
        [Test]
        public void PrivateGit_IsPreferredAndOnlyChangesTheCurrentProcessPath()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "private git-" + Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(root, "runtime", "git", "cmd");
            var executable = Path.Combine(directory, "git.exe");
            var inherited = Environment.GetEnvironmentVariable("PATH");
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(executable, "fixture");
                Environment.SetEnvironmentVariable("PATH", @"C:\fixture\system", EnvironmentVariableTarget.Process);
                Assert.That(BundledGitRuntime.ResolveExecutable(root), Is.EqualTo(executable));
                BundledGitRuntime.ConfigureProcessEnvironment(root);
                Assert.That(Environment.GetEnvironmentVariable("PATH"), Is.EqualTo(directory + @";C:\fixture\system"));
                BundledGitRuntime.ConfigureProcessEnvironment(root);
                Assert.That(Environment.GetEnvironmentVariable("PATH"), Is.EqualTo(directory + @";C:\fixture\system"));
                Assert.That(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Is.EqualTo(userPath));
                Assert.That(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine), Is.EqualTo(machinePath));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", inherited, EnvironmentVariableTarget.Process);
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void UnpreparedCheckout_KeepsTheExistingGitLookupAndProcessPath()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing-git-" + Guid.NewGuid().ToString("N"));
            var inherited = Environment.GetEnvironmentVariable("PATH");
            Assert.That(BundledGitRuntime.ResolveExecutable(root), Is.EqualTo("git"));
            BundledGitRuntime.ConfigureProcessEnvironment(root);
            Assert.That(Environment.GetEnvironmentVariable("PATH"), Is.EqualTo(inherited));
        }
    }
}
