using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedLocalizationResourceTests
    {
        // Matches local aliases such as `private static string L(string key, string fallback) { return SharedLocalization.Text(key, fallback); }`.
        private static readonly Regex LocalizationAliasPattern = new Regex(
            @"static\s+string\s+(?<alias>\w+)\(\s*string\s+key\s*,\s*string\s+fallback\s*\)\s*(?:\{\s*return|=>)\s*(?<localizer>SharedLocalization|UiLocalization)\.Text\(\s*key\s*,\s*fallback\s*\)",
            RegexOptions.Compiled);

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void SourceSharedLocalizationKeys_ArePresentInEnglishAndChineseResources()
        {
            AssertSourceKeysPresent(
                "SharedLocalization",
                RepositorySource.GetPath("src", "MyCodex.Shared", "Resources", "InternalStrings.resx"),
                RepositorySource.GetPath("src", "MyCodex.Shared", "Resources", "InternalStrings.zh-CN.resx"));
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void SourceWinFormsLocalizationKeys_ArePresentInEnglishAndChineseResources()
        {
            AssertSourceKeysPresent(
                "UiLocalization",
                RepositorySource.GetPath("src", "MyCodex.WinForms", "Resources", "Strings.resx"),
                RepositorySource.GetPath("src", "MyCodex.WinForms", "Resources", "Strings.zh-CN.resx"));
        }

        [Test]
        public void SourceLocalizationKeyScan_FindsAliasAndMultilineCalls()
        {
            const string source =
                "private static string L(string key, string fallback)\n{\n    return SharedLocalization.Text(key, fallback);\n}\n" +
                "var a = L(\"Alias.Key\", \"Alias\");\n" +
                "var b = SharedLocalization.Text(\n    \"Multiline.Key\",\n    \"Multiline\");\n" +
                "var c = UiLocalization.Text(\"Ui.Key\", \"Ui\");\n" +
                "var d = SharedLocalization.Text(\"Dynamic.\" + name, \"Dynamic\");\n";

            Assert.That(FindLocalizationKeys(source, "SharedLocalization"), Is.EquivalentTo(new[] { "Alias.Key", "Multiline.Key" }));
            Assert.That(FindLocalizationKeys(source, "UiLocalization"), Is.EquivalentTo(new[] { "Ui.Key" }));
        }

        [Test]
        public void CustomApprovalAutoApprovalSourceResources_ArePresentInEnglishAndChineseResources()
        {
            var requiredKeys = new[]
            {
                "AppServerClient.Approval.Source.FullAccessMode",
                "AppServerClient.Approval.Source.CustomPermission",
                "AppServerClient.Approval.Source.CustomCommandPrefix"
            };

            AssertRequiredKeysPresent("InternalStrings.resx", requiredKeys);
            AssertRequiredKeysPresent("InternalStrings.zh-CN.resx", requiredKeys);
        }

        private static void AssertRequiredKeysPresent(string fileName, string[] requiredKeys)
        {
            var resourceKeys = LoadResourceKeys(fileName);
            var missingKeys = requiredKeys
                .Where(key => !resourceKeys.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missingKeys, Is.Empty, fileName + " 缺少 Custom approval 共享资源键: " + string.Join(", ", missingKeys));
        }

        private static void AssertSourceKeysPresent(string localizer, string englishPath, string chinesePath)
        {
            var sourceRoot = RepositorySource.GetPath("src");
            var keys = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedPath(path))
                .SelectMany(path => FindLocalizationKeys(File.ReadAllText(path), localizer))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            var englishKeys = LoadResourceKeysFromPath(englishPath);
            var chineseKeys = LoadResourceKeysFromPath(chinesePath);
            var missingEnglish = keys.Where(key => !englishKeys.Contains(key)).ToArray();
            var missingChinese = keys.Where(key => !chineseKeys.Contains(key)).ToArray();

            Assert.That(missingEnglish, Is.Empty, Path.GetFileName(englishPath) + " 缺少资源键: " + string.Join(", ", missingEnglish));
            Assert.That(missingChinese, Is.Empty, Path.GetFileName(chinesePath) + " 缺少资源键: " + string.Join(", ", missingChinese));
        }

        private static IEnumerable<string> FindLocalizationKeys(string source, string localizer)
        {
            var callNames = new List<string> { Regex.Escape(localizer) + @"\.Text" };
            callNames.AddRange(LocalizationAliasPattern.Matches(source)
                .Cast<Match>()
                .Where(match => string.Equals(match.Groups["localizer"].Value, localizer, StringComparison.Ordinal))
                .Select(match => @"(?<![.\w])" + Regex.Escape(match.Groups["alias"].Value)));
            var callPattern = new Regex(@"(?:" + string.Join("|", callNames) + @")\(\s*""(?<key>[^""]+)""\s*[,)]");
            return callPattern.Matches(source)
                .Cast<Match>()
                .Select(match => match.Groups["key"].Value);
        }

        private static bool IsGeneratedPath(string path)
        {
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<string> LoadResourceKeys(string fileName)
        {
            return LoadResourceKeysFromPath(RepositorySource.GetPath("src", "MyCodex.Shared", "Resources", fileName));
        }

        private static HashSet<string> LoadResourceKeysFromPath(string path)
        {
            var document = XDocument.Load(path);
            return new HashSet<string>(
                document.Root
                    .Elements("data")
                    .Select(element => (string)element.Attribute("name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.Ordinal);
        }
    }
}
