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
        private const int MainFormPartialNonEmptyLinesCeiling = 22777;
        private const int MainFormCoreFileNonEmptyLinesCeiling = 2954;
        // One coordinator owns deferred browser setup; feature state stays outside MainForm.
        private const int MainFormFieldCeiling = 363;
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
            new DuplicateHelper("CreateButton", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?Button\s+CreateButton\s*\(", null, 27),
            new DuplicateHelper("CreateLabel", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?Label\s+CreateLabel\s*\(", null, 7),
            new DuplicateHelper("CreateTextBox", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?\w*TextBox\s+CreateTextBox\s*\(", null, 6),
            new DuplicateHelper("CreateComboBox", @"^\s+(?:private|internal|public|protected)\s+(?:static\s+)?ComboBox\s+CreateComboBox\s*\(", null, 2),
            new DuplicateHelper("Infrastructure JSON GetString", @"^\s+private\s+static\s+string\s+GetString\s*\(", "MyCodex.Infrastructure", 6),
            new DuplicateHelper("TrimToNull", @"^\s+(?:private|internal)\s+static\s+string\s+TrimToNull\s*\(", null, 0),
            new DuplicateHelper("FirstNonEmpty", @"^\s+(?:private|internal)\s+static\s+string\s+FirstNonEmpty\s*\(", null, 4),
            new DuplicateHelper("Localization alias L", @"^\s+(?:private|internal)\s+static\s+string\s+L\s*\(", null, 19)
        };

        // Every test reads the same measurement, so the source tree is scanned once per run.
        private static readonly Lazy<SourceMetrics> Measured = new Lazy<SourceMetrics>(SourceMetrics.Measure);

        [Test]
        public void MainForm_DoesNotGrow()
        {
            var metrics = Measured.Value;

            Assert.Multiple(() =>
            {
                Assert.That(metrics.MainFormPartialNonEmptyLines, Is.AtMost(MainFormPartialNonEmptyLinesCeiling), "MainForm partial non-empty lines");
                Assert.That(metrics.MainFormCoreFileNonEmptyLines, Is.AtMost(MainFormCoreFileNonEmptyLinesCeiling), "MainForm.cs non-empty lines");
                Assert.That(metrics.MainFormFields, Is.AtMost(MainFormFieldCeiling), "MainForm fields");
            });
        }

        [Test]
        public void LongMethodsAndLargeFiles_DoNotIncrease()
        {
            var metrics = Measured.Value;

            Assert.Multiple(() =>
            {
                Assert.That(metrics.MethodsOver150Lines, Is.AtMost(MethodsOver150LinesCeiling), "Methods over 150 lines");
                Assert.That(metrics.FilesOver1200Lines, Is.AtMost(FilesOver1200LinesCeiling), "Files over 1200 lines");
            });
        }

        [Test]
        public void DuplicateHelperCopies_DoNotIncrease()
        {
            var metrics = Measured.Value;

            Assert.Multiple(() =>
            {
                foreach (var helper in DuplicateHelpers)
                {
                    Assert.That(metrics.DuplicateCopies[helper.Name], Is.AtMost(helper.Ceiling), helper.Name + " copies");
                }
            });
        }

        [Test]
        public void Metrics_AreReported()
        {
            var metrics = Measured.Value;
            var lines = new List<string>
            {
                "MainForm partial non-empty lines: " + metrics.MainFormPartialNonEmptyLines,
                "MainForm.cs non-empty lines: " + metrics.MainFormCoreFileNonEmptyLines,
                "MainForm fields: " + metrics.MainFormFields,
                "Methods over 150 lines: " + metrics.MethodsOver150Lines,
                "Files over 1200 lines: " + metrics.FilesOver1200Lines
            };
            lines.AddRange(DuplicateHelpers.Select(helper => helper.Name + " copies: " + metrics.DuplicateCopies[helper.Name]));
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));

            Assert.That(metrics.MainFormPartialCount, Is.GreaterThan(0));
        }

        /// <summary>Counts non-empty lines other than using directives, so moving a type to another namespace is not growth.</summary>
        private static int CountNonEmptyLines(string[] lines)
        {
            return lines.Count(line => line.Trim().Length > 0 && !UsingDirective.IsMatch(line));
        }

        private static int CountFields(string[] lines)
        {
            return lines.Count(line => FieldDeclaration.IsMatch(line));
        }

        private static int CountLongMembers(string[] lines, int lineLimit)
        {
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

        private sealed class SourceMetrics
        {
            public int MainFormPartialCount { get; private set; }

            public int MainFormPartialNonEmptyLines { get; private set; }

            public int MainFormCoreFileNonEmptyLines { get; private set; }

            public int MainFormFields { get; private set; }

            public int MethodsOver150Lines { get; private set; }

            public int FilesOver1200Lines { get; private set; }

            public Dictionary<string, int> DuplicateCopies { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            public static SourceMetrics Measure()
            {
                var metrics = new SourceMetrics();
                foreach (var helper in DuplicateHelpers)
                {
                    metrics.DuplicateCopies.Add(helper.Name, 0);
                }

                var mainFormPartials = new HashSet<string>(
                    RepositorySource.FindSourceFiles("MyCodex.WinForms", "MainForm*.cs")
                        .Where(path => MainFormPartialFileName.IsMatch(Path.GetFileName(path))),
                    StringComparer.OrdinalIgnoreCase);
                var mainFormCore = RepositorySource.FindSourceFile("MainForm.cs");

                foreach (var path in RepositorySource.FindAllSourceFiles())
                {
                    var text = File.ReadAllText(path);
                    var lines = File.ReadAllLines(path);
                    metrics.MethodsOver150Lines += CountLongMembers(lines, LongMethodLineLimit);
                    if (lines.Length > LargeFileLineLimit)
                    {
                        metrics.FilesOver1200Lines++;
                    }

                    if (mainFormPartials.Contains(path))
                    {
                        metrics.MainFormPartialCount++;
                        metrics.MainFormPartialNonEmptyLines += CountNonEmptyLines(lines);
                        metrics.MainFormFields += CountFields(lines);
                    }

                    if (string.Equals(path, mainFormCore, StringComparison.OrdinalIgnoreCase))
                    {
                        metrics.MainFormCoreFileNonEmptyLines = CountNonEmptyLines(lines);
                    }

                    foreach (var helper in DuplicateHelpers)
                    {
                        if (helper.Covers(path) && helper.Declaration.IsMatch(text))
                        {
                            metrics.DuplicateCopies[helper.Name]++;
                        }
                    }
                }

                return metrics;
            }
        }

        private sealed class DuplicateHelper
        {
            public DuplicateHelper(string name, string declarationPattern, string projectName, int ceiling)
            {
                Name = name;
                // Compiled: each pattern scans every production file.
                Declaration = new Regex(declarationPattern, RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                ProjectName = projectName;
                Ceiling = ceiling;
            }

            public bool Covers(string path)
            {
                return ProjectName == null ||
                    path.StartsWith(RepositorySource.GetPath("src", ProjectName) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }

            public string Name { get; }

            public Regex Declaration { get; }

            public string ProjectName { get; }

            public int Ceiling { get; }
        }
    }
}
