using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationQueryParametersTests
    {
        [Test]
        public void ParameterDialogValidatesNamesAndPreservesLiteralValues()
        {
            using (var dialog = new AuthenticationQueryParameterEditForm(null, null, new[] { "existing" }))
            {
                dialog.ShowInTaskbar = false;
                dialog.Size = dialog.MinimumSize;
                dialog.Show();
                var name = Field<TextBox>(dialog, "_name");
                var value = Field<TextBox>(dialog, "_value");
                Assert.That(name.Focused, Is.True);
                Assert.That(name.AccessibleName, Is.Not.Empty);
                Assert.That(value.AccessibleName, Is.Not.Empty);
                Assert.That(dialog.Save(), Is.False);
                name.Text = " existing ";
                Assert.That(dialog.Save(), Is.False);
                Assert.That(Field<Label>(dialog, "_status").Text, Is.Not.Empty);
                Assert.That(Field<Label>(dialog, "_status").TabStop, Is.False);
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
            using (var control = new AuthenticationQueryParametersControl(original) { Dock = DockStyle.Fill })
            {
                form.Controls.Add(control);
                form.Show();
                var list = Field<ListView>(control, "_list");
                var add = Field<Button>(control, "_add");
                Assert.That(list.AccessibleName, Is.Not.Empty);
                Assert.That(control.Read(), Is.EquivalentTo(original));
                list.Focus();
                form.SelectNextControl(list, true, true, true, false);
                Assert.That(add.Focused, Is.True);
                EditDialog(form, () => add.PerformClick(), dialog =>
                {
                    Field<TextBox>(dialog, "_name").Text = "empty-value";
                    Assert.That(dialog.Save(), Is.True);
                });
                Assert.That(control.Read()["empty-value"], Is.EqualTo(string.Empty));
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo("empty-value"));
                Assert.That(list.Focused, Is.True);
                EditDialog(form, () => PressKey(list, Keys.Enter), dialog =>
                {
                    Field<TextBox>(dialog, "_name").Text = "renamed";
                    Field<TextBox>(dialog, "_value").Text = "new value";
                    Assert.That(dialog.Save(), Is.True);
                });
                Assert.That(control.Read().ContainsKey("empty-value"), Is.False);
                Assert.That(control.Read()["renamed"], Is.EqualTo("new value"));
                EditDialog(form, () => list.ContextMenu.MenuItems[0].PerformClick(), dialog =>
                {
                    Field<TextBox>(dialog, "_value").Text = "discarded";
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

        private static void EditDialog(Form parent, Action trigger, Action<AuthenticationQueryParameterEditForm> edit)
        {
            Exception failure = null;
            parent.BeginInvoke(new Action(() =>
            {
                var dialog = System.Windows.Forms.Application.OpenForms.OfType<AuthenticationQueryParameterEditForm>().Single();
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

        private static T Field<T>(object value, string name) where T : class
        {
            return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value) as T;
        }
    }
}
