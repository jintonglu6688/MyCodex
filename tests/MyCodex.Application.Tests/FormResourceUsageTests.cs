using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class FormResourceUsageTests
    {
        [Test]
        public void ChineseResources_DoNotContainEncodingReplacementMarkers()
        {
            var document = XDocument.Load(GetRepositoryPath("src", "MyCodex.WinForms", "Resources", "Strings.zh-CN.resx"));
            var corruptedKeys = document.Root.Elements("data")
                .Where(element => ((string)element.Element("value") ?? string.Empty).Contains("??")
                    || ((string)element.Element("value") ?? string.Empty).Contains("\uFFFD"))
                .Select(element => (string)element.Attribute("name"))
                .ToArray();

            Assert.That(corruptedKeys, Is.Empty, "Chinese resources contain encoding replacement markers.");
        }

        [Test]
        public void WinFormsStringResources_AreReferencedByCodeLiterals()
        {
            var resourceKeys = LoadResourceKeys();
            var sourceTexts = LoadSourceTexts();

            var unusedKeys = resourceKeys
                .Where(key => !sourceTexts.Any(text => text.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            Assert.That(unusedKeys, Is.Empty, "存在未被代码字符串字面量引用的 WinForms 资源键: " + string.Join(", ", unusedKeys));
        }

        [Test]
        public void CustomApprovalWinFormsResources_ArePresentInEnglishAndChineseResources()
        {
            var requiredKeys = new[]
            {
                "ApprovalMode.Custom",
                "ApprovalMode.CustomWithDescription",
                "PermissionSettingsForm.OptionDescription.Custom",
                "OptionsForm.Category.Permissions",
                "OptionsForm.Label.CustomPermissions",
                "OptionsForm.Accessible.CustomPermissions",
                "OptionsForm.Description.CustomPermissions",
                "OptionsForm.Label.CustomCommandPrefixes",
                "OptionsForm.Accessible.CustomCommandPrefixes",
                "OptionsForm.Button.AddCustomCommandPrefix",
                "OptionsForm.Accessible.AddCustomCommandPrefix",
                "OptionsForm.Button.EditCustomCommandPrefix",
                "OptionsForm.Accessible.EditCustomCommandPrefix",
                "OptionsForm.Button.DeleteCustomCommandPrefix",
                "OptionsForm.Accessible.DeleteCustomCommandPrefix",
                "CustomCommandPrefixEditForm.Title",
                "CustomCommandPrefixEditForm.Label.Prefix",
                "CustomCommandPrefixEditForm.Accessible.Prefix",
                "CustomApprovalPermission.GitInspect",
                "CustomApprovalPermission.GitStage",
                "CustomApprovalPermission.GitLocalCommit",
                "CustomApprovalPermission.GitInitialize",
                "CustomApprovalPermission.GitConfigurationChange",
                "CustomApprovalPermission.GitWorkingTreeChange",
                "CustomApprovalPermission.GitRemoteRead",
                "CustomApprovalPermission.GitRemoteWrite",
                "CustomApprovalPermission.GitDestructive",
                "CustomApprovalPermission.CommandBuild",
                "CustomApprovalPermission.CommandTest",
                "CustomApprovalPermission.CommandDependencyInstall",
                "CustomApprovalPermission.CommandGeneric",
                "CustomApprovalPermission.FileRead",
                "CustomApprovalPermission.FileCreate",
                "CustomApprovalPermission.FileModify",
                "CustomApprovalPermission.FileDelete",
                "CustomApprovalPermission.FileMove",
                "CustomApprovalPermission.FileMixed",
                "CustomApprovalPermission.FileUnknown",
                "CustomApprovalPermission.PermissionExtraReadRoots",
                "CustomApprovalPermission.PermissionExtraWriteRoots",
                "CustomApprovalPermission.PermissionNetwork",
                "CustomApprovalPermission.PermissionMixed",
                "CustomApprovalPermission.RequestUnknown"
            };

            AssertRequiredKeysPresent("Strings.resx", requiredKeys);
            AssertRequiredKeysPresent("Strings.zh-CN.resx", requiredKeys);
        }

        [Test]
        public void RuntimeAwareOfficeResources_ArePresentInEnglishAndChineseResources()
        {
            var requiredKeys = new[]
            {
                "MainForm.ModeTab.Coding",
                "MainForm.ModeTab.Office",
                "MainForm.Accessible.CodingMode",
                "MainForm.Accessible.OfficeMode",
                "MainForm.Message.ConfirmExit.Active",
                "MainForm.SessionSettings.Message.ConfirmRuntimeTargetChange",
                "MainForm.SessionSettings.Status.RuntimeTargetChangedActivityStopped",
                "MainForm.SessionSettings.Status.RuntimeRestartedActivityStopped",
                "RuntimeFileBrowserForm.Title.OfficeWorkspace",
                "MainForm.File.OpenOfficeDirectory",
                "MainForm.Status.OfficeDirectoryChangedFilesNotMoved",
                "OptionsForm.Label.OfficeRuntime",
                "OptionsForm.Accessible.OfficeRuntimeValue",
                "OptionsForm.Button.RestoreDefaultOfficeRootPath",
                "OptionsForm.Accessible.RestoreDefaultOfficeRootPath",
                "OptionsForm.Accessible.OfficeRootPathStatus",
                "OptionsForm.ModeSettings.Unavailable",
                "OptionsForm.ModeSettings.SaveFailed.Template",
                "OptionsForm.OfficeRootPath.DefaultUnavailable",
                "OptionsForm.OfficeRootPath.Validation.WindowsAbsoluteRequired",
                "OptionsForm.OfficeRootPath.Validation.UnixAbsoluteRequired"
            };

            AssertRequiredKeysPresent("Strings.resx", requiredKeys);
            AssertRequiredKeysPresent("Strings.zh-CN.resx", requiredKeys);
        }

        private static void AssertRequiredKeysPresent(string fileName, string[] requiredKeys)
        {
            var resourceKeys = LoadResourceKeys(fileName);
            var missingKeys = requiredKeys
                .Where(key => !resourceKeys.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missingKeys, Is.Empty, fileName + " 缺少 Custom approval 资源键: " + string.Join(", ", missingKeys));
        }

        private static string[] LoadResourceKeys()
        {
            return LoadResourceKeys("Strings.zh-CN.resx");
        }

        private static string[] LoadResourceKeys(string fileName)
        {
            var path = GetRepositoryPath("src", "MyCodex.WinForms", "Resources", fileName);
            var document = XDocument.Load(path);
            return document.Root
                .Elements("data")
                .Select(element => (string)element.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }

        private static string[] LoadSourceTexts()
        {
            var repositoryRoot = GetRepositoryPath();
            return Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Select(File.ReadAllText)
                .ToArray();
        }

        private static string GetRepositoryPath(params string[] parts)
        {
            var path = Path.Combine(new[]
            {
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                ".."
            }.Concat(parts ?? Array.Empty<string>()).ToArray());

            return Path.GetFullPath(path);
        }
    }
}
