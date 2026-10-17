using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Git;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class GitCloneServiceTests
    {
        [Test]
        public void InferTargetFolderName_HandlesCommonUrls()
        {
            Assert.That(GitClonePathPlanner.InferTargetFolderName("https://github.com/example/MyRepo.git"), Is.EqualTo("MyRepo"));
            Assert.That(GitClonePathPlanner.InferTargetFolderName("git@github.com:example/MyRepo.git"), Is.EqualTo("MyRepo"));
            Assert.That(GitClonePathPlanner.InferTargetFolderName("https://gitcode.com/example/My Repo.git"), Is.EqualTo("My Repo"));
        }

        [Test]
        public void RedactSensitiveText_RemovesUrlCredentialsAndGitHubTokens()
        {
            var redacted = GitOutputSanitizer.RedactSensitiveText(
                "https://user:secret@example.com/repo.git github_pat_ABC123 ghp_ABC123 access_token=secret123 token: secret456 glpat-ABC123");

            Assert.That(redacted, Is.EqualTo("https://***:***@example.com/repo.git *** *** access_token=*** token: *** ***"));
        }

        [Test]
        public void CloneAsync_ClonesLocalRepository()
        {
            var sourceRoot = CreateTempDirectory();
            var targetParent = CreateTempDirectory();
            try
            {
                RunGit(sourceRoot, "init");
                RunGit(sourceRoot, "config user.name \"MyCodex Tests\"");
                RunGit(sourceRoot, "config user.email tests@example.com");
                File.WriteAllText(Path.Combine(sourceRoot, "readme.txt"), "hello" + Environment.NewLine);
                RunGit(sourceRoot, "add readme.txt");
                RunGit(sourceRoot, "commit -m initial");

                var progressLines = new List<string>();
                var service = new GitCloneService();
                var result = service.CloneAsync(
                    new GitCloneRequest
                    {
                        RepositoryUrl = sourceRoot,
                        TargetParentDirectory = targetParent,
                        TargetFolderName = "copy"
                    },
                    new Progress<GitCloneProgress>(item =>
                    {
                        if (item != null && item.Text != null)
                        {
                            progressLines.Add(item.Text);
                        }
                    }),
                    CancellationToken.None).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(targetParent, "copy", "readme.txt")), Is.True);
                Assert.That(progressLines.Count, Is.GreaterThan(0));
            }
            finally
            {
                DeleteDirectory(sourceRoot);
                DeleteDirectory(targetParent);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "mycodex-git-clone-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void RunGit(string workingDirectory, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(stdout + stderr);
                }
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            Directory.Delete(path, true);
        }
    }
}
