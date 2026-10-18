using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class ExternalAgentConfigImportSourceTests
    {
        [Test]
        public void ActiveImportPath_UsesOfficialClientAndFiltersUnsafeItems()
        {
            var formSource = RepositorySource.ReadSourceFile("ExternalAgentConfigImportForm.cs");
            var mapperSource = RepositorySource.ReadSourceFile("AppServerExternalAgentConfigMapper.cs");
            var settingsSource = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

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
