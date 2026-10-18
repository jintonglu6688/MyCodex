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
        private static readonly Regex SharedLocalizationKeyPattern = new Regex(
            @"SharedLocalization\.Text\(""(?<key>[^""]+)""",
            RegexOptions.Compiled);

        [Test]
        public void SourceSharedLocalizationKeys_ArePresentInEnglishAndChineseResources()
        {
            var sourceRoot = RepositorySource.GetPath("src");
            var keys = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedPath(path))
                .SelectMany(path => SharedLocalizationKeyPattern
                    .Matches(File.ReadAllText(path))
                    .Cast<Match>())
                .Select(match => match.Groups["key"].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            var englishKeys = LoadResourceKeys("InternalStrings.resx");
            var chineseKeys = LoadResourceKeys("InternalStrings.zh-CN.resx");
            var missingEnglish = keys.Where(key => !englishKeys.Contains(key)).ToArray();
            var missingChinese = keys.Where(key => !chineseKeys.Contains(key)).ToArray();

            Assert.That(missingEnglish, Is.Empty, "英文共享资源键缺失: " + string.Join(", ", missingEnglish));
            Assert.That(missingChinese, Is.Empty, "中文共享资源键缺失: " + string.Join(", ", missingChinese));
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

        private static bool IsGeneratedPath(string path)
        {
            var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HashSet<string> LoadResourceKeys(string fileName)
        {
            var path = RepositorySource.GetPath("src", "MyCodex.Shared", "Resources", fileName);
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
