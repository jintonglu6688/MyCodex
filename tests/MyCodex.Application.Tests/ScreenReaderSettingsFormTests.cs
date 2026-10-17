using System.Reflection;
using System.Threading;
using System.Windows.Forms;
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
                var channelNameTextBox = GetField<TextBox>(form, "_independentChannelNameTextBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);

                InvokeOk(form);

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
                var providerComboBox = GetField<ComboBox>(form, "_providerComboBox");

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
                var providerComboBox = GetField<ComboBox>(form, "_providerComboBox");

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
                var channelNameTextBox = GetField<TextBox>(form, "_independentChannelNameTextBox");
                var manualBrowseCheckBox = GetField<CheckBox>(form, "_manualBrowseMainChannelCheckBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);
                Assert.That(manualBrowseCheckBox.Enabled, Is.True);

                InvokeOk(form);

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
                var channelNameTextBox = GetField<TextBox>(form, "_independentChannelNameTextBox");
                var manualBrowseCheckBox = GetField<CheckBox>(form, "_manualBrowseMainChannelCheckBox");

                Assert.That(channelNameTextBox.Enabled, Is.True);
                Assert.That(manualBrowseCheckBox.Enabled, Is.True);

                InvokeOk(form);

                Assert.That(form.SelectedProvider, Is.EqualTo(ScreenReaderProviderKind.Zdsr));
                Assert.That(form.ManualBrowseUseReaderMainChannel, Is.True);
                Assert.That(form.ScreenReaderIndependentChannelName, Is.EqualTo("Zdsr Channel"));
            }
        }

        private static T GetField<T>(ScreenReaderSettingsForm form, string fieldName) where T : class
        {
            var field = typeof(ScreenReaderSettingsForm).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(form);
        }

        private static void InvokeOk(ScreenReaderSettingsForm form)
        {
            var method = typeof(ScreenReaderSettingsForm).GetMethod(
                "OkButton_Click",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[] { null, System.EventArgs.Empty });
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
