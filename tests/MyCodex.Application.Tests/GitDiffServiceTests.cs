using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class GitDiffServiceTests
    {
        [Test]
        public void ReadUncommittedChanges_PreservesUtf8AndSpacesInFilePath()
        {
            var relativePath = Path.Combine("docs", "\u4E2D\u6587 \u7A7A\u683C.txt");
            var runner = new FakeGitDiffCommandRunner();
            runner.SetResult(
                "status --short -z --untracked-files=all --no-renames",
                "?? " + ToGitQuotedPath(relativePath.Replace('\\', '/')) + "\0");
            var service = new GitDiffService(runner);
            var workspaceRoot = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));

                var snapshot = service.ReadUncommittedChanges(workspaceRoot);
                var entry = snapshot.Files.Single(item =>
                    string.Equals(item.FilePath, relativePath, StringComparison.OrdinalIgnoreCase));

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(entry.ChangeKind, Is.EqualTo("untracked"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadUncommittedChanges_WithoutRunnerThrows()
        {
            var service = new GitDiffService();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));

                Assert.Throws<InvalidOperationException>(() => service.ReadUncommittedChanges(workspaceRoot));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void BuildSnapshotFromTurnDiff_ParsesQuotedDiffHeaderPath()
        {
            var service = new GitDiffService();
            var relativePath = "src/\u4E2D\u6587 \u7A7A\u683C.cs";
            var diffText = string.Join(
                Environment.NewLine,
                new[]
                {
                    "diff --git " + ToGitQuotedPath("a/" + relativePath) + " " + ToGitQuotedPath("b/" + relativePath),
                    "index 1111111..2222222 100644",
                    "--- " + ToGitQuotedPath("a/" + relativePath),
                    "+++ " + ToGitQuotedPath("b/" + relativePath),
                    "@@ -1 +1 @@",
                    "-before",
                    "+after"
                });

            var snapshot = service.BuildSnapshotFromTurnDiff(null, diffText, null);

            Assert.That(snapshot.Files, Has.Count.EqualTo(1));
            Assert.That(snapshot.Files[0].FilePath, Is.EqualTo(Path.Combine("src", "\u4E2D\u6587 \u7A7A\u683C.cs")));
            Assert.That(snapshot.Files[0].OriginalFilePath, Is.EqualTo(Path.Combine("src", "\u4E2D\u6587 \u7A7A\u683C.cs")));
            Assert.That(snapshot.Files[0].AddedLineCount, Is.EqualTo(1));
            Assert.That(snapshot.Files[0].DeletedLineCount, Is.EqualTo(1));
        }

        private static string ToGitQuotedPath(string path)
        {
            var bytes = Encoding.UTF8.GetBytes(path.Replace('\\', '/'));
            var builder = new StringBuilder();
            builder.Append('"');
            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                if (value == '"' || value == '\\')
                {
                    builder.Append('\\');
                    builder.Append((char)value);
                    continue;
                }

                if (value >= 0x20 && value <= 0x7e)
                {
                    builder.Append((char)value);
                    continue;
                }

                builder.Append('\\');
                builder.Append(Convert.ToString(value, 8).PadLeft(3, '0'));
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-GitDiffTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class FakeGitDiffCommandRunner : IGitDiffCommandRunner
        {
            private readonly Dictionary<string, GitCommandResult> _results =
                new Dictionary<string, GitCommandResult>(StringComparer.Ordinal);

            public void SetResult(string arguments, string standardOutput)
            {
                _results[arguments] = new GitCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = standardOutput,
                    StandardError = string.Empty
                };
            }

            public GitCommandResult Run(string workspacePath, string arguments, int timeoutMilliseconds)
            {
                GitCommandResult result;
                if (_results.TryGetValue(arguments, out result))
                {
                    return result;
                }

                return new GitCommandResult
                {
                    ExitCode = 0,
                    StandardOutput = string.Empty,
                    StandardError = string.Empty
                };
            }
        }
    }
}
