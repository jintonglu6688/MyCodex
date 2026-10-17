using System.Linq;
using System.Windows.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void SettingsMenu_PreservesTopLevelAndNestedOrder(bool withTargetContext)
        {
            using (var fixture = new TargetFormFixture(withTargetContext))
            {
                var form = fixture.Form;
                Assert.That(form.Menu.MenuItems.Cast<MenuItem>(), Is.EqualTo(new[]
                {
                    Get<MenuItem>(form, "_fileMenuItem"), Get<MenuItem>(form, "_settingsMenuItem"),
                    Get<MenuItem>(form, "_gitMenuItem"), Get<MenuItem>(form, "_accountMenuItem"),
                    Get<MenuItem>(form, "_helpMenuItem")
                }));
                Assert.That(Get<MenuItem>(form, "_settingsMenuItem").MenuItems.Cast<MenuItem>(), Is.EqualTo(new[]
                {
                    Get<MenuItem>(form, "_defaultModelMenuItem"), Get<MenuItem>(form, "_defaultPermissionMenuItem"),
                    Get<MenuItem>(form, "_optionsMenuItem"),
                    Get<MenuItem>(form, withTargetContext ? "_targetsMenuItem" : "_appServerRuntimeSettingsMenuItem"),
                    Get<MenuItem>(form, "_extensionsMenuItem"), Get<MenuItem>(form, "_remoteServiceMenuItem")
                }));
                Assert.That(Get<MenuItem>(form, "_extensionsMenuItem").MenuItems.Cast<MenuItem>(), Is.EqualTo(new[]
                {
                    Get<MenuItem>(form, "_pluginManagementMenuItem"), Get<MenuItem>(form, "_skillsManagementMenuItem"),
                    Get<MenuItem>(form, "_mcpManagementMenuItem")
                }));
                if (withTargetContext)
                {
                    var items = Get<MenuItem>(form, "_targetsMenuItem").MenuItems;
                    Assert.That(items.Count, Is.EqualTo(3));
                    Assert.That(items[1].Text, Is.EqualTo("-"));
                    Assert.That(items[2], Is.SameAs(Get<MenuItem>(form, "_appServerRuntimeSettingsMenuItem")));
                }
            }
        }
    }
}
