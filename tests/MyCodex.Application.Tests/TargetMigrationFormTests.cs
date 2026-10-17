using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TargetMigrationFormTests
    {
        [Test]
        public void Migration_ExposesReadableFailureAndRetryWithoutOpeningDefaultTarget()
        {
            var attempts = 0;
            using (var form = new TargetMigrationForm(() =>
            {
                if (++attempts == 1) throw new InvalidDataException("secret-must-not-appear");
                return new TargetCatalogSnapshot { LastOpenedTargetId = "windows" };
            }))
            {
                Run(form);
                var status = form.Controls.OfType<ListBox>().Single();
                var buttons = form.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().OrderBy(b => b.TabIndex).ToArray();
                Assert.That(form.Catalog, Is.Null);
                Assert.That(buttons[0].Enabled, Is.True);
                Assert.That(buttons[1].Enabled, Is.False);
                Assert.That(buttons[2].Enabled, Is.True);
                Assert.That(string.Join(" ", status.Items.Cast<string>()), Does.Contain("InvalidDataException").And.Not.Contain("secret-must-not-appear"));
                Run(form);
                Assert.That(form.Catalog.LastOpenedTargetId, Is.EqualTo("windows"));
                Assert.That(buttons[0].Enabled, Is.False);
                Assert.That(buttons[1].Enabled, Is.True);
                Assert.That(form.AcceptButton, Is.SameAs(buttons[1]));
                Assert.That(form.CancelButton, Is.SameAs(buttons[2]));
            }
        }

        [Test]
        public void MigrationControls_HaveNamesTabOrderAndResizableLayout()
        {
            using (var form = new TargetMigrationForm(() => new TargetCatalogSnapshot()))
            {
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(560));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(280));
                Assert.That(form.Controls.OfType<ListBox>().Single().AccessibleName, Is.Not.Empty);
                var panel = form.Controls.OfType<FlowLayoutPanel>().Single();
                Assert.That(panel.TabStop, Is.False);
                Assert.That(panel.Controls.OfType<Button>().Select(b => b.TabIndex), Is.EqualTo(new[] { 1, 2, 3 }));
                Assert.That(panel.Controls.OfType<Button>().All(b => !string.IsNullOrWhiteSpace(b.AccessibleName)), Is.True);
            }
        }

        [TestCase("Strings.resx")]
        [TestCase("Strings.zh-CN.resx")]
        public void MigrationActions_HaveUniqueMnemonics(string resourceFile)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MyCodex.sln"))) directory = directory.Parent;
            Assert.That(directory, Is.Not.Null);
            var resources = XDocument.Load(Path.Combine(directory.FullName, "src", "MyCodex.WinForms", "Resources", resourceFile));
            var values = resources.Root.Elements("data").Where(e => ((string)e.Attribute("name")).StartsWith("TargetMigrationForm.Button.", StringComparison.Ordinal))
                .Select(e => e.Element("value").Value).ToArray();
            Assert.That(values.Length, Is.EqualTo(3));
            var mnemonics = values.Select(v => char.ToUpperInvariant(v[v.IndexOf('&') + 1])).ToArray();
            Assert.That(mnemonics.Distinct().Count(), Is.EqualTo(3));
        }

        private static void Run(TargetMigrationForm form)
        {
            var task = (Task)typeof(TargetMigrationForm).GetMethod("RunMigrationAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }
    }
}
