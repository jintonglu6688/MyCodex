using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MyCodex.Application.Tests.SourceGuards
{
    /// <summary>
    /// Size and duplication ceilings for production source. A ceiling may only go down: when a change
    /// improves a metric, lower its ceiling to the new value so the gain cannot silently regress.
    /// Code may move between files and folders freely; only the totals are checked.
    /// </summary>
    [TestFixture]
    [Category(SourceGuardCategory.Placement)]
    public sealed class CodeHealthRatchetTests
    {
        private const int MainFormPartialNonEmptyLinesCeiling = 22761;
        private const int MainFormCoreFileNonEmptyLinesCeiling = 2960;
        private const int MainFormFieldCeiling = 364;
        private const int MethodsOver150LinesCeiling = 13;
        private const int FilesOver1200LinesCeiling = 23;

        private const int LongMethodLineLimit = 150;
        private const int LargeFileLineLimit = 1200;

        private static readonly Regex MainFormPartialFileName = new Regex(@"^MainForm(\..+)?\.cs$", RegexOptions.CultureInvariant);
        private static readonly Regex UsingDirective = new Regex(@"^using\s+[\w\.=\s]+;\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex FieldDeclaration = new Regex(
            @"^        (?:private|internal|protected)\s+(?:static\s+)?(?:readonly\s+)?[\w<>\[\],\.\? ]+?\s+_\w+\s*(?:=|;)",
            RegexOptions.CultureInvariant);
        private static readonly Regex MemberSignature = new Regex(
            @"^        (?:public|private|protected|internal)\b[^=;{}]*\)\s*(?:where\b.*)?$",
            RegexOptions.CultureInvariant);

        private static readonly DuplicateHelper[] DuplicateHelpers =
        {
            new DuplicateHelper("CreateButton", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?Button\s+CreateButton\s*\(", null, 55),
            new DuplicateHelper("CreateLabel", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?Label\s+CreateLabel\s*\(", null, 15),
            new DuplicateHelper("CreateTextBox", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?\w*TextBox\s+CreateTextBox\s*\(", null, 16),
            new DuplicateHelper("CreateComboBox", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?ComboBox\s+CreateComboBox\s*\(", null, 7),
            new DuplicateHelper("Infrastructure JSON GetString", @"^\s+private\s+static\s+string\s+GetString\s*\(", "MyCodex.Infrastructure", 7),
            new DuplicateHelper("TrimToNull", @"^\s+(?:private|internal)\s+static\s+string\s+TrimToNull\s*\(", null, 7),
            new DuplicateHelper("FirstNonEmpty", @"^\s+(?:private|internal)\s+static\s+string\s+FirstNonEmpty\s*\(", null, 11),
            new DuplicateHelper("Localization alias L", @"^\s+(?:private|internal)\s+static\s+string\s+L\s*\(", null, 19)
        };

        [Test]
        public void MainForm_DoesNotGrow()
        {
            var partials = RepositorySource.FindSourceFiles("MyCodex.WinForms", "MainForm*.cs")
                .Where(path => MainFormPartialFileName.IsMatch(Path.GetFileName(path)))
                .ToArray();
            var core = RepositorySource.FindSourceFile("MainForm.cs");

            Assert.Multiple(() =>
            {
                Assert.That(partials.Sum(CountNonEmptyLines), Is.AtMost(MainFormPartialNonEmptyLinesCeiling), "MainForm partial non-empty lines");
                Assert.That(CountNonEmptyLines(core), Is.AtMost(MainFormCoreFileNonEmptyLinesCeiling), "MainForm.cs non-empty lines");
                Assert.That(partials.Sum(CountFields), Is.AtMost(MainFormFieldCeiling), "MainForm fields");
            });
        }

        [Test]
        public void LongMethodsAndLargeFiles_DoNotIncrease()
        {
            var files = RepositorySource.FindAllSourceFiles();

            Assert.Multiple(() =>
            {
                Assert.That(files.Sum(path => CountLongMembers(path, LongMethodLineLimit)), Is.AtMost(MethodsOver150LinesCeiling), "Methods over 150 lines");
                Assert.That(files.Count(path => File.ReadAllLines(path).Length > LargeFileLineLimit), Is.AtMost(FilesOver1200LinesCeiling), "Files over 1200 lines");
            });
        }

        [Test]
        public void DuplicateHelperCopies_DoNotIncrease()
        {
            Assert.Multiple(() =>
            {
                foreach (var helper in DuplicateHelpers)
                {
                    Assert.That(CountDeclaringFiles(helper), Is.AtMost(helper.Ceiling), helper.Name + " copies");
                }
            });
        }

        [Test]
        public void Metrics_AreReported()
        {
            var partials = RepositorySource.FindSourceFiles("MyCodex.WinForms", "MainForm*.cs")
                .Where(path => MainFormPartialFileName.IsMatch(Path.GetFileName(path)))
                .ToArray();
            var files = RepositorySource.FindAllSourceFiles();
            var lines = new List<string>
            {
                "MainForm partial non-empty lines: " + partials.Sum(CountNonEmptyLines),
                "MainForm.cs non-empty lines: " + CountNonEmptyLines(RepositorySource.FindSourceFile("MainForm.cs")),
                "MainForm fields: " + partials.Sum(CountFields),
                "Methods over 150 lines: " + files.Sum(path => CountLongMembers(path, LongMethodLineLimit)),
                "Files over 1200 lines: " + files.Count(path => File.ReadAllLines(path).Length > LargeFileLineLimit)
            };
            lines.AddRange(DuplicateHelpers.Select(helper => helper.Name + " copies: " + CountDeclaringFiles(helper)));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));

            Assert.That(partials, Is.Not.Empty);
        }

        /// <summary>Counts non-empty lines other than using directives, so moving a type to another namespace is not growth.</summary>
        private static int CountNonEmptyLines(string path)
        {
            return File.ReadLines(path).Count(line => line.Trim().Length > 0 && !UsingDirective.IsMatch(line));
        }

        private static int CountFields(string path)
        {
            return File.ReadLines(path).Count(line => FieldDeclaration.IsMatch(line));
        }

        private static int CountLongMembers(string path, int lineLimit)
        {
            var lines = File.ReadAllLines(path);
            var count = 0;
            for (var i = 0; i + 1 < lines.Length; i++)
            {
                if (!MemberSignature.IsMatch(lines[i]) || lines[i + 1] != "        {")
                {
                    continue;
                }

                var end = i + 2;
                while (end < lines.Length && lines[end] != "        }")
                {
                    end++;
                }

                if (end - i + 1 > lineLimit)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDeclaringFiles(DuplicateHelper helper)
        {
            var files = helper.ProjectName == null
                ? RepositorySource.FindAllSourceFiles()
                : RepositorySource.FindSourceFiles(helper.ProjectName, "*.cs");
            return files.Count(path => helper.Declaration.IsMatch(File.ReadAllText(path)));
        }

        private sealed class DuplicateHelper
        {
            public DuplicateHelper(string name, string declarationPattern, string projectName, int ceiling)
            {
                Name = name;
                Declaration = new Regex(declarationPattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);
                ProjectName = projectName;
                Ceiling = ceiling;
            }

            public string Name { get; }

            public Regex Declaration { get; }

            public string ProjectName { get; }

            public int Ceiling { get; }
        }
    }
}
