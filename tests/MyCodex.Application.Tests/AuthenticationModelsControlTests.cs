using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationModelsControlTests
    {
        [Test]
        public void DiscoveryPreservesManualValuesAndDoesNotRemoveModelsOrFetchOnSelection()
        {
            using (var control = new AuthenticationModelsControl())
            {
                var original = new AuthenticationCatalogModel { Model = "a", DisplayName = "Custom", ContextWindow = 1234, ReasoningLevels = new List<string> { "high" }, BaseInstructions = "preserved" };
                control.Models = new List<AuthenticationCatalogModel> { original, new AuthenticationCatalogModel { Model = "offline", DisplayName = "Offline" } };
                control.DefaultModel = "a";
                var requests = 0;
                control.FetchRequested += (sender, args) => requests++;
                control.MergeFetched(new[] { new AuthenticationCatalogModel { Model = "a", DisplayName = "Overwrite" }, new AuthenticationCatalogModel { Model = "new" }, new AuthenticationCatalogModel { Model = "new" } });
                Assert.That(control.Models.Count, Is.EqualTo(3));
                Assert.That(control.Models[0].DisplayName, Is.EqualTo("Custom"));
                Assert.That(control.Models[0].ContextWindow, Is.EqualTo(1234));
                Assert.That(control.Models[0].BaseInstructions, Is.EqualTo("preserved"));
                Assert.That(control.Models[2].DisplayName, Is.EqualTo("new"));
                Assert.That(control.DefaultModel, Is.EqualTo("a"));
                original.ReasoningLevels.Clear();
                control.Models[0].ReasoningLevels.Clear();
                Assert.That(control.Models[0].ReasoningLevels, Is.EqualTo(new[] { "high" }));
                var list = Field<ListView>(control, "_list");
                var handle = list.Handle;
                list.Items[0].Selected = true;
                Assert.That(requests, Is.Zero);
                Assert.That(list.ContextMenu, Is.Not.Null);
                Assert.That(list.AccessibleName, Is.Not.Empty);
                control.Fetching = true;
                Assert.That(Field<Button>(control, "_fetch").Enabled, Is.False);
                Assert.That(list.Enabled, Is.True);
            }
        }

        [Test]
        public void EditorValidatesContextAndPreservesCapabilitiesNotEdited()
        {
            using (var form = new AuthenticationModelEditForm(new AuthenticationCatalogModel
            {
                Model = "a", DisplayName = "A", ContextWindow = 42, BaseInstructions = "instructions",
                ReasoningLevels = new List<string> { "high", "future" }, DefaultReasoningLevel = "high",
                InputModalities = new List<string> { "text", "audio" }, SupportsParallelToolCalls = null
            }))
            {
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                var context = Field<TextBox>(form, "_context");
                context.Text = "-2";
                Assert.That(form.Save(), Is.False);
                context.Text = "";
                Assert.That(form.Save(), Is.True);
                Assert.That(form.Model.ContextWindow, Is.Null);
                Assert.That(form.Model.BaseInstructions, Is.EqualTo("instructions"));
                Assert.That(form.Model.ReasoningLevels, Does.Contain("future"));
                Assert.That(form.Model.InputModalities, Is.EqualTo(new[] { "text", "audio" }));
                Assert.That(form.Model.SupportsParallelToolCalls, Is.Null);
                Assert.That(form.GetType().GetField("_modalities", BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
                foreach (var name in new[] { "_name", "_id", "_context", "_levels", "_default", "_parallel" })
                {
                    Assert.That(Field<Control>(form, name).AccessibleName, Is.Not.Empty);
                }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EnterOpensModelEditorWithoutSubmittingParentAndRestoresListFocus(bool save)
        {
            using (var parent = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationModelsControl { Dock = DockStyle.Fill })
            using (var submit = new Button { Text = "Save provider", Dock = DockStyle.Bottom })
            {
                var submissions = 0;
                submit.Click += (sender, args) => submissions++;
                parent.AcceptButton = submit;
                parent.Controls.Add(control);
                parent.Controls.Add(submit);
                control.Models = new[] { new AuthenticationCatalogModel { Model = "a", DisplayName = "Original" } };
                control.DefaultModel = "a";
                parent.Show();
                var list = Field<ListView>(control, "_list");
                list.Items[0].Selected = list.Items[0].Focused = true;
                list.Focus();
                var message = Message.Create(list.Handle, 0x100, (IntPtr)Keys.Enter, IntPtr.Zero);
                Assert.That(list.PreProcessControlMessage(ref message), Is.EqualTo(PreProcessControlState.MessageNeeded));

                Exception failure = null;
                parent.BeginInvoke(new Action(() =>
                {
                    var dialog = System.Windows.Forms.Application.OpenForms.OfType<AuthenticationModelEditForm>().Single();
                    try
                    {
                        Assert.That(Field<TextBox>(dialog, "_name").Focused, Is.True);
                        Field<TextBox>(dialog, "_name").Text = "Edited";
                        if (save) Assert.That(dialog.Save(), Is.True);
                        else ((Button)dialog.CancelButton).PerformClick();
                    }
                    catch (Exception error) { failure = error; }
                    finally { dialog.Close(); }
                }));
                PressKey(list, Keys.Enter);
                if (failure != null) throw failure;
                Assert.That(submissions, Is.Zero);
                Assert.That(control.Models.Single().DisplayName, Is.EqualTo(save ? "Edited" : "Original"));
                Assert.That(control.DefaultModel, Is.EqualTo("a"));
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo(save ? "Edited" : "Original"));
                Assert.That(list.Focused, Is.True);
            }
        }

        [Test]
        public void DeleteRemovesSelectedModelAndMaintainsDefaultSelectionAndFocus()
        {
            using (var parent = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationModelsControl { Dock = DockStyle.Fill })
            {
                var original = new[]
                {
                    new AuthenticationCatalogModel { Model = "a", DisplayName = "A" },
                    new AuthenticationCatalogModel { Model = "b", DisplayName = "B" },
                    new AuthenticationCatalogModel { Model = "c", DisplayName = "C" }
                };
                control.Models = original;
                control.DefaultModel = "b";
                parent.Controls.Add(control);
                parent.Show();
                var list = Field<ListView>(control, "_list");
                list.Items[1].Selected = list.Items[1].Focused = true;
                list.Focus();
                var changes = 0;
                control.ModelsChanged += (sender, args) => changes++;

                var modifiedKey = new KeyEventArgs(Keys.Control | Keys.Delete);
                typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(list, new object[] { modifiedKey });
                Assert.That(control.Models.Count, Is.EqualTo(3));
                PressKey(list, Keys.Delete);
                Assert.That(control.Models.Select(model => model.Model), Is.EqualTo(new[] { "a", "c" }));
                Assert.That(control.DefaultModel, Is.Null);
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo("C"));
                Assert.That(list.Focused, Is.True);
                PressKey(list, Keys.Delete);
                Assert.That(list.SelectedItems[0].Text, Is.EqualTo("A"));
                PressKey(list, Keys.Delete);
                Assert.That(control.Models, Is.Empty);
                Assert.That(list.Focused, Is.True);
                PressKey(list, Keys.Delete);
                Assert.That(changes, Is.EqualTo(3));
                Assert.That(original.Length, Is.EqualTo(3));
            }
        }

        [Test]
        public void NewModelKeepsInputCapabilitiesUnspecified()
        {
            using (var form = new AuthenticationModelEditForm())
            {
                Field<TextBox>(form, "_name").Text = "New";
                Field<TextBox>(form, "_id").Text = "new-model";
                Assert.That(form.Save(), Is.True);
                Assert.That(form.Model.InputModalities, Is.Null);
            }
        }

        [TestCase("zh-CN")]
        [TestCase("en-US")]
        public void EditorLocalizesReasoningLabelsAndSavesOriginalIds(string culture)
        {
            var previousCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                var ids = new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra", "future" };
                using (var form = new AuthenticationModelEditForm(new AuthenticationCatalogModel
                {
                    Model = "a", DisplayName = "A", ReasoningLevels = ids.ToList(), DefaultReasoningLevel = "xhigh"
                }))
                {
                    var levels = Field<CheckedListBox>(form, "_levels");
                    var defaults = Field<ComboBox>(form, "_default");
                    Assert.That(levels.Items.Cast<string>(), Is.EqualTo(ids));
                    foreach (var id in ids)
                    {
                        var text = MainFormModelOptionComposer.GetReasoningEffortText(id);
                        Assert.That(levels.GetItemText(id), Is.EqualTo(text));
                        Assert.That(levels.AccessibilityObject.GetChild(levels.Items.IndexOf(id)).Name, Is.EqualTo(text));
                        Assert.That(defaults.GetItemText(defaults.Items[defaults.Items.IndexOf(id)]), Is.EqualTo(text));
                    }
                    Assert.That(defaults.GetItemText(defaults.SelectedItem), Is.EqualTo(culture == "zh-CN" ? "超高" : "Extra High"));
                    defaults.SelectedIndex = defaults.Items.IndexOf("future");
                    Assert.That(form.Save(), Is.True);
                    Assert.That(form.Model.ReasoningLevels, Is.EqualTo(ids));
                    Assert.That(form.Model.DefaultReasoningLevel, Is.EqualTo("future"));
                    defaults.SelectedIndex = defaults.Items.IndexOf("high");
                    Assert.That(form.Save(), Is.True);
                    Assert.That(form.Model.DefaultReasoningLevel, Is.EqualTo("high"));
                }
            }
            finally { Thread.CurrentThread.CurrentUICulture = previousCulture; }
        }

        [Test]
        public void UncheckingDefaultReasoningFallsBackToAutomatic()
        {
            using (var form = new AuthenticationModelEditForm(new AuthenticationCatalogModel { Model = "a", DisplayName = "A", ReasoningLevels = new List<string> { "high" }, DefaultReasoningLevel = "high" }))
            {
                var levels = Field<CheckedListBox>(form, "_levels");
                levels.SetItemChecked(levels.Items.IndexOf("high"), false);
                Assert.That(Field<ComboBox>(form, "_default").SelectedIndex, Is.Zero);
                Assert.That(form.Save(), Is.True);
                Assert.That(form.Model.DefaultReasoningLevel, Is.Null);
            }
        }

        private static void PressKey(Control control, Keys key)
        {
            var args = new KeyEventArgs(key);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(control, new object[] { args });
            Assert.That(args.SuppressKeyPress, Is.True);
        }

        private static T Field<T>(object value, string name) where T : class
        {
            return typeof(T).IsAssignableFrom(value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).FieldType)
                ? value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value) as T : null;
        }
    }
}
