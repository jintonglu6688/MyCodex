using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
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
                    FormTestDriver.Field<MenuItem>(form, "_fileMenuItem"), FormTestDriver.Field<MenuItem>(form, "_settingsMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, "_gitMenuItem"), FormTestDriver.Field<MenuItem>(form, "_accountMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, "_helpMenuItem")
                }));
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_settingsMenuItem").MenuItems.Cast<MenuItem>(), Is.EqualTo(new[]
                {
                    FormTestDriver.Field<MenuItem>(form, "_defaultModelMenuItem"), FormTestDriver.Field<MenuItem>(form, "_defaultPermissionMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, "_optionsMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, withTargetContext ? "_targetsMenuItem" : "_appServerRuntimeSettingsMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, "_extensionsMenuItem"), FormTestDriver.Field<MenuItem>(form, "_remoteServiceMenuItem")
                }));
                Assert.That(FormTestDriver.Field<MenuItem>(form, "_extensionsMenuItem").MenuItems.Cast<MenuItem>(), Is.EqualTo(new[]
                {
                    FormTestDriver.Field<MenuItem>(form, "_pluginManagementMenuItem"), FormTestDriver.Field<MenuItem>(form, "_skillsManagementMenuItem"),
                    FormTestDriver.Field<MenuItem>(form, "_mcpManagementMenuItem")
                }));
                if (withTargetContext)
                {
                    var items = FormTestDriver.Field<MenuItem>(form, "_targetsMenuItem").MenuItems;
                    Assert.That(items.Count, Is.EqualTo(3));
                    Assert.That(items[1].Text, Is.EqualTo("-"));
                    Assert.That(items[2], Is.SameAs(FormTestDriver.Field<MenuItem>(form, "_appServerRuntimeSettingsMenuItem")));
                }
            }
        }
    }
}
