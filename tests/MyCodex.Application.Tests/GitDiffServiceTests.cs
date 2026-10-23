using System;
using System.IO;
using System.Text;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class GitDiffServiceTests
    {
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
    }
}
