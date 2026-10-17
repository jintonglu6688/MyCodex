using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ExternalAgentConfigImportSourceTests
    {
        [Test]
        public void ActiveImportPath_UsesOfficialClientAndFiltersUnsafeItems()
        {
            var formSource = File.ReadAllText(GetSourcePath("MyCodex.WinForms", "Forms", "ExternalAgentConfigImportForm.cs"));
            var mapperSource = File.ReadAllText(GetSourcePath("MyCodex.Infrastructure", "AppServer", "AppServerExternalAgentConfigMapper.cs"));
            var settingsSource = File.ReadAllText(GetSourcePath("MyCodex.WinForms", "Forms", "MainForm.SessionSettings.cs"));

            Assert.That(formSource, Does.Contain("ExternalAgentConfigImportPolicy.CanImport"));
            Assert.That(formSource, Does.Contain("item.ItemType != ExternalAgentConfigMigrationItemType.Config"));
            Assert.That(ExtractConstructor(formSource), Does.Not.Contain("LoadMigrationItemsAsync()"));
            Assert.That(ExtractConstructor(formSource), Does.Not.Contain("ImportSelectedItemsAsync()"));
            Assert.That(mapperSource, Does.Contain("ExternalAgentConfigImportPolicy.FilterImportable(items)"));
            Assert.That(mapperSource, Does.Contain("HasMatchingRawItemType(item)"));
            Assert.That(mapperSource, Does.Not.Contain("File."));
            Assert.That(mapperSource, Does.Not.Contain("Directory."));
            Assert.That(settingsSource, Does.Contain("new ExternalAgentConfigImportForm(_appServerClient, GetImportSyncWorkspacePath(sender))"));
            Assert.That(settingsSource, Does.Contain("AppServerProtocolFeature.ExternalAgentConfigImport"));
        }

        private static string GetSourcePath(params string[] path)
        {
            var parts = new string[]
            {
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src"
            };
            return Path.GetFullPath(Path.Combine(parts.Concat(path).ToArray()));
        }

        private static string ExtractConstructor(string source)
        {
            var start = source.IndexOf("public ExternalAgentConfigImportForm(", StringComparison.Ordinal);
            var end = source.IndexOf("internal int MigrationItemCountForTests", start, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            return source.Substring(start, end - start);
        }
    }
}
