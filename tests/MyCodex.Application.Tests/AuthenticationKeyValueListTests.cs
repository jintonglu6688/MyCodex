using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationKeyValueListTests
    {
        [Test]
        public void ParameterDialogValidatesNamesAndPreservesLiteralValues()
        {
            using (var dialog = new AuthenticationKeyValueEditForm(AuthenticationKeyValueTexts.Query(), null, null, null, new[] { "existing" }))
            {
                dialog.ShowInTaskbar = false;
                dialog.Size = dialog.MinimumSize;
                dialog.Show();
                var name = FormTestDriver.Field<TextBox>(dialog, "_name");
                var value = FormTestDriver.Field<TextBox>(dialog, "_value");
                Assert.That(name.Focused, Is.True);
                Assert.That(name.AccessibleName, Is.Not.Empty);
                Assert.That(value.AccessibleName, Is.Not.Empty);
                Assert.That(dialog.Save(), Is.False);
                name.Text = " existing ";
                Assert.That(dialog.Save(), Is.False);
                Assert.That(FormTestDriver.Field<Label>(dialog, "_status").Text, Is.Not.Empty);
                Assert.That(FormTestDriver.Field<Label>(dialog, "_status").TabStop, Is.False);
                name.Text = " new-name ";
                value.Text = "  a&b=中文  ";
                dialog.SelectNextControl(name, true, true, true, false);
                Assert.That(value.Focused, Is.True);
                dialog.SelectNextControl(value, true, true, true, false);
                Assert.That(((Button)dialog.AcceptButton).Focused, Is.True);
                Assert.That(dialog.Save(), Is.True);
                Assert.That(dialog.ParameterName, Is.EqualTo("new-name"));
                Assert.That(dialog.ParameterValue, Is.EqualTo("  a&b=中文  "));
            }
        }

        [Test]
        public void QueryListSupportsAddEditCancelAndDeleteWithoutChangingOriginal()
        {
            var original = new Dictionary<string, object> { { "api-version", "v1" }, { "token", "********" } };
            using (var form = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationKeyValueListControl(original, AuthenticationKeyValueTexts.Query()) { Dock = DockStyle.Fill })
            {
                form.Controls.Add(control);
                form.Show();
                var list = FormTestDriver.Field<ListView>(control, "_list");
                var add = FormTestDriver.Field<Button>(control, "_add");
                Assert.That(list.AccessibleName, Is.Not.Empty);
                Assert.That(control.Read(), Is.EquivalentTo(original));
                list.Focus();
                form.SelectNextControl(list, true, true, true, false);
                Assert.That(add.Focused, Is.True);
                EditDialog(form, () => add.PerformClick(), dialog =>
                {
                    FormTestDriver.Field<TextBox>(dialog, "_name").Text = "empty-value";
                    Assert.That(dialog.Save(), Is.True);
                });
                Assert.That(control.Read()["empty-value"], Is.EqualTo(string.Empty));
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo("empty-value"));
                Assert.That(list.Focused, Is.True);
                EditDialog(form, () => PressKey(list, Keys.Enter), dialog =>
                {
                    FormTestDriver.Field<TextBox>(dialog, "_name").Text = "renamed";
                    FormTestDriver.Field<TextBox>(dialog, "_value").Text = "new value";
                    Assert.That(dialog.Save(), Is.True);
                });
                Assert.That(control.Read().ContainsKey("empty-value"), Is.False);
                Assert.That(control.Read()["renamed"], Is.EqualTo("new value"));
                EditDialog(form, () => list.ContextMenu.MenuItems[0].PerformClick(), dialog =>
                {
                    FormTestDriver.Field<TextBox>(dialog, "_value").Text = "discarded";
                    ((Button)dialog.CancelButton).PerformClick();
                });
                Assert.That(control.Read()["renamed"], Is.EqualTo("new value"));
                PressKey(list, Keys.Delete);
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo("token"));
                PressKey(list, Keys.Delete);
                PressKey(list, Keys.Delete);
                Assert.That(control.Read(), Is.Empty);
                Assert.That(add.Focused, Is.True);
                Assert.That(original.Count, Is.EqualTo(2));
                Assert.That(original["token"], Is.EqualTo("********"));
            }
        }

        [TestCase("responses")]
        [TestCase("chat_completions")]
        [TestCase("anthropic")]
        public void AdvancedSettingsRoundTripQueryParameters(string kind)
        {
            var values = new Dictionary<string, object> { { "api-version", "v1" }, { "token", "********" } };
            using (var advanced = new AuthenticationAdvancedControl(new Dictionary<string, object> { { "queryParams", values } }) { Kind = kind })
            {
                Assert.That((Dictionary<string, object>)advanced.Read()["queryParams"], Is.EquivalentTo(values));
            }
        }

        [Test]
        public void TypedDialogValidatesValuesByTypeAndKeepsStructuredValues()
        {
            using (var dialog = new AuthenticationKeyValueEditForm(AuthenticationKeyValueTexts.Body(), null, null, AuthenticationKeyValueEditForm.StringType, new string[0]))
            {
                dialog.ShowInTaskbar = false;
                dialog.Show();
                var name = FormTestDriver.Field<TextBox>(dialog, "_name");
                var value = FormTestDriver.Field<TextBox>(dialog, "_value");
                var type = FormTestDriver.Field<ComboBox>(dialog, "_type");
                Assert.That(type.AccessibleName, Is.Not.Empty);
                dialog.SelectNextControl(value, true, true, true, false);
                Assert.That(type.Focused, Is.True, "Tab after the value must reach the value type.");
                name.Text = "max_tokens";
                type.SelectedItem = "number";
                value.Text = "many";
                Assert.That(dialog.Save(), Is.False);
                Assert.That(FormTestDriver.Field<Label>(dialog, "_status").Text, Is.Not.Empty);
                Assert.That(value.Focused, Is.True);
                value.Text = " 1.5 ";
                Assert.That(dialog.Save(), Is.True);
                Assert.That(dialog.ParameterValue, Is.EqualTo(1.5m));
                Assert.That(dialog.ValueType, Is.EqualTo("number"));
                type.SelectedItem = "boolean";
                value.Text = "true";
                Assert.That(dialog.Save(), Is.True);
                Assert.That(dialog.ParameterValue, Is.EqualTo(true));
                type.SelectedItem = "null";
                Assert.That(value.Enabled, Is.False);
                Assert.That(dialog.Save(), Is.True);
                Assert.That(dialog.ParameterValue, Is.Null);
            }
            var nested = new Dictionary<string, object> { { "type", "enabled" } };
            using (var dialog = new AuthenticationKeyValueEditForm(AuthenticationKeyValueTexts.Body(), "thinking", nested, "preserved", new string[0]))
            {
                dialog.ShowInTaskbar = false;
                dialog.Show();
                Assert.That(FormTestDriver.Field<ComboBox>(dialog, "_type").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(dialog, "_value").ReadOnly, Is.True);
                FormTestDriver.Field<TextBox>(dialog, "_name").Text = "reasoning";
                Assert.That(dialog.Save(), Is.True);
                Assert.That(dialog.ParameterValue, Is.SameAs(nested));
            }
        }

        [Test]
        public void TypedListShowsTypesAndRoundTripsEditedValues()
        {
            var nested = new Dictionary<string, object> { { "type", "enabled" } };
            var original = new Dictionary<string, object> { { "X-Token", "********" }, { "temperature", 0.2m }, { "stream", true }, { "stop", null }, { "thinking", nested } };
            using (var form = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationKeyValueListControl(original, AuthenticationKeyValueTexts.Body()) { Dock = DockStyle.Fill })
            {
                form.Controls.Add(control);
                form.Show();
                var list = FormTestDriver.Field<ListView>(control, "_list");
                Assert.That(list.Columns.Count, Is.EqualTo(3));
                Assert.That(list.Items.Cast<ListViewItem>().Select(item => item.SubItems[2].Text),
                    Is.EqualTo(new[] { "string", "number", "boolean", "null", "preserved" }));
                Assert.That(list.Items[2].SubItems[1].Text, Is.EqualTo("true"));
                var read = control.Read();
                Assert.That(read, Is.EquivalentTo(original));
                Assert.That(read["thinking"], Is.SameAs(nested));
                list.Items[1].Selected = true;
                EditDialog(form, () => PressKey(list, Keys.Enter), dialog =>
                {
                    Assert.That(FormTestDriver.Field<ComboBox>(dialog, "_type").SelectedItem, Is.EqualTo("number"));
                    FormTestDriver.Field<TextBox>(dialog, "_value").Text = "0.7";
                    Assert.That(dialog.Save(), Is.True);
                });
                Assert.That(control.Read()["temperature"], Is.EqualTo(0.7m));
                Assert.That(original["temperature"], Is.EqualTo(0.2m));
            }
        }

        [TestCase("chat_completions")]
        [TestCase("anthropic")]
        public void AdvancedSettingsRoundTripHeadersAndBodyOverrides(string kind)
        {
            var headers = new Dictionary<string, object> { { "X-Trace", "on" } };
            var body = new Dictionary<string, object> { { "max_tokens", 4096 }, { "nested", new Dictionary<string, object> { { "a", 1 } } } };
            using (var advanced = new AuthenticationAdvancedControl(new Dictionary<string, object> { { "requestHeaders", headers }, { "requestBody", body } }) { Kind = kind })
            {
                var result = advanced.Read();
                Assert.That((Dictionary<string, object>)result["requestHeaders"], Is.EquivalentTo(headers));
                Assert.That((Dictionary<string, object>)result["requestBody"], Is.EquivalentTo(body));
            }
        }

        private static void EditDialog(Form parent, Action trigger, Action<AuthenticationKeyValueEditForm> edit)
        {
            Exception failure = null;
            parent.BeginInvoke(new Action(() =>
            {
                var dialog = System.Windows.Forms.Application.OpenForms.OfType<AuthenticationKeyValueEditForm>().Single();
                try { edit(dialog); }
                catch (Exception error) { failure = error; }
                finally { dialog.Close(); }
            }));
            trigger();
            if (failure != null) throw failure;
        }

        private static void PressKey(Control control, Keys key)
        {
            var args = new KeyEventArgs(key);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { args });
            Assert.That(args.SuppressKeyPress, Is.True);
        }
    }
}
