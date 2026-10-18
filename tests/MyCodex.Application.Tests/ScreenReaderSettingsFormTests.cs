using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ScreenReaderSettingsFormTests
    {
        [Test]
        public void ByCtrl_AllowsIndependentChannelName()
        {
            using (var form = new ScreenReaderSettingsForm(
                ScreenReaderProviderKind.ByCtrl,
                false,
                "  My Channel  "))
            {
                var channelNameTextBox = FormTestDriver.Field<TextBox>(form, "_independentChannelNameTextBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedProvider, Is.EqualTo(ScreenReaderProviderKind.ByCtrl));
                Assert.That(form.ScreenReaderIndependentChannelName, Is.EqualTo("My Channel"));
            }
        }

        [Test]
        public void ProviderList_DoesNotOfferLegacyProvider()
        {
            using (var form = new ScreenReaderSettingsForm(
                ScreenReaderProviderKind.ByCtrl,
                false,
                null))
            {
                var providerComboBox = FormTestDriver.Field<ComboBox>(form, "_providerComboBox");

                foreach (var item in providerComboBox.Items)
                {
                    Assert.That(item.ToString(), Is.Not.EqualTo("Boy" + "Ctrl"));
                    Assert.That(GetProviderValue(item), Is.Not.EqualTo(1));
                }
            }
        }

        [Test]
        public void ProviderList_OffersBaoYiWebApi()
        {
            using (var form = new ScreenReaderSettingsForm(
                ScreenReaderProviderKind.ByCtrl,
                false,
                null))
            {
                var providerComboBox = FormTestDriver.Field<ComboBox>(form, "_providerComboBox");

                var found = false;
                foreach (var item in providerComboBox.Items)
                {
                    if (GetProviderValue(item) == (int)ScreenReaderProviderKind.BaoYiWebApi)
                    {
                        found = true;
                        Assert.That(item.ToString(), Is.EqualTo("保益悦听新接口"));
                    }
                }

                Assert.That(found, Is.True);
            }
        }

        [Test]
        public void BaoYiWebApi_AllowsIndependentChannelAndManualBrowseMainChannel()
        {
            using (var form = new ScreenReaderSettingsForm(
                ScreenReaderProviderKind.BaoYiWebApi,
                true,
                "  Web Channel  "))
            {
                var channelNameTextBox = FormTestDriver.Field<TextBox>(form, "_independentChannelNameTextBox");
                var manualBrowseCheckBox = FormTestDriver.Field<CheckBox>(form, "_manualBrowseMainChannelCheckBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);
                Assert.That(manualBrowseCheckBox.Enabled, Is.True);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedProvider, Is.EqualTo(ScreenReaderProviderKind.BaoYiWebApi));
                Assert.That(form.ManualBrowseUseReaderMainChannel, Is.True);
                Assert.That(form.ScreenReaderIndependentChannelName, Is.EqualTo("Web Channel"));
            }
        }

        [Test]
        public void Zdsr_AllowsIndependentChannelAndManualBrowseMainChannel()
        {
            using (var form = new ScreenReaderSettingsForm(
                ScreenReaderProviderKind.Zdsr,
                true,
                "  Zdsr Channel  "))
            {
                var channelNameTextBox = FormTestDriver.Field<TextBox>(form, "_independentChannelNameTextBox");
                var manualBrowseCheckBox = FormTestDriver.Field<CheckBox>(form, "_manualBrowseMainChannelCheckBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);
                Assert.That(manualBrowseCheckBox.Enabled, Is.True);

                FormTestDriver.ClickOk(form);

                Assert.That(form.SelectedProvider, Is.EqualTo(ScreenReaderProviderKind.Zdsr));
                Assert.That(form.ManualBrowseUseReaderMainChannel, Is.True);
                Assert.That(form.ScreenReaderIndependentChannelName, Is.EqualTo("Zdsr Channel"));
            }
        }

        private static int GetProviderValue(object providerItem)
        {
            var property = providerItem.GetType().GetProperty(
                "Provider",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            return (int)property.GetValue(providerItem, null);
        }
    }
}
