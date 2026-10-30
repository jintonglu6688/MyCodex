using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class UiScalingTests
    {
        private object _originalScale;
        private object _originalFamily;
        private CultureInfo _originalCulture;
        private static readonly FieldInfo ScaleField = typeof(UiTheme).GetField("_uiFontScalePercent", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo FamilyField = typeof(UiTheme).GetField("_uiFontFamily", BindingFlags.Static | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            _originalScale = ScaleField.GetValue(null);
            _originalFamily = FamilyField.GetValue(null);
            _originalCulture = Thread.CurrentThread.CurrentUICulture;
            ScaleField.SetValue(null, null);
            FamilyField.SetValue(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            ScaleField.SetValue(null, _originalScale);
            FamilyField.SetValue(null, _originalFamily);
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
        }

        [TestCase(100)]
        [TestCase(125)]
        [TestCase(150)]
        public void ScaledDialog_PreservesInitialFocus(int percent)
        {
            UiTheme.InitializeUiFontScale(percent);
            using (var form = new AuthenticationKeyValueEditForm(AuthenticationKeyValueTexts.Query(), null, null, null, new string[0]))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var name = (Control)form.GetType().GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert.That(name.Focused, Is.True);
            }
        }

        [TestCase(100, false)]
        [TestCase(125, false)]
        [TestCase(150, false)]
        [TestCase(100, true)]
        [TestCase(125, true)]
        [TestCase(150, true)]
        public void ScaledModalDialogWithMinimumSize_FocusesInitialControlWhenShown(int percent, bool presetActiveControl)
        {
            UiTheme.InitializeUiFontScale(percent);
            using (var owner = new Form { ShowInTaskbar = false })
            using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(400, 300), Size = new Size(500, 400) })
            {
                UiTheme.ApplyForm(form);
                var list = new ListBox { Dock = DockStyle.Fill, TabIndex = 0 };
                list.Items.Add("item");
                var close = new Button { Dock = DockStyle.Bottom, TabIndex = 1, Text = "Close" };
                form.Controls.Add(list);
                form.Controls.Add(close);
                if (presetActiveControl)
                {
                    form.ActiveControl = close;
                }

                var expected = presetActiveControl ? (Control)close : list;
                var focusedWhenShown = false;
                form.Shown += (sender, e) => form.BeginInvoke(new Action(() =>
                {
                    focusedWhenShown = expected.Focused;
                    form.Close();
                }));
                owner.Show();
                form.ShowDialog(owner);

                Assert.That(focusedWhenShown, Is.True);
            }
        }

        [TestCase(100, 200)]
        [TestCase(125, 200)]
        [TestCase(150, 200)]
        [TestCase(100, 280)]
        [TestCase(125, 280)]
        [TestCase(150, 280)]
        public void SavedSplitters_RestoreLogicalWidthsAtCurrentScale(int percent, int navigationWidth)
        {
            ScaleField.SetValue(null, percent);
            Assert.That(UiTheme.ScaleFactor, Is.EqualTo(percent / 100F), "This test requires 96 DPI.");

            Assert.That(MainFormLayoutPlanner.ResolveMainSplitterDistance(UiTheme.Scale(1500), navigationWidth),
                Is.EqualTo(UiTheme.Scale(navigationWidth)));
            Assert.That(MainFormLayoutPlanner.ResolveContentSplitterDistance(UiTheme.Scale(1106), 700),
                Is.EqualTo(UiTheme.Scale(700)));
        }

        [TestCase(true, "Minimum")]
        [TestCase(false, "Minimum")]
        [TestCase(true, "Distance")]
        [TestCase(false, "Distance")]
        [TestCase(true, "Layout")]
        [TestCase(false, "Layout")]
        public void ProgrammaticSplitterChanges_PreserveSavedWidths(bool isMain, string adjustment)
        {
            ScaleField.SetValue(null, 125);
            var form = Uninitialized<MainForm>();
            var snapshot = new AppStateSnapshot { MainSplitterDistance = 250, ContentSplitterDistance = 700 };
            using (var main = new SplitContainer { Width = UiTheme.Scale(1500), SplitterDistance = 50 })
            using (var content = new SplitContainer { Width = UiTheme.Scale(1106), SplitterDistance = 50 })
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainForm).GetField("_appStateSnapshot", flags).SetValue(form, snapshot);
                typeof(MainForm).GetField("_mainSplitContainer", flags).SetValue(form, main);
                typeof(MainForm).GetField("_contentSplitContainer", flags).SetValue(form, content);
                main.SplitterMoved += (sender, e) => typeof(MainForm).GetMethod("OnMainSplitterMoved", flags)
                    .Invoke(form, new object[] { sender, e });
                content.SplitterMoved += (sender, e) => typeof(MainForm).GetMethod("OnContentSplitterMoved", flags)
                    .Invoke(form, new object[] { sender, e });
                var split = isMain ? main : content;
                var moved = false;
                split.SplitterMoved += (sender, e) => moved = true;

                if (adjustment == "Minimum")
                {
                    split.Panel1MinSize = UiTheme.Scale(isMain ? 200 : 440);
                }
                else if (adjustment == "Distance")
                {
                    split.SplitterDistance = UiTheme.Scale(isMain ? 300 : 600);
                }
                else
                {
                    typeof(MainForm).GetMethod("ApplySplitLayout", flags).Invoke(form, null);
                    Assert.That(main.SplitterDistance, Is.EqualTo(UiTheme.Scale(250)));
                    Assert.That(content.SplitterDistance, Is.EqualTo(UiTheme.Scale(700)));
                }

                Assert.That(moved, Is.True, "The adjustment must exercise SplitterMoved.");
                Assert.That(snapshot.MainSplitterDistance, Is.EqualTo(250));
                Assert.That(snapshot.ContentSplitterDistance, Is.EqualTo(700));
            }
        }

        [TestCase(100)]
        [TestCase(125)]
        [TestCase(150)]
        public void MovedSplitters_SaveLogicalWidths(int percent)
        {
            ScaleField.SetValue(null, percent);
            var form = Uninitialized<MainForm>();
            var snapshot = new AppStateSnapshot();
            using (var main = new SplitContainer { Width = UiTheme.Scale(1500), SplitterDistance = UiTheme.Scale(280) })
            using (var content = new SplitContainer { Width = UiTheme.Scale(1106), SplitterDistance = UiTheme.Scale(700) })
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(MainForm).GetField("_appStateSnapshot", flags).SetValue(form, snapshot);
                typeof(MainForm).GetField("_mainSplitContainer", flags).SetValue(form, main);
                typeof(MainForm).GetField("_contentSplitContainer", flags).SetValue(form, content);
                typeof(MainForm).GetMethod("OnMainSplitterMoving", flags).Invoke(form, new object[] { main, null });
                typeof(MainForm).GetMethod("OnContentSplitterMoved", flags).Invoke(form, new object[] { content, null });
                Assert.That(snapshot.ContentSplitterDistance, Is.Null, "Each splitter must track its own user movement.");
                typeof(MainForm).GetMethod("OnContentSplitterMoving", flags).Invoke(form, new object[] { content, null });
                typeof(MainForm).GetMethod("OnMainSplitterMoved", flags).Invoke(form, new object[] { main, null });
                typeof(MainForm).GetMethod("OnContentSplitterMoved", flags).Invoke(form, new object[] { content, null });

                Assert.That(snapshot.MainSplitterDistance, Is.EqualTo(280));
                Assert.That(snapshot.ContentSplitterDistance, Is.EqualTo(700));

                main.SplitterDistance = UiTheme.Scale(300);
                content.SplitterDistance = UiTheme.Scale(600);
                typeof(MainForm).GetMethod("OnMainSplitterMoved", flags).Invoke(form, new object[] { main, null });
                typeof(MainForm).GetMethod("OnContentSplitterMoved", flags).Invoke(form, new object[] { content, null });
                Assert.That(snapshot.MainSplitterDistance, Is.EqualTo(280), "User movement must be consumed once.");
                Assert.That(snapshot.ContentSplitterDistance, Is.EqualTo(700), "User movement must be consumed once.");
            }
        }

        [TestCase(100)]
        [TestCase(125)]
        [TestCase(150)]
        public void ScaleAndUnscale_RoundTripWithinOnePixel(int percent)
        {
            ScaleField.SetValue(null, percent);
            Assert.That(UiTheme.ScaleFactor, Is.EqualTo(percent / 100F), "This test requires 96 DPI.");

            for (var pixels = 0; pixels <= 2000; pixels++)
            {
                Assert.That(UiTheme.Scale(UiTheme.Unscale(pixels)), Is.EqualTo(pixels).Within(1),
                    "Device pixels: " + pixels);
            }
        }

        [TestCase(96, 1F, 1F)]
        [TestCase(120, 1.25F, 1.5625F)]
        [TestCase(144, 1.5F, 2.25F)]
        [TestCase(192, 1.5F, 3F)]
        public void ScaleFactor_CombinesDpiAndUserFont(float dpi, float userScale, float expected)
        {
            Assert.That(UiTheme.CalculateScaleFactor(dpi, userScale), Is.EqualTo(expected));
        }

        [Test]
        public void FontScale_IsFixedForProcess_AndScalesFontsAndGlyphsOnce()
        {
            UiTheme.InitializeUiFontScale(150);
            UiTheme.InitializeUiFontScale(100);
            using (var ui = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular))
            using (var code = UiTheme.CreateCodeFont(UiTheme.ReadingFontSize, FontStyle.Regular))
            using (var icon = UiTheme.CreateGlyph(UiGlyph.Ok, UiTheme.ButtonIconSize))
            {
                Assert.That(ui.SizeInPoints, Is.EqualTo(13.5F));
                Assert.That(code.SizeInPoints, Is.EqualTo(15F));
                Assert.That(icon.Width, Is.EqualTo(UiTheme.Scale(16)));
            }
        }

        [Test]
        public void ThemeRefresh_PreservesScaledButtonMetricsBeforeItsHandleExists()
        {
            UiTheme.InitializeUiFontScale(150);
            using (var button = new Button())
            {
                UiTheme.ApplyButton(button, UiGlyph.Ok);
                button.Scale(new SizeF(1.5F, 1.5F));
                var height = button.Height;
                var padding = button.Padding;
                UiTheme.ApplyButton(button, UiGlyph.Ok);
                Assert.That(button.IsHandleCreated, Is.False);
                Assert.That(button.Height, Is.EqualTo(height));
                Assert.That(button.Padding, Is.EqualTo(padding));
            }
        }

        [TestCase(1F, "en-US")]
        [TestCase(1.875F, "en-US")]
        [TestCase(1F, "zh-CN")]
        [TestCase(1.875F, "zh-CN")]
        public void ActivityCommands_AtMinimumWidth_KeepBothButtonsInsidePanel(float scale, string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            var main = Uninitialized<MainForm>();
            using (var host = new Form { AutoScaleMode = AutoScaleMode.None })
            using (var font = new Font(UiTheme.ResolveUiFontFamily(culture), UiTheme.BodyFontSize * scale))
            using (var terminal = new Button { Text = "Terminal(&K)", AccessibleName = "Terminal", TabIndex = 0 })
            using (var git = new Button { Text = "Git Workbench(&H)", AccessibleName = "Git Workbench", TabIndex = 1 })
            {
                host.Font = font;
                UiTheme.ApplyButton(terminal, UiGlyph.Command);
                UiTheme.ApplyButton(git, UiGlyph.FileChange);
                typeof(MainForm).GetField("_commandMenuButton", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, terminal);
                typeof(MainForm).GetField("_diffWorkbenchButton", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, git);
                typeof(MainForm).GetField("_activityCurrentStatusLabel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, new Label());
                typeof(MainForm).GetField("_activityBrowseControl", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(main, new MyCodex.WinForms.Controls.ActivityVirtualBrowseControl());
                var activity = (Control)typeof(MainForm).GetMethod("CreateRightPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, null);
                host.Controls.Add(activity);
                // Simulate the combined DPI/user-font factor without changing the process DPI or supported settings.
                activity.Scale(new SizeF(scale, scale));
                host.ClientSize = new Size((int)Math.Round(MainFormLayoutPlanner.MinimumActivityWidth * scale), (int)Math.Round(300 * scale));
                CreateHandles(host);
                var panel = terminal.Parent;
                Assert.That(activity.ClientRectangle.Contains(panel.Bounds), Is.True);
                foreach (var button in new[] { terminal, git })
                {
                    Assert.That(panel.ClientRectangle.Contains(button.Bounds), Is.True, button.Text);
                    Assert.That(button.Width, Is.GreaterThan(0));
                    Assert.That(button.Height, Is.GreaterThanOrEqualTo(UiTheme.ButtonHeight * scale - 1));
                    Assert.That(button.TabStop, Is.True);
                }
                Assert.That(git.Left - terminal.Right, Is.EqualTo(Math.Round(UiTheme.ControlSpacing * scale)).Within(1));
                Assert.That(panel.TabStop, Is.False);
                Assert.That(terminal.TabIndex, Is.EqualTo(0));
                Assert.That(git.TabIndex, Is.EqualTo(1));
                Assert.That(terminal.Text, Is.EqualTo("Terminal(&K)"));
                Assert.That(git.Text, Is.EqualTo("Git Workbench(&H)"));
                Assert.That(terminal.AccessibleName, Is.EqualTo("Terminal"));
                Assert.That(git.AccessibleName, Is.EqualTo("Git Workbench"));
            }
        }

        [TestCase(100)]
        [TestCase(125)]
        [TestCase(150)]
        public void StatusStrip_FontAndIndicatorFollowUserScale_AndKeepStatusText(int percent)
        {
            UiTheme.InitializeUiFontScale(percent);
            using (var strip = new StatusStrip { Width = UiTheme.Scale(800), TabStop = false })
            using (var indicator = UiTheme.CreateStatusIndicator())
            using (var label = new ToolStripStatusLabel("已恢复最近状态"))
            {
                strip.Items.Add(indicator);
                strip.Items.Add(label);
                UiTheme.ApplyStatusStrip(strip);
                CreateHandles(strip);
                Assert.That(strip.Font.SizeInPoints, Is.EqualTo(UiTheme.BodyFontSize * percent / 100F).Within(0.01F));
                Assert.That(label.Font.SizeInPoints, Is.EqualTo(strip.Font.SizeInPoints));
                Assert.That(label.Text, Is.EqualTo("已恢复最近状态"));
                Assert.That(label.Visible, Is.True);
                Assert.That(strip.ClientRectangle.Contains(label.Bounds), Is.True);
                Assert.That(indicator.Image.Size, Is.EqualTo(new Size(UiTheme.Scale(16), UiTheme.Scale(16))));
                Assert.That(indicator.Width, Is.EqualTo(UiTheme.Scale(20)));
                Assert.That(indicator.ImageScaling, Is.EqualTo(ToolStripItemImageScaling.None));
                Assert.That(strip.TabStop, Is.False);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [TestCase("{}", 100)]
        [TestCase("{\"UiFontScalePercent\":125}", 125)]
        [TestCase("{\"UiFontScalePercent\":150}", 150)]
        [TestCase("{\"UiFontScalePercent\":0}", 100)]
        [TestCase("{\"UiFontScalePercent\":999}", 100)]
        public void Config_DefaultLegacyAndInvalidValues_RoundTrip(string json, int expected)
        {
            Assert.That(new AppConfigSnapshot().UiFontScalePercent, Is.EqualTo(100));
            var serializer = new DataContractJsonSerializer(typeof(AppConfigSnapshot));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var config = (AppConfigSnapshot)serializer.ReadObject(stream);
                Assert.That(config.UiFontScalePercent, Is.EqualTo(expected));
                var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "font-scale-" + Guid.NewGuid().ToString("N"));
                var store = new JsonAppConfigStore(new AppConfigService(path));
                store.Save(config);
                Assert.That(store.Load().UiFontScalePercent, Is.EqualTo(expected));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Net48_AutoScale_EarlyAndLateApplyForm_ScalesOnce(bool applyBeforeChildren)
        {
            UiTheme.InitializeUiFontScale(150);
            using (var form = new Form { Size = new Size(600, 400), MinimumSize = new Size(400, 300) })
            {
                if (applyBeforeChildren)
                {
                    UiTheme.ApplyForm(form);
                }
                var split = new SplitContainer { Size = new Size(500, 200), SplitterDistance = 200, Panel1MinSize = 60 };
                var list = new ListView { Size = new Size(150, 100), View = View.Details };
                list.Columns.Add("Column", 100);
                split.Panel1.Controls.Add(list);
                form.Controls.Add(split);
                if (!applyBeforeChildren)
                {
                    UiTheme.ApplyForm(form);
                }
                Assert.That(form.Size, Is.EqualTo(new Size(600, 400)), "Form scaling waits for the complete tree.");
                CreateHandles(form);
                var factor = new SizeF(form.CurrentAutoScaleDimensions.Width / UiTheme.GetBaselineAutoScaleDimensions().Width,
                    form.CurrentAutoScaleDimensions.Height / UiTheme.GetBaselineAutoScaleDimensions().Height);
                Assert.That(split.Width, Is.EqualTo(Math.Round(500 * factor.Width)).Within(1));
                Assert.That(split.Height, Is.EqualTo(Math.Round(200 * factor.Height)).Within(1));
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(400));
                Assert.That(form.MinimumSize.Height, Is.GreaterThan(300));
                Assert.That(split.SplitterDistance, Is.EqualTo(Math.Round(200 * factor.Width)).Within(2));
                Assert.That(split.Panel1MinSize, Is.EqualTo(60), "Panel minima require their layout owner to scale them.");
                Assert.That(list.Columns[0].Width, Is.EqualTo(UiTheme.Scale(100)));
                var size = form.Size;
                var minimum = form.MinimumSize;
                UiTheme.ApplyToControlTree(form);
                form.PerformAutoScale();
                Assert.That(form.Size, Is.EqualTo(size));
                Assert.That(form.MinimumSize, Is.EqualTo(minimum));
                Assert.That(list.Columns[0].Width, Is.EqualTo(UiTheme.Scale(100)));
                TestContext.WriteLine("factor={0}, form={1}, minimum={2}, split={3}, column={4}", factor, size, minimum, split.SplitterDistance, list.Columns[0].Width);
            }
        }

        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void Options_FontScaleHasAccessibleLabel_AndAppliesOnConfirmation(string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            using (var form = new OptionsForm(new AppConfigSnapshot { UiFontScalePercent = 125 }, null))
            {
                var combo = (ComboBox)typeof(OptionsForm).GetField("_uiFontScaleComboBox", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert.That(combo.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                Assert.That(combo.AccessibleName, Is.Not.Empty);
                Assert.That(combo.SelectedItem, Is.EqualTo("125%"));
                Assert.That(combo.TabIndex, Is.EqualTo(11));
                var label = combo.Parent.Controls.OfType<Label>().Single(c => c.AccessibleName == combo.AccessibleName);
                Assert.That(label.Text, Does.Contain("&Z"));
                Assert.That(label.TabStop, Is.False);
                combo.SelectedIndex = 2;
                Assert.That(form.UiFontScalePercent, Is.EqualTo(125));
                typeof(OptionsForm).GetMethod("OkButton_Click", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { form, EventArgs.Empty });
                Assert.That(form.UiFontScalePercent, Is.EqualTo(150));
                Assert.That(UiTheme.UserFontScale, Is.EqualTo(1F));
            }
        }

        [TestCase("Options", "en-US")]
        [TestCase("Model", "en-US")]
        [TestCase("Permissions", "en-US")]
        [TestCase("Reader", "en-US")]
        [TestCase("Authentication", "en-US")]
        [TestCase("Automation", "en-US")]
        [TestCase("Plugins", "en-US")]
        [TestCase("Git", "en-US")]
        [TestCase("Options", "zh-CN")]
        [TestCase("Model", "zh-CN")]
        [TestCase("Permissions", "zh-CN")]
        [TestCase("Reader", "zh-CN")]
        [TestCase("Authentication", "zh-CN")]
        [TestCase("Automation", "zh-CN")]
        [TestCase("Plugins", "zh-CN")]
        [TestCase("Git", "zh-CN")]
        public void RepresentativeForms_150Percent_TextAndControlsFit(string name, string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            UiTheme.InitializeUiFontScale(150);
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                Assert.That(graphics.DpiX, Is.EqualTo(96), "This empirical test requires the specified 96 DPI environment.");
            }
            using (var form = CreateForm(name))
            {
                // Creating handles exercises native layout without invoking Shown/network refreshes.
                CreateHandles(form);
                AssertLayout(form, name);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void MainWindow_150Percent_ClampsMinimumBeforeBoundsAndKeepsSplitsUsable(string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            UiTheme.InitializeUiFontScale(150);
            UiTheme.InitializeUiFontFamily(culture);
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory, "main-scale-" + Guid.NewGuid().ToString("N")));
            var config = new JsonAppConfigStore(paths);
            var runtime = new CodexRuntimeManager(paths);
            using (var client = new AppServerClient(runtime, config))
            using (var form = new MainForm(client, new JsonAppStateStore(paths), config, paths, new SilentReader(), runtime))
            {
                try
                {
                    CreateHandles(form);
                    typeof(MainForm).GetMethod("MainForm_Shown", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { form, EventArgs.Empty });
                    var area = Screen.FromRectangle(form.Bounds).WorkingArea;
                    Assert.That(area.Contains(form.Bounds), Is.True);
                    Assert.That(form.MinimumSize.Width, Is.LessThanOrEqualTo(area.Width));
                    Assert.That(form.MinimumSize.Height, Is.LessThanOrEqualTo(area.Height));
                    var workspaces = (ListView)typeof(MainForm).GetField("_workspaceList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Assert.That(workspaces.Columns.Cast<ColumnHeader>().Sum(c => c.Width), Is.LessThanOrEqualTo(workspaces.ClientSize.Width), "Fitted columns must not be scaled a second time.");
                    foreach (var field in new[] { "_mainSplitContainer", "_contentSplitContainer" })
                    {
                        var split = (SplitContainer)typeof(MainForm).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                        Assert.That(split.Panel1.Width, Is.GreaterThan(0));
                        Assert.That(split.Panel2.Width, Is.GreaterThan(0));
                        Assert.That(split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth, Is.LessThanOrEqualTo(split.Width));
                    }
                }
                finally
                {
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                        Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), form, "OnSystemUserPreferenceChanged");
                    foreach (var method in new[] { "DisposeThreadSyncTimersOnExit", "DisposeLocalSyncTimersOnExit", "DisposeRateLimitAutoResetTimerOnExit", "DisposeRemoteHostService", "DisposeTrayIconOnExit" })
                    {
                        typeof(MainForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    }
                }
            }
        }

        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void SubAgentWindow_150Percent_ShownScalesPanelMinimaOnce(string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            UiTheme.InitializeUiFontScale(150);
            using (var form = new SubAgentWorkbenchForm(null, new ChatSession[0], new SubAgentWorkbenchOperations()))
            {
                CreateHandles(form);
                typeof(Form).GetMethod("OnShown", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { EventArgs.Empty });
                var main = form.Controls.OfType<SplitContainer>().Single();
                var detail = main.Panel2.Controls[0].Controls.OfType<SplitContainer>().Single();
                Assert.That(main.Panel1MinSize, Is.EqualTo(UiTheme.Scale(300)));
                Assert.That(main.Panel2MinSize, Is.EqualTo(UiTheme.Scale(480)));
                Assert.That(detail.Panel1MinSize, Is.EqualTo(UiTheme.Scale(230)));
                Assert.That(detail.Panel2MinSize, Is.EqualTo(UiTheme.Scale(160)));
                Assert.That(main.Panel1MinSize + main.Panel2MinSize + main.SplitterWidth, Is.LessThanOrEqualTo(main.Width));
                Assert.That(detail.Panel1MinSize + detail.Panel2MinSize + detail.SplitterWidth, Is.LessThanOrEqualTo(detail.Height));
            }
        }

        [Test]
        public void MainMinimumAndBounds_Fit1920By1080At150Percent()
        {
            UiTheme.InitializeUiFontScale(150);
            var area = new Rectangle(0, 0, 1920, 1040);
            var minimum = MainFormLayoutPlanner.ConstrainMinimumSize(new Size(2304, 1440), area);
            var bounds = MainFormLayoutPlanner.ConstrainWindowBounds(new Rectangle(100, 100, 2400, 1600), area);
            Assert.That(area.Contains(bounds), Is.True);
            Assert.That(minimum.Width, Is.LessThanOrEqualTo(area.Width));
            Assert.That(minimum.Height, Is.LessThanOrEqualTo(area.Height));
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void Manifest_DeclaresSystemDpiAndPreservesAccessibilitySwitches()
        {
            var dir = RepositorySource.GetPath("src", "MyCodex.WinForms");
            var manifest = XDocument.Load(Path.Combine(dir, "app.manifest"));
            Assert.That(manifest.Descendants().Single(e => e.Name.LocalName == "dpiAware").Value, Is.EqualTo("true"));
            Assert.That(manifest.Descendants().Any(e => e.Name.LocalName == "dpiAwareness"), Is.False);
            Assert.That(manifest.Descendants().Single(e => e.Name.LocalName == "requestedExecutionLevel").Attribute("level").Value, Is.EqualTo("asInvoker"));
            Assert.That(File.ReadAllText(Path.Combine(dir, "MyCodex.WinForms.csproj")), Does.Contain("<ApplicationManifest>app.manifest</ApplicationManifest>"));
            var config = File.ReadAllText(Path.Combine(dir, "App.config"));
            for (var i = 2; i <= 5; i++)
            {
                Assert.That(config, Does.Contain("Switch.UseLegacyAccessibilityFeatures." + i + "=true"));
            }
        }

        private sealed class SilentReader : IScreenReaderService
        {
            public bool IsAvailable => false;
            public bool IsInitialized => false;
            public string LastError => null;
            public bool EnsureInitialized() => false;
            public bool IsReaderRunning() => false;
            public int GetReaderState() => 0;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) => false;
            public bool Stop(bool useSlave) => false;
            public void Dispose()
            {
            }
        }

        private static T Uninitialized<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }

        private static Form CreateForm(string name)
        {
            switch (name)
            {
                case "Options": return new OptionsForm(new AppConfigSnapshot(), null);
                case "Model": return new ModelSettingsForm("Model", null, null, null, "Default", "Description");
                case "Permissions": return new PermissionSettingsForm("Permissions", false, null, null, ApprovalMode.Auto, ApprovalMode.Auto);
                case "Reader": return new ScreenReaderSettingsForm(ScreenReaderProviderKind.ByCtrl, false, "MyCodex");
                case "Authentication": return new AuthenticationCenterForm(Uninitialized<AuthenticationCenterClient>());
                case "Automation": return new AutomationEditForm(new AutomationCreationContext());
                case "Plugins": return new PluginManagementForm(Uninitialized<AppServerClient>(), Uninitialized<PluginMarketCatalogClient>());
                case "Git": return new GitWorkbenchForm("test", "main", Uninitialized<GitRepositoryService>(), Uninitialized<GitHubCliService>(), null);
                default: throw new ArgumentException(name);
            }
        }

        private static void CreateHandles(Control control)
        {
            var handle = control.Handle;
            foreach (Control child in control.Controls)
            {
                CreateHandles(child);
            }
            control.PerformLayout();
        }

        private static bool LocallyVisible(Control control)
        {
            return (bool)typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { 2 });
        }

        private static void AssertLayout(Control parent, string path)
        {
            var children = parent.Controls.Cast<Control>().Where(LocallyVisible).ToArray();
            foreach (var child in children)
            {
                var label = path + "/" + child.GetType().Name + ":" + (child.AccessibleName ?? child.Text);
                if ((child is Button || child is Label || child is CheckBox) && !child.AutoSize && child.Text.Length > 0)
                {
                    Assert.That(child.Width + 1, Is.GreaterThanOrEqualTo(child.PreferredSize.Width), label + " text width");
                    Assert.That(child.Height + 1, Is.GreaterThanOrEqualTo(child.PreferredSize.Height), label + " text height");
                }
                Assert.That(child.Left, Is.GreaterThanOrEqualTo(0), label + " left");
                Assert.That(child.Top, Is.GreaterThanOrEqualTo(0), label + " top");
                var scroll = parent as ScrollableControl;
                var area = scroll != null && scroll.AutoScroll ? scroll.DisplayRectangle : parent.ClientRectangle;
                Assert.That(child.Right, Is.LessThanOrEqualTo(area.Right + 1), label + " right");
                Assert.That(child.Bottom, Is.LessThanOrEqualTo(area.Bottom + 1), label + " bottom");
                foreach (var sibling in children.Where(c => c != child && !(c is TabPage) && !(child is TabPage)))
                {
                    Assert.That(!child.Bounds.IntersectsWith(sibling.Bounds), Is.True, label + " overlaps " + sibling.AccessibleName);
                }
                AssertLayout(child, label);
            }
        }
    }
}
