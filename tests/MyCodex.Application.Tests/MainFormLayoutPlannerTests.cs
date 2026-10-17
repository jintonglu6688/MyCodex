using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormLayoutPlannerTests
    {
        [TestCase(1024, 640)]
        [TestCase(1280, 680)]
        [TestCase(1366, 728)]
        public void DefaultBounds_FitWorkingArea(int width, int height)
        {
            var area = new Rectangle(0, 0, width, height);
            var result = MainFormLayoutPlanner.ConstrainWindowBounds(
                new Rectangle(Point.Empty, MainFormLayoutPlanner.DefaultWindowSize), area);

            Assert.That(result, Is.EqualTo(area));
        }

        [TestCase(2500, 1500, 2560, 1440, 0, 0, 1280, 680)]
        [TestCase(-2000, -100, 1100, 650, 0, 0, 1280, 680)]
        [TestCase(300, 200, 1024, 640, -1366, 40, 1366, 728)]
        [TestCase(2000, 900, 200, 100, 1920, 40, 1366, 728)]
        public void SavedBounds_ResizeAndMoveInsideTargetWorkingArea(
            int x, int y, int width, int height, int left, int top, int areaWidth, int areaHeight)
        {
            var area = new Rectangle(left, top, areaWidth, areaHeight);
            var result = MainFormLayoutPlanner.ConstrainWindowBounds(new Rectangle(x, y, width, height), area);

            Assert.That(area.Contains(result), Is.True);
            Assert.That(result.Width, Is.InRange(1024, areaWidth));
            Assert.That(result.Height, Is.InRange(640, areaHeight));
        }

        [Test]
        public void ValidBounds_RemainUnchanged()
        {
            var bounds = new Rectangle(100, 40, 1100, 650);
            Assert.That(MainFormLayoutPlanner.ConstrainWindowBounds(bounds, new Rectangle(0, 0, 1366, 728)), Is.EqualTo(bounds));
        }

        [TestCase(null)]
        [TestCase(0)]
        [TestCase(5000)]
        public void Splitters_FitInsideMinimumWindowClientArea(int? savedDistance)
        {
            using (var form = new Form { Size = MainFormLayoutPlanner.MinimumWindowSize })
            using (var tabs = new TabControl { Dock = DockStyle.Fill })
            using (var main = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = MainFormLayoutPlanner.SplitterWidth })
            using (var content = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = MainFormLayoutPlanner.SplitterWidth })
            {
                var page = new TabPage();
                tabs.TabPages.Add(page);
                form.Controls.Add(tabs);
                page.Controls.Add(main);
                main.Panel2.Controls.Add(content);
                Assert.That(form.Handle, Is.Not.EqualTo(System.IntPtr.Zero));
                Assert.That(tabs.Handle, Is.Not.EqualTo(System.IntPtr.Zero));
                page.CreateControl();
                main.SplitterDistance = MainFormLayoutPlanner.ResolveMainSplitterDistance(main.Width, savedDistance);
                content.SplitterDistance = MainFormLayoutPlanner.ResolveContentSplitterDistance(content.Width, savedDistance);

                Assert.That(main.Panel1.Width, Is.GreaterThanOrEqualTo(200));
                Assert.That(content.Panel1.Width, Is.GreaterThanOrEqualTo(440));
                Assert.That(content.Panel2.Width, Is.InRange(320, 520));
            }
        }

        [Test]
        public void Header_UsesFullWidthTitleAndSingleLineDetailsWithNonFocusableLabels()
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            var factory = typeof(MainForm).GetMethod("CreateInfoValueLabel", BindingFlags.NonPublic | BindingFlags.Static);
            var name = (Label)factory.Invoke(null, null);
            var path = (Label)factory.Invoke(null, null);
            var branch = (Label)factory.Invoke(null, null);
            name.AccessibleName = "Session title";
            path.AccessibleName = "Session details";
            branch.AccessibleName = "Sync status";
            SetField(form, "_workspaceNameValueLabel", name);
            SetField(form, "_workspacePathValueLabel", path);
            SetField(form, "_branchValueLabel", branch);
            using (var panel = (Control)typeof(MainForm).GetMethod("CreateHeaderPanel", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null))
            {
                panel.Width = 416;
                panel.PerformLayout();
                var table = (TableLayoutPanel)panel.Controls[0];
                Assert.That(table.RowCount, Is.EqualTo(2));
                Assert.That(panel.Height, Is.EqualTo(64));
                Assert.That(table.GetRow(name), Is.EqualTo(0));
                Assert.That(table.GetRow(path), Is.EqualTo(1));
                Assert.That(table.GetColumnSpan(name), Is.EqualTo(2));
                Assert.That(name.Width, Is.EqualTo(path.Width));
                Assert.That(table.Controls.Contains(branch), Is.True);
                Assert.That(branch.Visible, Is.False);
                Assert.That(name.AccessibleName, Is.EqualTo("Session title"));
                Assert.That(path.AccessibleName, Is.EqualTo("Session details"));
                Assert.That(branch.AccessibleName, Is.EqualTo("Sync status"));
                Assert.That(name.Font.Bold, Is.True);
                Assert.That(path.AutoEllipsis, Is.True);
                Assert.That(path.Height, Is.InRange(path.Font.Height, 2 * path.Font.Height - 1));
                Assert.That(table.GetColumnSpan(path), Is.EqualTo(2));
                Assert.That(name.TabStop || path.TabStop || branch.TabStop || table.TabStop || panel.TabStop, Is.False);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        [SetCulture("en-US")]
        [SetUICulture("en-US")]
        public void OfficeHeader_PreservesTitleDirectoryModelAndSyncInTwoLines(bool hasSession)
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "_sessionDefaultsService", new SessionDefaultsService());
            SetField(form, "_availableModels", new List<AppServerModelOption>());
            SetField(form, "_currentSession", hasSession ? new ChatSession { Title = "Weekly report", ModelId = "gpt-5.5" } : null);
            SetField(form, "_currentWorkspace", hasSession
                ? new WorkspaceInfo { RootPath = "/home/user/MyCodex-office", Kind = WorkspaceKind.Office } : null);
            SetField(form, "_threadSyncSummary", hasSession ? "  Up to date  " : null);
            var summary = (SessionHeaderSummary)typeof(MainForm)
                .GetMethod("BuildOfficeHeaderSummary", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(form, new object[] { "(Not selected)", "Not synced yet" });

            Assert.That(summary.TitleText, Is.EqualTo(hasSession ? "Mainline: Weekly report" : "No office session selected"));
            Assert.That(SessionHeaderSummaryBuilder.CombineDetails(summary.ContextText, summary.StatusText, "  |  "),
                Is.EqualTo(hasSession
                    ? "Office directory: /home/user/MyCodex-office  |  Model: 5.5 | Sync: Up to date"
                    : "Office directory: (Not selected)  |  Model: Default | Sync: Not synced yet"));
        }

        [Test]
        [SetCulture("en-US")]
        [SetUICulture("en-US")]
        public void EmptyCodingHeader_PreservesSelectionAndSyncFallbacks()
        {
            var summary = SessionHeaderSummaryBuilder.Build(null, null, null, "  ", "(Not selected)", "Not synced yet");
            Assert.That(summary.TitleText, Is.EqualTo("(Not selected)"));
            Assert.That(SessionHeaderSummaryBuilder.CombineDetails(summary.ContextText, summary.StatusText, "  |  "),
                Is.EqualTo("(Not selected)  |  Sync Not synced yet"));
        }

        [TestCase(null, null, "")]
        [TestCase("  ", "Sync ready", "Sync ready")]
        [TestCase("Context", " ", "Context")]
        [TestCase(" Context ", " Sync ready ", "Context / Sync ready")]
        public void CombineDetails_OmitsEmptyParts(string context, string status, string expected)
        {
            Assert.That(SessionHeaderSummaryBuilder.CombineDetails(context, status, " / "), Is.EqualTo(expected));
        }

        private static void SetField(MainForm form, string name, object value)
        {
            typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, value);
        }
    }
}
