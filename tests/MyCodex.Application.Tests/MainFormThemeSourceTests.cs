using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class MainFormThemeSourceTests
    {
        [Test]
        public void OptionsForm_UsesThemeChoicesWithoutMenuStrip()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.cs") +
                Environment.NewLine +
                RepositorySource.ReadSourceFile("MainForm.SessionSettings.cs") +
                Environment.NewLine +
                RepositorySource.ReadSourceFile("OptionsForm.cs");

            Assert.That(source.IndexOf("\"OptionsForm.Theme.FollowSystem\"", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(source.IndexOf("\"OptionsForm.Theme.Light\"", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(source.IndexOf("\"OptionsForm.Theme.Dark\"", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(source.IndexOf("new MenuStrip", StringComparison.Ordinal), Is.LessThan(0));
        }

        [Test]
        public void MainForm_ListensForSystemThemeChange()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.cs");
            var layout = RepositorySource.ReadSourceFile("MainForm.Layout.cs");
            var handlerStart = source.IndexOf("private void OnSystemUserPreferenceChanged(", StringComparison.Ordinal);
            var handlerEnd = source.IndexOf("private void LoadAppConfig()", handlerStart, StringComparison.Ordinal);
            var handler = source.Substring(handlerStart, handlerEnd - handlerStart);

            Assert.That(layout, Does.Contain("SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged"));
            Assert.That(source, Does.Contain("SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged"));
            Assert.That(handler, Does.Contain("BeginInvokeSafe"));
            Assert.That(handler, Does.Contain("UiTheme.ResolveMode("));
            Assert.That(handler, Does.Contain("mode != UiTheme.CurrentMode"));
            Assert.That(handler, Does.Contain("ApplyConfiguredTheme()"));
            Assert.That(handler, Does.Not.Contain("UiThemePreference.Auto"));
            Assert.That(source, Does.Contain("UiTheme.ApplyToOpenForms();"));
        }
    }
}
