using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AutomationEditFormTests
    {
        [Test]
        public void AutomationEditForm_PrefillsThreadContext()
        {
            var context = new AutomationCreationContext
            {
                Kind = AutomationKind.Thread,
                WorkspaceId = "workspace-1",
                WorkspacePath = @"E:\MyCodex",
                SessionId = "session-1",
                ThreadId = "thread-1",
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.ReadOnly
            };

            using (var form = new AutomationEditForm(context))
            {
                Assert.That(form.Text, Is.EqualTo(UiLocalization.Text("AutomationEditForm.Title.New", "New automation")));
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily review";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Review the current thread.";

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request, Is.Not.Null);
                Assert.That(form.Request.Kind, Is.EqualTo(AutomationKind.Thread));
                Assert.That(form.Request.Context.WorkspaceId, Is.EqualTo("workspace-1"));
                Assert.That(form.Request.Context.WorkspacePath, Is.EqualTo(@"E:\MyCodex"));
                Assert.That(form.Request.Context.SessionId, Is.EqualTo("session-1"));
                Assert.That(form.Request.Context.ThreadId, Is.EqualTo("thread-1"));
            }
        }

        [Test]
        public void AutomationEditForm_LocalizesKindAndScheduleChoicesInChinese()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                using (var form = new AutomationEditForm(new AutomationCreationContext
                {
                    Kind = AutomationKind.Thread,
                    WorkspacePath = @"E:\MyCodex"
                }))
                {
                    var kindComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Kind", "自动化类型"));
                    var scheduleComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ScheduleKind", "调度规则"));

                    Assert.That(kindComboBox, Is.Not.Null);
                    Assert.That(scheduleComboBox, Is.Not.Null);
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            UiLocalization.Text("Automation.Kind.Thread", "会话线程"),
                            UiLocalization.Text("Automation.Kind.Standalone", "独立自动化")
                        },
                        kindComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            UiLocalization.Text("Automation.Schedule.ManualOnly", "仅手动运行"),
                            UiLocalization.Text("Automation.Schedule.EveryMinutes", "每隔几分钟"),
                            UiLocalization.Text("Automation.Schedule.EveryHours", "每隔几小时"),
                            UiLocalization.Text("Automation.Schedule.Daily", "每天"),
                            UiLocalization.Text("Automation.Schedule.Weekly", "每周")
                        },
                        scheduleComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                    Assert.That(
                        kindComboBox.SelectedItem.ToString(),
                        Is.EqualTo(UiLocalization.Text("Automation.Kind.Thread", "会话线程")));
                    Assert.That(
                        scheduleComboBox.SelectedItem.ToString(),
                        Is.EqualTo(UiLocalization.Text("Automation.Schedule.EveryHours", "每隔几小时")));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void AutomationEditForm_ProjectContextAllowsOnlyProjectKindAndSavesProject()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                using (var form = new AutomationEditForm(new AutomationCreationContext
                {
                    Kind = AutomationKind.Project,
                    InteractionMode = AppInteractionMode.Coding,
                    RuntimeProfileId = "wsl|ubuntu",
                    WorkspaceId = "project-workspace",
                    WorkspacePath = @"E:\Project",
                    ModelId = "gpt-5.5",
                    ReasoningEffort = "high",
                    ApprovalMode = ApprovalMode.Auto
                }))
                {
                    var kindComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Kind", "Automation type"));

                    CollectionAssert.AreEqual(
                        new[] { UiLocalization.Text("Automation.Kind.Project", "项目自动化") },
                        kindComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                    AssertKindComboHasNoBareEnumText(kindComboBox);

                    FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Project review";
                    FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Review project changes.";
                    FormTestDriver.ClickOk(form);

                    Assert.That(form.Request.Kind, Is.EqualTo(AutomationKind.Project));
                    Assert.That(form.Request.Context.InteractionMode, Is.EqualTo(AppInteractionMode.Coding));
                    Assert.That(form.Request.Context.RuntimeProfileId, Is.EqualTo("wsl|ubuntu"));
                    Assert.That(form.Request.Context.WorkspaceId, Is.EqualTo("project-workspace"));
                    Assert.That(form.Request.Context.WorkspacePath, Is.EqualTo(@"E:\Project"));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void AutomationEditForm_OfficeContextAllowsOnlyOfficeKindAndSavesOffice()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                using (var form = new AutomationEditForm(new AutomationCreationContext
                {
                    Kind = AutomationKind.Office,
                    InteractionMode = AppInteractionMode.Office,
                    RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                    WorkspaceId = "office-reports",
                    WorkspacePath = @"E:\Documents\Reports",
                    OfficeRootPath = @"E:\Documents",
                    ModelId = "gpt-5.5",
                    ReasoningEffort = "medium",
                    ApprovalMode = ApprovalMode.ReadOnly
                }))
                {
                    var kindComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Kind", "Automation type"));

                    CollectionAssert.AreEqual(
                        new[] { "Office 自动化" },
                        kindComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
                    AssertKindComboHasNoBareEnumText(kindComboBox);

                    FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Office digest";
                    FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize reports.";
                    FormTestDriver.ClickOk(form);

                    Assert.That(form.Request.Kind, Is.EqualTo(AutomationKind.Office));
                    Assert.That(form.Request.Context.InteractionMode, Is.EqualTo(AppInteractionMode.Office));
                    Assert.That(form.Request.Context.RuntimeProfileId, Is.EqualTo(RuntimeProfileService.WindowsRuntimeProfileId));
                    Assert.That(form.Request.Context.WorkspaceId, Is.EqualTo("office-reports"));
                    Assert.That(form.Request.Context.WorkspacePath, Is.EqualTo(@"E:\Documents\Reports"));
                    Assert.That(form.Request.Context.OfficeRootPath, Is.EqualTo(@"E:\Documents"));
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void AutomationEditForm_StandaloneContextAllowsOnlyStandaloneKind()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                var kindComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Kind", "Automation type"));

                CollectionAssert.AreEqual(
                    new[] { AutomationDisplayText.Kind(AutomationKind.Standalone) },
                    kindComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
            }
        }

        [Test]
        public void AutomationEditForm_RunSettingsDefaultFromContextAndSaveOverrides()
        {
            var models = new[]
            {
                CreateModelOption("gpt-5.5", "high", "high", "medium"),
                CreateModelOption("gpt-5.6", "medium", "medium", "low")
            };

            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex",
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.ReadOnly
            }, null, models, modelId => true))
            {
                var modelComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model"));
                var reasoningComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort"));
                var approvalComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ApprovalMode", "Approval mode"));

                Assert.That(modelComboBox, Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model")), Is.Null);
                Assert.That(GetSelectedComboItemProperty(modelComboBox, "ModelId"), Is.EqualTo("gpt-5.5"));
                Assert.That(reasoningComboBox.SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text("ReasoningEffort.High", "High")));
                Assert.That(approvalComboBox.SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text(
                    "ApprovalMode.ReadOnlyWithDescription",
                    "Read Only - read files only; asks before changes.")));

                SelectComboItemProperty(modelComboBox, "ModelId", "gpt-5.6");
                SelectComboText(reasoningComboBox, UiLocalization.Text("ReasoningEffort.Medium", "Medium"));
                SelectComboText(approvalComboBox, UiLocalization.Text(
                    "ApprovalMode.FullAccessWithDescription",
                    "Full Access - can access files and run commands; approvals are reviewed automatically by a model."));
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.ModelId, Is.EqualTo("gpt-5.6"));
                Assert.That(form.Request.ReasoningEffort, Is.EqualTo("medium"));
                Assert.That(form.Request.ApprovalMode, Is.EqualTo(ApprovalMode.FullAccess));
            }
        }

        [Test]
        public void AutomationEditForm_ApprovalChoicesIncludeBriefDescriptions()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                var approvalComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ApprovalMode", "Approval mode"));

                CollectionAssert.AreEqual(
                    new[]
                    {
                        UiLocalization.Text(
                            "ApprovalMode.ReadOnlyWithDescription",
                            "Read Only - read files only; asks before changes."),
                        UiLocalization.Text(
                            "ApprovalMode.AutoWithDescription",
                            "Auto - can work in workspace; asks for broader access."),
                        UiLocalization.Text(
                            "ApprovalMode.FullAccessWithDescription",
                            "Full Access - can access files and run commands; approvals are reviewed automatically by a model."),
                        UiLocalization.Text(
                            "ApprovalMode.CustomWithDescription",
                            "Custom - starts read-only; selected permission categories are allowed automatically.")
                    },
                    approvalComboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray());
            }
        }

        [Test]
        public void AutomationEditForm_RunSettingsClearReasoningEffortForUnsupportedThirdPartyModel()
        {
            var models = new[]
            {
                CreateModelOption("deepseek-v4-pro", "high", "high")
            };

            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex",
                ModelId = "deepseek-v4-pro",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.Auto
            }, null, models, modelId => false))
            {
                var modelComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model"));
                var reasoningComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort"));

                Assert.That(GetSelectedComboItemProperty(modelComboBox, "ModelId"), Is.EqualTo("deepseek-v4-pro"));
                Assert.That(reasoningComboBox.Enabled, Is.False);

                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.ModelId, Is.EqualTo("deepseek-v4-pro"));
                Assert.That(form.Request.ReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void AutomationEditForm_DisablesUnreportedReasoningCapabilityWithoutCallback()
        {
            var models = new[]
            {
                new AppServerModelOption { ModelId = "official-observed", DisplayName = "Official observed" }
            };

            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }, null, models, null))
            {
                var reasoningComboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort"));
                Assert.That(reasoningComboBox.Enabled, Is.False);

                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";
                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.ModelId, Is.EqualTo("official-observed"));
                Assert.That(form.Request.ReasoningEffort, Is.Null);
            }
        }

        [Test]
        public void AutomationEditForm_UnconfiguredRunUsesFirstAppServerModelObservation()
        {
            var models = new[]
            {
                CreateModelOption("official-observed", "medium", "low", "medium", "high")
            };

            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }, null, models, modelId => true))
            {
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.ModelId, Is.EqualTo("official-observed"));
                Assert.That(form.Request.ReasoningEffort, Is.EqualTo("medium"));
                Assert.That(form.Request.ApprovalMode, Is.EqualTo(ApprovalMode.Auto));
            }
        }

        [Test]
        public void AutomationEditForm_RunSettingsHaveAccessibleNames()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model")), Is.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ApprovalMode", "Approval mode")), Is.Not.Null);
            }
        }

        [Test]
        public void AutomationEditForm_PrefillsExistingDefinitionForEdit()
        {
            var definition = new AutomationDefinitionSnapshot
            {
                Id = "automation-1",
                Name = "Daily news",
                Kind = AutomationKind.Standalone,
                Prompt = "Summarize news.",
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                ApprovalMode = ApprovalMode.FullAccess,
                AllowNoFindingsAutoArchive = true,
                Schedule = new AutomationScheduleSnapshot
                {
                    Kind = AutomationScheduleKind.Daily,
                    TimeOfDayMinutes = 8 * 60 + 30
                },
                Context = new AutomationContextSnapshot
                {
                    WorkspacePath = @"E:\MyCodex"
                },
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Skill,
                        Name = "news-digest",
                        DisplayName = "News Digest",
                        Path = @"E:\skills\news-digest"
                    }
                }
            };

            using (var form = new AutomationEditForm(definition, null, new[]
            {
                CreateModelOption("gpt-5.5", "high", "high")
            }, modelId => true))
            {
                Assert.That(form.Text, Is.EqualTo(UiLocalization.Text("AutomationEditForm.Title.Edit", "Edit automation")));
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text, Is.EqualTo("Daily news"));
                Assert.That(FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text, Is.EqualTo("Summarize news."));
                Assert.That(GetSelectedComboItemProperty(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Model", "Model")), "ModelId"), Is.EqualTo("gpt-5.5"));
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ReasoningEffort", "Reasoning effort")).SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text("ReasoningEffort.High", "High")));
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ApprovalMode", "Approval mode")).SelectedItem.ToString(), Is.EqualTo(UiLocalization.Text(
                    "ApprovalMode.FullAccessWithDescription",
                    "Full Access - can access files and run commands; approvals are reviewed automatically by a model.")));
                Assert.That(FormTestDriver.FindControlOrDefault<CheckBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.AutoArchive", "Allow no-findings runs to archive automatically")).Checked, Is.True);
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationEditForm.Accessible.InputItems", "Inserted plugins and skills")).Items, Has.Count.EqualTo(1));
                Assert.That(FormTestDriver.FindControlOrDefault<DateTimePicker>(form, UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time")).Value.Hour, Is.EqualTo(8));
                Assert.That(FormTestDriver.FindControlOrDefault<DateTimePicker>(form, UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time")).Value.Minute, Is.EqualTo(30));
            }
        }

        [Test]
        public void AutomationEditForm_InsertsAndRemovesStructuredInputItems()
        {
            var callbacks = new AutomationInputSelectionCallbacks
            {
                SelectSkillAsync = owner => Task.FromResult(new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Skill,
                    Name = "news-digest",
                    DisplayName = "News Digest",
                    Path = @"E:\skills\news-digest"
                }),
                SelectPluginAsync = owner => Task.FromResult(new AutomationInputItemSnapshot
                {
                    Type = AutomationInputItemKind.Plugin,
                    Name = "mycodex-office",
                    DisplayName = "MyCodex Office",
                    Path = @"E:\plugins\mycodex-office"
                })
            };

            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }, callbacks))
            {
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";

                ClickButton(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.InsertSkill", "Insert skill")));
                ClickButton(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.InsertPlugin", "Insert plugin")));

                var listView = FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationEditForm.Accessible.InputItems", "Inserted plugins and skills"));
                Assert.That(listView.Items, Has.Count.EqualTo(2));
                Assert.That(listView.Items[0].SubItems[0].Text, Is.EqualTo(AutomationDisplayText.InputItemKind(AutomationInputItemKind.Skill)));
                Assert.That(listView.Items[1].SubItems[0].Text, Is.EqualTo(AutomationDisplayText.InputItemKind(AutomationInputItemKind.Plugin)));

                Assert.That(listView.Handle, Is.Not.EqualTo(IntPtr.Zero));
                listView.Items[0].Selected = true;
                ClickButton(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.RemoveInput", "Remove inserted item")));

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.InputItems, Has.Count.EqualTo(1));
                Assert.That(form.Request.InputItems[0].Type, Is.EqualTo(AutomationInputItemKind.Plugin));
                Assert.That(form.Request.InputItems[0].Name, Is.EqualTo("mycodex-office"));
            }
        }

        [Test]
        public void AutomationEditForm_InputItemControlsHaveAccessibleNames()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.InsertPlugin", "Insert plugin")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.InsertSkill", "Insert skill")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<ListView>(form, UiLocalization.Text("AutomationEditForm.Accessible.InputItems", "Inserted plugins and skills")), Is.Not.Null);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, UiLocalization.Text("AutomationEditForm.Accessible.RemoveInput", "Remove inserted item")), Is.Not.Null);
            }
        }

        [Test]
        public void AutomationEditForm_EscapeClosesForm()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                form.Show();

                var handled = FormTestDriver.ProcessCmdKey(form, Keys.Escape);

                Assert.That(handled, Is.True);
                Assert.That(form.IsDisposed, Is.True);
            }
        }

        [Test]
        public void AutomationEditForm_DailyScheduleStoresTimeOfDayMinutes()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Daily news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";
                SelectScheduleKind(form, AutomationScheduleKind.Daily);
                var timePicker = FormTestDriver.FindControlOrDefault<DateTimePicker>(form, UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time"));
                timePicker.Value = new DateTime(2026, 6, 6, 8, 30, 0);

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.Schedule.Kind, Is.EqualTo(AutomationScheduleKind.Daily));
                Assert.That(form.Request.Schedule.TimeOfDayMinutes, Is.EqualTo(8 * 60 + 30));
                Assert.That(form.Request.Schedule.DayOfWeek, Is.Null);
                Assert.That(form.Request.Schedule.Interval, Is.EqualTo(0));
            }
        }

        [Test]
        public void AutomationEditForm_WeeklyScheduleStoresDayAndTime()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Weekly review";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Review changes.";
                SelectScheduleKind(form, AutomationScheduleKind.Weekly);
                SelectDayOfWeek(form, DayOfWeek.Monday);
                var timePicker = FormTestDriver.FindControlOrDefault<DateTimePicker>(form, UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time"));
                timePicker.Value = new DateTime(2026, 6, 6, 8, 0, 0);

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.Schedule.Kind, Is.EqualTo(AutomationScheduleKind.Weekly));
                Assert.That(form.Request.Schedule.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
                Assert.That(form.Request.Schedule.TimeOfDayMinutes, Is.EqualTo(8 * 60));
                Assert.That(form.Request.Schedule.Interval, Is.EqualTo(0));
            }
        }

        [Test]
        public void AutomationEditForm_EveryHoursDefaultsToOneHour()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Name", "Name")).Text = "Hourly news";
                FormTestDriver.FindControlOrDefault<TextBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.Prompt", "Prompt")).Text = "Summarize news.";

                FormTestDriver.ClickOk(form);

                Assert.That(form.Request.Schedule.Kind, Is.EqualTo(AutomationScheduleKind.EveryHours));
                Assert.That(form.Request.Schedule.Interval, Is.EqualTo(1));
                Assert.That(form.Request.Schedule.TimeOfDayMinutes, Is.Null);
                Assert.That(form.Request.Schedule.DayOfWeek, Is.Null);
            }
        }

        [Test]
        public void AutomationEditForm_ManualOnlyDisablesScheduleDetailControls()
        {
            using (var form = new AutomationEditForm(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                WorkspacePath = @"E:\MyCodex"
            }))
            {
                SelectScheduleKind(form, AutomationScheduleKind.ManualOnly);

                Assert.That(FormTestDriver.FindControlOrDefault<NumericUpDown>(form, UiLocalization.Text("AutomationEditForm.Accessible.Interval", "Interval")).Enabled, Is.False);
                Assert.That(FormTestDriver.FindControlOrDefault<DateTimePicker>(form, UiLocalization.Text("AutomationEditForm.Accessible.Time", "Time")).Enabled, Is.False);
                Assert.That(FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.DayOfWeek", "Day of week")).Enabled, Is.False);
            }
        }

        private static void SelectScheduleKind(AutomationEditForm form, AutomationScheduleKind kind)
        {
            var comboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.ScheduleKind", "Schedule"));
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i].ToString() == AutomationDisplayText.ScheduleKind(kind))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            Assert.Fail("Schedule kind was not found: " + kind);
        }

        private static void SelectDayOfWeek(AutomationEditForm form, DayOfWeek dayOfWeek)
        {
            var comboBox = FormTestDriver.FindControlOrDefault<ComboBox>(form, UiLocalization.Text("AutomationEditForm.Accessible.DayOfWeek", "Day of week"));
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i].ToString() == AutomationDisplayText.DayOfWeek(dayOfWeek))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            Assert.Fail("Day of week was not found: " + dayOfWeek);
        }

        private static void AssertKindComboHasNoBareEnumText(ComboBox comboBox)
        {
            var values = comboBox.Items.Cast<object>().Select(item => item.ToString()).ToArray();
            CollectionAssert.DoesNotContain(values, "Project");
            CollectionAssert.DoesNotContain(values, "Office");
            CollectionAssert.DoesNotContain(values, "Standalone");
            CollectionAssert.DoesNotContain(values, "Thread");
        }

        private static void ClickButton(Button button)
        {
            Assert.That(button, Is.Not.Null);
            var method = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(button, new object[] { EventArgs.Empty });
        }

        private static void SelectComboText(ComboBox comboBox, string text)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i].ToString() == text)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            Assert.Fail("Combo item was not found: " + text);
        }

        private static AppServerModelOption CreateModelOption(string modelId, string defaultReasoningEffort, params string[] efforts)
        {
            var option = new AppServerModelOption
            {
                ModelId = modelId,
                DisplayName = modelId,
                DefaultReasoningEffort = defaultReasoningEffort
            };

            foreach (var effort in efforts ?? new string[0])
            {
                option.ReasoningEfforts.Add(new AppServerModelEffortOption
                {
                    EffortId = effort,
                    Description = effort
                });
            }

            return option;
        }

        private static void SelectComboItemProperty(ComboBox comboBox, string propertyName, string value)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var itemValue = GetComboItemProperty(comboBox.Items[i], propertyName);
                if (string.Equals(itemValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            Assert.Fail("Combo item was not found: " + value);
        }

        private static string GetSelectedComboItemProperty(ComboBox comboBox, string propertyName)
        {
            return GetComboItemProperty(comboBox.SelectedItem, propertyName);
        }

        private static string GetComboItemProperty(object item, string propertyName)
        {
            if (item == null)
            {
                return null;
            }

            var property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null, propertyName + " property should exist.");
            return property.GetValue(item, null) as string;
        }
    }
}
