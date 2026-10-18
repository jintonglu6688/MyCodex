using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class OptionsFormTests
    {
        [Test]
        public void TargetPreferences_LoadAndKeepDraftAfterValidationFailure()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                MainWindowHotKeyEnabled = false,
                EnableAutomaticChatReading = true
            }, null))
            {
                var shortcut = FormTestDriver.Field<CheckBox>(form, "_mainWindowHotKeyEnabledCheckBox");
                var key = FormTestDriver.Field<TextBox>(form, "_mainWindowHotKeyTextBox");
                var reading = FormTestDriver.Field<CheckBox>(form, "_automaticChatReadingCheckBox");
                Assert.That(shortcut.Checked, Is.False);
                Assert.That(key.Enabled, Is.False);
                Assert.That(reading.Checked, Is.True);
                Assert.That(reading.AccessibleName, Is.Not.Empty);
                Assert.That(shortcut.AccessibleName, Is.Not.Empty);
                Assert.That(shortcut.TabIndex, Is.LessThan(key.TabIndex));
                shortcut.Checked = true;
                reading.Checked = false;
                form.SetMainWindowHotKeyStateText("Registration failed; still using Alt+Shift+M");
                form.SetTargetDisplayName("Ubuntu");
                form.ValidateSelection = () => false;
                FormTestDriver.ClickOk(form);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(form.MainWindowHotKeyEnabled, Is.True);
                Assert.That(form.EnableAutomaticChatReading, Is.False);
                Assert.That(key.Enabled, Is.True);
                Assert.That(FormTestDriver.Field<Label>(form, "_mainWindowHotKeyStateLabel").Text, Does.Contain("Alt+Shift+M"));
                Assert.That(FormTestDriver.Field<Label>(form, "_targetOptionsLabel").Text, Does.Contain("Ubuntu"));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
            }
        }

        [Test]
        public void OptionsForm_ContainsExpectedCategories()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var tabControl = FormTestDriver.Field<TabControl>(form, "_tabControl");

                Assert.That(tabControl.TabPages.Count, Is.EqualTo(6));
                Assert.That(tabControl.TabPages[0].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.Appearance", "Appearance")));
                Assert.That(tabControl.TabPages[1].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.Permissions", "Permissions")));
                Assert.That(tabControl.TabPages[2].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.Office", "Office")));
                Assert.That(tabControl.TabPages[3].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.Notification", "Sound")));
                Assert.That(tabControl.TabPages[4].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.ScreenReader", "Screen Reader")));
                Assert.That(tabControl.TabPages[5].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.OfficialSettings", "Official Settings")));
                Assert.That(tabControl.AccessibleName, Is.Null.Or.Empty);
                Assert.That(form.HasOfficialCodexSettings, Is.False);
            }
        }

        [Test]
        public void OptionsForm_EmbedsEditableOfficialSettingsWhenConfigIsAvailable()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), new AppServerConfigSnapshot()))
            {
                Assert.That(form.HasOfficialCodexSettings, Is.True);
                Assert.That(form.OfficialCodexSettings, Is.Not.Null);

                var description = FormTestDriver.Field<TextBox>(form.OfficialCodexSettings, "_descriptionTextBox");
                Assert.That(description.ReadOnly, Is.True);
                Assert.That(description.TabStop, Is.True);
                Assert.That(description.Parent, Is.Not.Null);
            }
        }

        [Test]
        public void OptionsForm_PermissionsTabShowsCustomApprovalChecklist()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    EnabledPermissions = new List<CustomApprovalPermissionKind>
                    {
                        CustomApprovalPermissionKind.GitInspect,
                        CustomApprovalPermissionKind.FileCreate
                    }
                }
            }, null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customPermissionsListView");

                Assert.That(list.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.CustomPermissions", "Custom permissions")));
                Assert.That(list.Items.Count, Is.EqualTo(CustomApprovalPermissionCatalog.GetOrderedPermissions().Length));
                Assert.That(list.CheckBoxes, Is.True);
                Assert.That(list.FullRowSelect, Is.True);
                Assert.That(list.UseCompatibleStateImageBehavior, Is.False);
                Assert.That(list.Items[0].Checked, Is.True);
                Assert.That(list.Items[14].Checked, Is.True);
                Assert.That(list.TabIndex, Is.EqualTo(0));
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OptionsForm_CustomPermissionsUseStandardCheckedListView()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customPermissionsListView");
                var source = RepositorySource.ReadSourceFile("OptionsForm.cs");

                Assert.That(list.CheckBoxes, Is.True);
                Assert.That(list.View, Is.EqualTo(View.Details));
                Assert.That(source, Does.Contain("_customPermissionsListView = new ListView"));
                Assert.That(source, Does.Not.Contain("CheckedListBox"));
                Assert.That(source, Does.Not.Contain("NotifyWinEvent("));
                Assert.That(source, Does.Not.Contain("protected override void OnKeyDown"));
            }
        }

        [Test]
        public void OptionsForm_AppliesCustomApprovalSettings()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customPermissionsListView");
                list.Items[1].Checked = true;
                list.Items[15].Checked = true;

                FormTestDriver.ClickOk(form);

                Assert.That(form.CustomApprovalSettings.EnabledPermissions, Is.EqualTo(new[]
                {
                    CustomApprovalPermissionKind.GitStage,
                    CustomApprovalPermissionKind.FileModify
                }));
            }
        }

        [Test]
        public void OptionsForm_PermissionsTabShowsCustomCommandPrefixes()
        {
            var snapshot = new AppConfigSnapshot
            {
                CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    CommandPrefixes = new List<string>
                    {
                        "git status",
                        "dotnet test"
                    }
                }
            };

            using (var form = new OptionsForm(snapshot, null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customCommandPrefixesListView");

                Assert.That(list.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.CustomCommandPrefixes", "Custom command prefixes")));
                Assert.That(list.View, Is.EqualTo(View.Details));
                Assert.That(list.CheckBoxes, Is.False);
                Assert.That(list.Items.Cast<ListViewItem>().Select(item => item.Text).ToArray(), Is.EqualTo(new[]
                {
                    "git status",
                    "dotnet test"
                }));
            }
        }

        [Test]
        public void OptionsForm_AppliesCustomCommandPrefixes()
        {
            var snapshot = new AppConfigSnapshot
            {
                CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    CommandPrefixes = new List<string> { "git status" }
                }
            };

            using (var form = new OptionsForm(snapshot, null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customCommandPrefixesListView");
                list.Items.Add(new ListViewItem("dotnet test"));

                FormTestDriver.ClickOk(form);

                Assert.That(form.CustomApprovalSettings.CommandPrefixes, Is.EqualTo(new[]
                {
                    "git status",
                    "dotnet test"
                }));
            }
        }

        [Test]
        public void MainForm_CustomApprovalSettingsComparisonTreatsCommandPrefixOrderAsChanged()
        {
            var left = new CustomApprovalSettingsSnapshot
            {
                EnabledPermissions = new List<CustomApprovalPermissionKind>
                {
                    CustomApprovalPermissionKind.GitInspect,
                    CustomApprovalPermissionKind.FileCreate
                },
                CommandPrefixes = new List<string>
                {
                    "git status",
                    "dotnet test"
                }
            };
            var right = new CustomApprovalSettingsSnapshot
            {
                EnabledPermissions = new List<CustomApprovalPermissionKind>
                {
                    CustomApprovalPermissionKind.FileCreate,
                    CustomApprovalPermissionKind.GitInspect
                },
                CommandPrefixes = new List<string>
                {
                    "dotnet test",
                    "git status"
                }
            };

            Assert.That(InvokeAreCustomApprovalSettingsEqual(left, right), Is.False);
        }

        [Test]
        public void OptionsForm_CustomCommandPrefixControlsExposeKeyboardActions()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var list = FormTestDriver.Field<ListView>(form, "_customCommandPrefixesListView");
                var add = FormTestDriver.Field<Button>(form, "_addCustomCommandPrefixButton");
                var edit = FormTestDriver.Field<Button>(form, "_editCustomCommandPrefixButton");
                var delete = FormTestDriver.Field<Button>(form, "_deleteCustomCommandPrefixButton");

                Assert.That(list.MultiSelect, Is.False);
                Assert.That(add.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.AddCustomCommandPrefix", "Add custom command prefix")));
                Assert.That(edit.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.EditCustomCommandPrefix", "Edit custom command prefix")));
                Assert.That(delete.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.DeleteCustomCommandPrefix", "Delete custom command prefix")));
                Assert.That(add.Text, Does.Contain("(&A)"));
                Assert.That(edit.Text, Does.Contain("(&E)"));
                Assert.That(delete.Text, Does.Contain("(&D)"));
            }
        }

        [Test]
        public void OptionsForm_AppliesLocalSettings()
        {
            var snapshot = new AppConfigSnapshot
            {
                UiThemePreference = UiThemePreference.Auto,
                UiLanguagePreference = UiLanguagePreference.Auto,
                TurnCompletedNotificationMode = NotificationPromptMode.Sound,
                SubAgentTurnCompletedNotificationMode = NotificationPromptMode.Off,
                ApprovalRequestNotificationMode = NotificationPromptMode.Sound,
                SendMessageSoundEnabled = true,
                CloseSessionWorkbenchOnEscape = false,
                ActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Steer,
                ScreenReaderProvider = ScreenReaderProviderKind.ByCtrl,
                ManualBrowseUseReaderMainChannel = true,
                ScreenReaderIndependentChannelName = "Main",
                OfficeShowActivityPane = true,
                MessageDetailPreviewMode = MessageDetailPreviewMode.Web,
                SendMessageShortcutMode = SendMessageShortcutMode.EnterNewLineCtrlEnterSend
            };

            using (var form = new OptionsForm(snapshot, null))
            {
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_themeComboBox"), UiThemePreference.Dark);
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_languageComboBox"), UiLanguagePreference.EnUs);
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_messageDetailPreviewModeComboBox"), MessageDetailPreviewMode.Text);
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_sendMessageShortcutModeComboBox"), SendMessageShortcutMode.CtrlEnterNewLineEnterSend);
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_startupModeComboBox"), AppInteractionMode.Office.ToString());
                FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox").Text = "  C:\\Office  ";
                FormTestDriver.Field<CheckBox>(form, "_officeShowActivityPaneCheckBox").Checked = false;
                var closeSessionWorkbenchOnEscapeCheckBox = FormTestDriver.Field<CheckBox>(form, "_closeSessionWorkbenchOnEscapeCheckBox");
                Assert.That(closeSessionWorkbenchOnEscapeCheckBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.CloseSessionWorkbenchOnEscape", "Allow Esc to close the main and independent windows")));
                Assert.That(closeSessionWorkbenchOnEscapeCheckBox.Checked, Is.False);
                closeSessionWorkbenchOnEscapeCheckBox.Checked = true;
                var activeTurnFollowUpModeComboBox = FormTestDriver.Field<ComboBox>(form, "_activeTurnFollowUpModeComboBox");
                Assert.That(activeTurnFollowUpModeComboBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.ActiveTurnFollowUpMode", "Follow-up messages while working")));
                Assert.That(GetSelectedChoice(activeTurnFollowUpModeComboBox), Is.EqualTo(ActiveTurnFollowUpMode.Steer));
                Assert.That(activeTurnFollowUpModeComboBox.TabIndex, Is.EqualTo(4));
                SelectChoice(activeTurnFollowUpModeComboBox, ActiveTurnFollowUpMode.Queue);
                FormTestDriver.ClickOk(form);

                Assert.That(form.UiThemePreference, Is.EqualTo(UiThemePreference.Dark));
                Assert.That(form.UiLanguagePreference, Is.EqualTo(UiLanguagePreference.EnUs));
                Assert.That(form.MessageDetailPreviewMode, Is.EqualTo(MessageDetailPreviewMode.Text));
                Assert.That(form.SendMessageShortcutMode, Is.EqualTo(SendMessageShortcutMode.CtrlEnterNewLineEnterSend));
                Assert.That(form.DefaultStartupMode, Is.EqualTo(AppInteractionMode.Office));
                Assert.That(form.OfficeRootPath, Is.EqualTo(@"C:\Office"));
                Assert.That(form.OfficeShowActivityPane, Is.False);
                Assert.That(form.TurnCompletedNotificationMode, Is.EqualTo(NotificationPromptMode.Sound));
                Assert.That(form.SubAgentTurnCompletedNotificationMode, Is.EqualTo(NotificationPromptMode.Off));
                Assert.That(form.ApprovalRequestNotificationMode, Is.EqualTo(NotificationPromptMode.Sound));
                Assert.That(form.SendMessageSoundEnabled, Is.True);
                Assert.That(form.CloseSessionWorkbenchOnEscape, Is.True);
                Assert.That(form.ActiveTurnFollowUpMode, Is.EqualTo(ActiveTurnFollowUpMode.Queue));
                Assert.That(form.ScreenReaderProvider, Is.EqualTo(ScreenReaderProviderKind.ByCtrl));
                Assert.That(form.ManualBrowseUseReaderMainChannel, Is.True);
                Assert.That(form.ScreenReaderIndependentChannelName, Is.EqualTo("Main"));
            }
        }

        [Test]
        public void Constructor_DoesNotExposeOfficialOfficeCapabilityOption()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                Assert.That(typeof(OptionsForm).GetField("_officialOfficeCapabilityEnabledCheckBox", BindingFlags.NonPublic | BindingFlags.Instance), Is.Null);
                Assert.That(typeof(OptionsForm).GetProperty("OfficialOfficeCapabilityEnabled", BindingFlags.Public | BindingFlags.Instance), Is.Null);
                Assert.That(FindText(form, "Enable MyCodex Office capability"), Is.False);
                Assert.That(FindText(form, "启用 MyCodex 官方办公能力"), Is.False);
            }
        }

        [Test]
        public void OptionsForm_ShowsResolvedOfficeDirectoryWhenConfigIsEmpty()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var officeRootPathTextBox = FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox");
                var expectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrWhiteSpace(expectedPath))
                {
                    expectedPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                Assert.That(officeRootPathTextBox.Text, Is.EqualTo(Path.GetFullPath(Path.Combine(expectedPath, "MyCodex-office"))));
            }
        }

        [Test]
        public void Constructor_OfficeDirectoryControlsDescribeActiveRuntimeAndRespectTabOrder()
        {
            var context = new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = "Ubuntu 24.04",
                HostPlatform = AppServerHostPlatform.Wsl,
                ConfiguredRootPath = "/srv/mycodex-office",
                EffectiveRootPath = "/srv/mycodex-office"
            };

            using (var form = new OptionsForm(new AppConfigSnapshot(), null, null, null, context))
            {
                var runtime = FormTestDriver.Field<Label>(form, "_officeRuntimeDisplayNameLabel");
                var path = FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox");
                var browse = FormTestDriver.Field<Button>(form, "_officeRootBrowseButton");
                var restoreDefault = FormTestDriver.Field<Button>(form, "_officeRootRestoreDefaultButton");
                var activity = FormTestDriver.Field<CheckBox>(form, "_officeShowActivityPaneCheckBox");

                Assert.That(runtime.Text, Is.EqualTo("Ubuntu 24.04"));
                Assert.That(runtime.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeRuntimeValue", "Current runtime")));
                Assert.That(runtime.TabStop, Is.False);
                Assert.That(path.Text, Is.EqualTo("/srv/mycodex-office"));
                Assert.That(path.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeRootPath", "Office directory")));
                Assert.That(browse.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.BrowseOfficeRootPath", "Browse office directory")));
                Assert.That(restoreDefault.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.RestoreDefaultOfficeRootPath", "Restore default office directory")));
                Assert.That(path.TabIndex, Is.EqualTo(1));
                Assert.That(browse.TabIndex, Is.EqualTo(2));
                Assert.That(restoreDefault.TabIndex, Is.EqualTo(3));
                Assert.That(activity.TabIndex, Is.EqualTo(4));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(820));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(560));
            }
        }

        [Test]
        public void OfficeDirectoryBrowse_UsesRuntimeDelegateAndSelectedPath()
        {
            IWin32Window browseOwner = null;
            string browseInitialPath = null;
            var context = new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = "Ubuntu",
                HostPlatform = AppServerHostPlatform.Wsl,
                ConfiguredRootPath = "/srv/office",
                EffectiveRootPath = "/srv/office",
                BrowseDirectory = (owner, initialPath) =>
                {
                    browseOwner = owner;
                    browseInitialPath = initialPath;
                    return "/opt/work";
                }
            };

            using (var form = new OptionsForm(new AppConfigSnapshot(), null, null, null, context))
            {
                InvokePrivateEventHandler(form, "OfficeRootBrowseButton_Click");
                FormTestDriver.ClickOk(form);

                Assert.That(browseOwner, Is.SameAs(form));
                Assert.That(browseInitialPath, Is.EqualTo("/srv/office"));
                Assert.That(form.OfficeRootPath, Is.EqualTo("/opt/work"));
            }
        }

        [Test]
        public void OfficeDirectory_DefaultLeftUntouchedRemainsNull()
        {
            var context = new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = "Ubuntu",
                HostPlatform = AppServerHostPlatform.Wsl,
                EffectiveRootPath = "/home/alice/MyCodex-office"
            };

            using (var form = new OptionsForm(new AppConfigSnapshot(), null, null, null, context))
            {
                Assert.That(FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox").Text,
                    Is.EqualTo("/home/alice/MyCodex-office"));

                FormTestDriver.ClickOk(form);

                Assert.That(form.OfficeRootPath, Is.Null);
            }
        }

        [Test]
        public void OfficeDirectory_RestoreDefaultClearsConfiguredOverride()
        {
            var context = new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = "Ubuntu",
                HostPlatform = AppServerHostPlatform.Wsl,
                ConfiguredRootPath = "/srv/office",
                EffectiveRootPath = "/srv/office"
            };

            using (var form = new OptionsForm(new AppConfigSnapshot(), null, null, null, context))
            {
                InvokePrivateEventHandler(form, "OfficeRootRestoreDefaultButton_Click");

                Assert.That(FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox").Text,
                    Is.EqualTo("~/MyCodex-office"));
                Assert.That(FormTestDriver.Field<Label>(form, "_officeRootStatusLabel").Text,
                    Is.EqualTo(UiLocalization.Text(
                        "OptionsForm.OfficeRootPath.DefaultUnavailable",
                        "The effective office directory is unavailable until the runtime home directory can be resolved.")));

                FormTestDriver.ClickOk(form);

                Assert.That(form.OfficeRootPath, Is.Null);
            }
        }

        [Test]
        public void OfficeDirectory_ChangingResolvedHomeExpressionRequiresNewResolution()
        {
            var context = new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = "Ubuntu",
                HostPlatform = AppServerHostPlatform.Wsl,
                ConfiguredRootPath = "$HOME/work",
                EffectiveRootPath = "/home/alice/work"
            };

            using (var form = new OptionsForm(new AppConfigSnapshot(), null, null, null, context))
            {
                var path = FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox");
                var status = FormTestDriver.Field<Label>(form, "_officeRootStatusLabel");
                Assert.That(status.Text, Is.Empty);

                path.Text = "$HOME/other";

                Assert.That(status.Text, Is.EqualTo(UiLocalization.Text(
                    "OptionsForm.OfficeRootPath.DefaultUnavailable",
                    "The effective office directory is unavailable until the runtime home directory can be resolved.")));
            }
        }

        [Test]
        public void OfficeDirectory_UnresolvedUnixDefaultUsesUnixPlaceholderAndStatus()
        {
            using (var form = new OptionsForm(
                new AppConfigSnapshot(),
                null,
                null,
                null,
                new OfficeDirectoryOptionsContext
                {
                    RuntimeDisplayName = "Remote Linux",
                    HostPlatform = AppServerHostPlatform.RemoteMac
                }))
            {
                var path = FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox");
                var status = FormTestDriver.Field<Label>(form, "_officeRootStatusLabel");
                var windowsDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                Assert.That(path.Text, Is.EqualTo("~/MyCodex-office"));
                Assert.That(path.Text, Is.Not.EqualTo(windowsDocuments));
                Assert.That(status.Text, Is.EqualTo(UiLocalization.Text(
                    "OptionsForm.OfficeRootPath.DefaultUnavailable",
                    "The effective office directory is unavailable until the runtime home directory can be resolved.")));
                Assert.That(status.AccessibleName, Is.EqualTo(UiLocalization.Text(
                    "OptionsForm.Accessible.OfficeRootPathStatus",
                    "Office directory status")));
                Assert.That(status.TabStop, Is.False);
            }
        }

        [Test]
        public void OfficeDirectory_UnresolvedSavedHomeExpressionShowsUnavailableStatus()
        {
            using (var form = new OptionsForm(
                new AppConfigSnapshot(),
                null,
                null,
                null,
                new OfficeDirectoryOptionsContext
                {
                    RuntimeDisplayName = "Ubuntu",
                    HostPlatform = AppServerHostPlatform.Wsl,
                    ConfiguredRootPath = "$HOME/work"
                }))
            {
                Assert.That(FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox").Text,
                    Is.EqualTo("$HOME/work"));
                Assert.That(FormTestDriver.Field<Label>(form, "_officeRootStatusLabel").Text,
                    Is.EqualTo(UiLocalization.Text(
                        "OptionsForm.OfficeRootPath.DefaultUnavailable",
                        "The effective office directory is unavailable until the runtime home directory can be resolved.")));
            }
        }

        [Test]
        public void ModeSettingsUnavailable_DisablesOnlyRuntimeScopedControlsAndShowsStatus()
        {
            var unavailableText = "Remote mode settings are unavailable.";
            using (var form = new OptionsForm(
                new AppConfigSnapshot(),
                null,
                null,
                null,
                new OfficeDirectoryOptionsContext
                {
                    RuntimeDisplayName = "Remote Linux",
                    HostPlatform = AppServerHostPlatform.RemoteMac,
                    ConfiguredRootPath = "/srv/office",
                    EffectiveRootPath = "/srv/office",
                    ModeSettingsAvailable = false,
                    ModeSettingsError = unavailableText
                },
                new ModeSettingsSnapshot
                {
                    DefaultStartupMode = AppInteractionMode.Office,
                    OfficeRootPath = "/srv/office"
                }))
            {
                Assert.That(FormTestDriver.Field<ComboBox>(form, "_startupModeComboBox").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_officeRootPathTextBox").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_officeRootBrowseButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Button>(form, "_officeRootRestoreDefaultButton").Enabled, Is.False);
                Assert.That(FormTestDriver.Field<Label>(form, "_officeRootStatusLabel").Text, Is.EqualTo(unavailableText));
                Assert.That(FormTestDriver.Field<CheckBox>(form, "_officeShowActivityPaneCheckBox").Enabled, Is.True);
                Assert.That(FormTestDriver.Field<ComboBox>(form, "_themeComboBox").Enabled, Is.True);
            }
        }

        [TestCase(@"C:\Office\..\Work", @"C:\Work")]
        public void OfficeDirectoryValidation_WindowsAcceptsNormalizedLocalAbsolutePath(
            string value,
            string expected)
        {
            string normalized;

            var valid = OptionsForm.TryNormalizeOfficeRootPath(
                AppServerHostPlatform.Windows,
                value,
                out normalized);

            Assert.That(valid, Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase(@"Office\Work")]
        [TestCase(@"\Office\Work")]
        [TestCase(@"\\server\share")]
        [TestCase("/home/alice/office")]
        public void OfficeDirectoryValidation_WindowsRejectsNonLocalOrRelativePath(string value)
        {
            string normalized;

            Assert.That(OptionsForm.TryNormalizeOfficeRootPath(
                AppServerHostPlatform.Windows,
                value,
                out normalized), Is.False);
            Assert.That(normalized, Is.Null);
        }

        [TestCase("/home/alice/office", "/home/alice/office")]
        [TestCase("/home/alice/../office", "/home/office")]
        [TestCase("~/MyCodex-office", "~/MyCodex-office")]
        [TestCase("$HOME/work", "$HOME/work")]
        public void OfficeDirectoryValidation_UnixAcceptsAbsolutePathOrHomeExpression(
            string value,
            string expected)
        {
            string normalized;

            var valid = OptionsForm.TryNormalizeOfficeRootPath(
                AppServerHostPlatform.Wsl,
                value,
                out normalized);

            Assert.That(valid, Is.True);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("office/work")]
        [TestCase("./office")]
        [TestCase(@"C:\Office")]
        public void OfficeDirectoryValidation_UnixRejectsRelativeOrWindowsPath(string value)
        {
            string normalized;

            Assert.That(OptionsForm.TryNormalizeOfficeRootPath(
                AppServerHostPlatform.RemoteMac,
                value,
                out normalized), Is.False);
            Assert.That(normalized, Is.Null);
        }

        [Test]
        public void Constructor_LoadsMessageDetailPreviewModeOnAppearancePage()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                MessageDetailPreviewMode = MessageDetailPreviewMode.Text
            }, null))
            {
                var comboBox = FormTestDriver.Field<ComboBox>(form, "_messageDetailPreviewModeComboBox");

                Assert.That(comboBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.MessageDetailPreviewMode", "Message detail preview mode")));
                Assert.That(GetSelectedChoice(comboBox), Is.EqualTo(MessageDetailPreviewMode.Text));
            }
        }

        [TestCase(SessionListDisplayOrder.AgentsTimeTitle)]
        [TestCase(SessionListDisplayOrder.TitleTimeAgents)]
        [TestCase(SessionListDisplayOrder.TitleAgentsTime)]
        public void SessionListDisplayOrder_LoadsAccessibleChoiceAndAppliesOnlyOnConfirmation(string order)
        {
            var config = new AppConfigSnapshot { SessionListDisplayOrder = order };
            using (var form = new OptionsForm(config, null))
            {
                var comboBox = FormTestDriver.Field<ComboBox>(form, "_sessionListDisplayOrderComboBox");
                Assert.That(comboBox.AccessibleName, Is.EqualTo(
                    UiLocalization.Text("OptionsForm.Accessible.SessionListDisplayOrder", "Session information order")));
                Assert.That(comboBox.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                Assert.That(comboBox.TabIndex, Is.EqualTo(10));
                Assert.That(comboBox.Items.Count, Is.EqualTo(3));
                Assert.That(GetSelectedChoice(comboBox), Is.EqualTo(order));
                var root = (TableLayoutPanel)comboBox.Parent;
                Assert.That(root.GetControlFromPosition(0, 10).TabStop, Is.False);
                form.Size = form.MinimumSize;
                form.Show();
                Assert.That(comboBox.Bottom + comboBox.Margin.Bottom, Is.LessThanOrEqualTo(root.ClientSize.Height - root.Padding.Bottom));

                comboBox.SelectedIndex = (comboBox.SelectedIndex + 1) % comboBox.Items.Count;
                var nextOrder = GetSelectedChoice(comboBox);
                Assert.That(config.SessionListDisplayOrder, Is.EqualTo(order));
                FormTestDriver.ClickOk(form);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(form.SessionListDisplayOrder, Is.EqualTo(nextOrder));
            }
        }

        [Test]
        public void Constructor_LoadsSendMessageShortcutModeOnAppearancePage()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                SendMessageShortcutMode = SendMessageShortcutMode.CtrlEnterNewLineEnterSend
            }, null))
            {
                var comboBox = FormTestDriver.Field<ComboBox>(form, "_sendMessageShortcutModeComboBox");

                Assert.That(comboBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.SendMessageShortcutMode", "Send message shortcut")));
                Assert.That(GetSelectedChoice(comboBox), Is.EqualTo(SendMessageShortcutMode.CtrlEnterNewLineEnterSend));
            }
        }

        [Test]
        public void Constructor_LoadsActiveTurnFollowUpModeOnAppearancePage()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                ActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Queue
            }, null))
            {
                var comboBox = FormTestDriver.Field<ComboBox>(form, "_activeTurnFollowUpModeComboBox");

                Assert.That(comboBox.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                Assert.That(comboBox.AccessibleName, Is.EqualTo(
                    UiLocalization.Text("OptionsForm.Accessible.ActiveTurnFollowUpMode", "Follow-up messages while working")));
                Assert.That(comboBox.TabIndex, Is.EqualTo(4));
                Assert.That(GetSelectedChoice(comboBox), Is.EqualTo(ActiveTurnFollowUpMode.Queue));
                Assert.That(comboBox.Items.Count, Is.EqualTo(2));
                Assert.That(comboBox.Items[0].ToString(), Is.EqualTo(
                    UiLocalization.Text("OptionsForm.ActiveTurnFollowUpMode.Steer", "Steer")));
                Assert.That(comboBox.Items[1].ToString(), Is.EqualTo(
                    UiLocalization.Text("OptionsForm.ActiveTurnFollowUpMode.Queue", "Queue")));
            }
        }

        [Test]
        public void Constructor_LoadsAccessibleMainWindowGlobalShortcut()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                MainWindowHotKeyModifiers =
                    MainWindowHotKeyConfig.ModifierControl |
                    MainWindowHotKeyConfig.ModifierShift,
                MainWindowHotKeyVirtualKey = (int)Keys.F9
            }, null))
            {
                var textBox = FormTestDriver.Field<TextBox>(form, "_mainWindowHotKeyTextBox");

                Assert.That(textBox.ReadOnly, Is.True);
                Assert.That(textBox.ShortcutsEnabled, Is.False);
                Assert.That(textBox.TabIndex, Is.EqualTo(1));
                Assert.That(textBox.AccessibleName, Is.EqualTo(
                    UiLocalization.Text("OptionsForm.Accessible.MainWindowHotKey", "Main window global shortcut")));
                Assert.That(textBox.AccessibleDescription, Is.Null.Or.Empty);
                Assert.That(textBox.Text, Is.EqualTo("Ctrl+Shift+F9"));
                Assert.That(form.MainWindowHotKeyModifiers, Is.EqualTo(
                    MainWindowHotKeyConfig.ModifierControl |
                    MainWindowHotKeyConfig.ModifierShift));
                Assert.That(form.MainWindowHotKeyVirtualKey, Is.EqualTo((int)Keys.F9));
            }
        }

        [Test]
        public void Constructor_LoadsTextFileOpenSettingsOnAppearancePage()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot
            {
                TextFileOpenMode = TextFileOpenMode.VsCode,
                TextFileExternalEditorPath = @"C:\Tools\code.cmd",
                TextFileExternalEditorArguments = "-g {file}:{line}:{column}"
            }, null))
            {
                var comboBox = FormTestDriver.Field<ComboBox>(form, "_textFileOpenModeComboBox");
                var pathTextBox = FormTestDriver.Field<TextBox>(form, "_textFileExternalEditorPathTextBox");
                var argumentsTextBox = FormTestDriver.Field<TextBox>(form, "_textFileExternalEditorArgumentsTextBox");

                Assert.That(comboBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.TextFileOpenMode", "Text file opener")));
                Assert.That(GetSelectedChoice(comboBox), Is.EqualTo(TextFileOpenMode.VsCode));
                Assert.That(pathTextBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorPath", "External editor path")));
                Assert.That(argumentsTextBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorArguments", "External editor arguments")));
                Assert.That(pathTextBox.Text, Is.EqualTo(@"C:\Tools\code.cmd"));
                Assert.That(argumentsTextBox.Text, Is.EqualTo("-g {file}:{line}:{column}"));
            }
        }

        [Test]
        public void OptionsForm_AppliesTextFileOpenSettings()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                SelectChoice(FormTestDriver.Field<ComboBox>(form, "_textFileOpenModeComboBox"), TextFileOpenMode.Custom);
                FormTestDriver.Field<TextBox>(form, "_textFileExternalEditorPathTextBox").Text = "  C:\\Tools\\editor.exe  ";
                FormTestDriver.Field<TextBox>(form, "_textFileExternalEditorArgumentsTextBox").Text = "  --open {file}  ";

                FormTestDriver.ClickOk(form);

                Assert.That(form.TextFileOpenMode, Is.EqualTo(TextFileOpenMode.Custom));
                Assert.That(form.TextFileExternalEditorPath, Is.EqualTo(@"C:\Tools\editor.exe"));
                Assert.That(form.TextFileExternalEditorArguments, Is.EqualTo("--open {file}"));
            }
        }

        [Test]
        public void Constructor_OfficePageIncludesAccessibleOfficeCliControls()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var statusTextBox = FormTestDriver.Field<TextBox>(form, "_officeCliStatusTextBox");
                var refreshButton = FormTestDriver.Field<Button>(form, "_officeCliRefreshButton");
                var installButton = FormTestDriver.Field<Button>(form, "_officeCliInstallButton");
                var uninstallButton = FormTestDriver.Field<Button>(form, "_officeCliUninstallButton");

                Assert.That(statusTextBox.ReadOnly, Is.True);
                Assert.That(form.OfficeCliEnvironmentChanged, Is.False);
                Assert.That(statusTextBox.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeCliStatus", "OfficeCLI status")));
                Assert.That(refreshButton.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeCliRefresh", "Refresh OfficeCLI status")));
                Assert.That(installButton.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeCliInstall", "Install or update OfficeCLI")));
                Assert.That(uninstallButton.AccessibleName, Is.EqualTo(UiLocalization.Text("OptionsForm.Accessible.OfficeCliUninstall", "Uninstall OfficeCLI")));
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OptionsForm_OfficeCliAsyncCallbacksGuardDisposedForm()
        {
            var source = RepositorySource.ReadSourceFile("OptionsForm.cs");

            Assert.That(source, Does.Contain("private bool CanUpdateOfficeCliUi()"));
            Assert.That(source, Does.Contain("private void ShowOfficeCliMessage("));
            Assert.That(source, Does.Not.Contain("MessageBox.Show(\r\n                    this,\r\n                    result.Message"));
            Assert.That(source, Does.Not.Contain("MessageBox.Show(\r\n                    this,\r\n                    ex.Message"));
        }

        [Test]
        public void NotificationSettingsForm_MapsLegacySpeechModeToSoundSwitch()
        {
            using (var form = new NotificationSettingsForm(
                NotificationPromptMode.Speech,
                NotificationPromptMode.Off,
                false,
                false))
            {
                Assert.That(form.ApplySelection(null), Is.True);

                Assert.That(form.TurnCompletedMode, Is.EqualTo(NotificationPromptMode.Sound));
                Assert.That(form.SubAgentTurnCompletedMode, Is.EqualTo(NotificationPromptMode.Sound));
                Assert.That(form.ApprovalRequestMode, Is.EqualTo(NotificationPromptMode.Off));
                Assert.That(form.SendMessageSoundEnabled, Is.False);
            }
        }

        [Test]
        public void NotificationSettingsForm_ExposesSubAgentCompletionSoundSwitch()
        {
            using (var form = new NotificationSettingsForm(
                NotificationPromptMode.Sound,
                NotificationPromptMode.Sound,
                true,
                false))
            {
                var checkBox = FormTestDriver.FindControlOrDefault<CheckBox>(form, UiLocalization.Text(
                    "NotificationSettingsForm.Accessible.SubAgentTurnCompletedSound",
                    "Play sound when a subagent completes"));

                Assert.That(checkBox, Is.Not.Null);
                Assert.That(checkBox.TabIndex, Is.EqualTo(1));
            }
        }

        [Test]
        public void NotificationSettingsForm_DoesNotExposeTestSystemNotificationButton()
        {
            using (var form = new NotificationSettingsForm(
                NotificationPromptMode.Sound,
                NotificationPromptMode.Sound,
                true,
                false))
            {
                Assert.That(FindText(form, "Send test system notification(&T)"), Is.False);
                Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Send test system notification"), Is.Null);
            }
        }

        [Test]
        public void OptionsForm_DoesNotExposeTestSystemNotificationButton()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                var button = FormTestDriver.FindControlOrDefault<Button>(form, "Send test system notification");

                Assert.That(button, Is.Null);
                Assert.That(FindText(form, "Send test system notification(&T)"), Is.False);
            }
        }

        [Test]
        public void ActivitySettingsForm_AppliesActivityVisibilityAndClearHistorySwitches()
        {
            var snapshot = new AppConfigSnapshot
            {
                ShowActivityThinkingMessages = true,
                ShowActivityPlanMessages = true,
                ShowActivityToolMessages = true,
                ShowActivityCommandMessages = true,
                ShowActivityFileMessages = true,
                ShowActivityNoticeMessages = true,
                ShowActivityErrorMessages = true,
                ShowActivityApprovalMessages = true,
                ShowActivityAccountMessages = true,
                ShowActivityProtocolNoticeMessages = true,
                ShowActivityProtocolWarningMessages = true,
                ClearActivityOnNewTurn = false
            };

            using (var form = new ActivitySettingsForm(snapshot))
            {
                FormTestDriver.Field<CheckBox>(form, "_toolCheckBox").Checked = false;
                FormTestDriver.Field<CheckBox>(form, "_commandCheckBox").Checked = false;
                FormTestDriver.Field<CheckBox>(form, "_approvalCheckBox").Checked = false;
                FormTestDriver.Field<CheckBox>(form, "_protocolWarningCheckBox").Checked = false;
                FormTestDriver.Field<CheckBox>(form, "_clearOnNewTurnCheckBox").Checked = true;
                FormTestDriver.ClickOk(form);

                Assert.That(form.ShowToolMessages, Is.False);
                Assert.That(form.ShowCommandMessages, Is.False);
                Assert.That(form.ShowApprovalMessages, Is.False);
                Assert.That(form.ShowProtocolWarningMessages, Is.False);
                Assert.That(form.ClearOnNewTurn, Is.True);
                Assert.That(form.ShowThinkingMessages, Is.True);
                Assert.That(form.ShowPlanMessages, Is.True);
                Assert.That(form.ShowFileMessages, Is.True);
                Assert.That(form.ShowNoticeMessages, Is.True);
                Assert.That(form.ShowErrorMessages, Is.True);
                Assert.That(form.ShowAccountMessages, Is.True);
                Assert.That(form.ShowProtocolNoticeMessages, Is.True);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void MainForm_SettingsMenuUsesOptionsEntry()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs");

            Assert.That(source, Does.Contain("_settingsMenuItem.MenuItems.Add(_optionsMenuItem);"));
            Assert.That(source, Does.Not.Contain("_settingsMenuItem.MenuItems.Add(_notificationSettingsMenuItem);"));
            Assert.That(source, Does.Not.Contain("_settingsMenuItem.MenuItems.Add(_screenReaderSettingsMenuItem);"));
            Assert.That(source, Does.Not.Contain("_settingsMenuItem.MenuItems.Add(_officialCodexSettingsMenuItem);"));
        }

        private static void SelectChoice(ComboBox comboBox, string value)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i];
                var property = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null);
                if (string.Equals((string)property.GetValue(item, null), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            Assert.Fail("Choice not found: " + value);
        }

        private static string GetSelectedChoice(ComboBox comboBox)
        {
            var item = comboBox.SelectedItem;
            Assert.That(item, Is.Not.Null);
            var property = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            return (string)property.GetValue(item, null);
        }

        private static string GetChoiceValue(object item)
        {
            var property = item.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(property, Is.Not.Null);
            return property.GetValue(item, null) as string;
        }

        private static void InvokePrivateEventHandler(OptionsForm form, string methodName)
        {
            var method = typeof(OptionsForm).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, new object[] { null, EventArgs.Empty });
        }

        private static bool FindText(Control root, string text)
        {
            if (root == null)
            {
                return false;
            }

            if (string.Equals(root.Text, text, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (Control child in root.Controls)
            {
                if (FindText(child, text))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InvokeAreCustomApprovalSettingsEqual(
            CustomApprovalSettingsSnapshot left,
            CustomApprovalSettingsSnapshot right)
        {
            var method = typeof(MainForm).GetMethod("AreCustomApprovalSettingsEqual", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(null, new object[] { left, right });
        }
    }
}
