using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class ThemeCoverageTests
    {
        private static readonly Regex FormClassPattern = new Regex(
            @"(?m)^\s*(?:public|internal|private)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(?<name>\w+)\s*:\s*Form\b",
            RegexOptions.CultureInvariant);

        [Test]
        public void EveryConcreteFormCallsTheThemeEntryPoint()
        {
            var formBodies = LoadFormClassBodies();
            var missing = typeof(MainForm).Assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(Form).IsAssignableFrom(type))
                .Where(type =>
                {
                    string body;
                    return !formBodies.TryGetValue(type.Name, out body) ||
                        body.IndexOf("UiTheme.ApplyForm(this)", StringComparison.Ordinal) < 0;
                })
                .Select(type => type.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missing, Is.Empty, "Every concrete form must enter the shared theme path.");
        }

        [Test]
        public void NativeWidgetsThatNeedCustomChromeUseThemeAwareTypes()
        {
            var source = string.Join(
                Environment.NewLine,
                RepositorySource.FindSourceFiles("MyCodex.WinForms", "*.cs").Select(File.ReadAllText));

            Assert.That(Regex.IsMatch(source, @"new\s+TabControl(?!\w)"), Is.False);
            Assert.That(Regex.IsMatch(source, @"new\s+ComboBox(?!\w)"), Is.False);
            Assert.That(Regex.IsMatch(source, @"new\s+ProgressBar(?!\w)"), Is.False);
            Assert.That(Regex.IsMatch(source, @"new\s+DateTimePicker(?!\w)"), Is.False);
            Assert.That(Regex.IsMatch(source, @"new\s+CheckBox(?!\w)"), Is.False);
            Assert.That(Regex.IsMatch(source, @"new\s+RadioButton(?!\w)"), Is.False);
        }

        private static Dictionary<string, string> LoadFormClassBodies()
        {
            var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in RepositorySource.FindSourceFiles("MyCodex.WinForms", "*.cs"))
            {
                var text = File.ReadAllText(path);
                foreach (Match match in FormClassPattern.Matches(text))
                {
                    var name = match.Groups["name"].Value;
                    var body = ExtractClassBody(text, match.Index);
                    string existing;
                    bodies[name] = bodies.TryGetValue(name, out existing) ? existing + Environment.NewLine + body : body;
                }
            }

            return bodies;
        }

        private static string ExtractClassBody(string source, int declarationIndex)
        {
            var bodyStart = source.IndexOf('{', declarationIndex);
            Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), "Class body was not found.");
            var depth = 0;
            for (var index = bodyStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    return source.Substring(bodyStart, index - bodyStart + 1);
                }
            }

            Assert.Fail("Class body was incomplete.");
            return string.Empty;
        }
    }
}
