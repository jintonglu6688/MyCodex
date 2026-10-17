using System.IO;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;

namespace MyCodex.WinForms
{
    internal static class TargetMigrationStartup
    {
        public static AppConfigService Resolve(AppConfigService legacyPaths, string appDirectoryName)
        {
            if (!EnsureCatalog(legacyPaths)) return null;
            var catalog = new TargetCatalogStore(legacyPaths, appDirectoryName);
            var selection = TargetStartupPlanner.Resolve(catalog.Load(), null, null);
            return catalog.GetPaths(selection.Target.Id);
        }

        public static bool EnsureCatalog(AppConfigService legacyPaths)
        {
            // A completed migration must not inspect or repair every other target during startup.
            if (File.Exists(Path.Combine(legacyPaths.GetAppDataDirectory(), "targets", "catalog.json"))) return true;
            var migration = new TargetDataMigration(legacyPaths);
            var hasLegacyData = migration.HasLegacyData;
            if (!hasLegacyData)
            {
                try { migration.Run(); return true; }
                catch { /* Show a retryable failure without starting a default target. */ }
            }
            Program.InitializeUiPreferences(new JsonAppConfigStore(legacyPaths));
            using (var form = new TargetMigrationForm(migration))
            {
                return form.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
