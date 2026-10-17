using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AppServerFileWindowManagerTests
    {
        [Test]
        public void Registry_ReusesSameBrowserKey()
        {
            var registry = new AppServerFileWindowRegistry<object>();
            var key = AppServerFileWindowKey.CreateBrowser(
                "Bundled",
                "windows",
                null,
                AppInteractionMode.Coding,
                @"E:\Repo");
            var first = new object();
            var second = new object();

            Assert.That(registry.TryAdd(key, first), Is.True);
            Assert.That(registry.TryAdd(key, second), Is.False);
            Assert.That(registry.TryGet(key), Is.SameAs(first));
        }

        [Test]
        public void Registry_UpdateKeyMovesExistingWindow()
        {
            var registry = new AppServerFileWindowRegistry<object>();
            var oldKey = AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Repo");
            var newKey = AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Repo\src");
            var value = new object();

            registry.TryAdd(oldKey, value);
            registry.UpdateKey(oldKey, newKey, value);

            Assert.That(registry.TryGet(oldKey), Is.Null);
            Assert.That(registry.TryGet(newKey), Is.SameAs(value));
        }

        [Test]
        public void Registry_UpdateKeyToExistingKeyKeepsExistingTarget()
        {
            var registry = new AppServerFileWindowRegistry<object>();
            var oldKey = AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Repo");
            var existingKey = AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Repo\src");
            var moving = new object();
            var existing = new object();

            registry.TryAdd(oldKey, moving);
            registry.TryAdd(existingKey, existing);
            var result = registry.TryUpdateKey(oldKey, existingKey, moving);

            Assert.That(result, Is.False);
            Assert.That(registry.TryGet(existingKey), Is.SameAs(existing));
        }

        [Test]
        public void Registry_SnapshotRemainsStableAfterRegistryChanges()
        {
            var registry = new AppServerFileWindowRegistry<object>();
            var key = AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Repo");
            registry.TryAdd(key, new object());

            var snapshot = registry.Snapshot();
            registry.Remove(key);

            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(registry.TryGet(key), Is.Null);
        }

        [Test]
        public void RuntimeScopesMatch_RejectsAWindowAfterTheActiveRuntimeChanges()
        {
            var openedScope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = "Bundled",
                RuntimeProfileId = "windows",
                InteractionMode = AppInteractionMode.Coding
            };
            var sameScope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = "bundled",
                RuntimeProfileId = "WINDOWS",
                InteractionMode = AppInteractionMode.Coding
            };
            var changedScope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = "Wsl",
                RuntimeProfileId = "ubuntu",
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowManager.RuntimeScopesMatch(openedScope, sameScope), Is.True);
            Assert.That(AppServerFileWindowManager.RuntimeScopesMatch(openedScope, changedScope), Is.False);
        }

        [Test]
        public async Task RestoreWatchesAsync_OnlyRestoresOpenWindowsInCurrentScope()
        {
            var client = new FakeFileSystemClient();
            using (var owner = new Form())
            using (var current = new AppServerFileBrowserForm(client, @"E:\Current", paths => Task.CompletedTask, message => { }))
            using (var other = new AppServerFileBrowserForm(client, "/other", paths => Task.CompletedTask, message => { }))
            {
                var scope = new AppServerFileWindowRuntimeScope
                {
                    RuntimeSource = "Bundled",
                    RuntimeProfileId = "windows",
                    InteractionMode = AppInteractionMode.Coding
                };
                var manager = new AppServerFileWindowManager(
                    owner,
                    client,
                    () => scope,
                    paths => Task.CompletedTask,
                    null,
                    message => { });
                var registry = (AppServerFileWindowRegistry<AppServerFileBrowserForm>)typeof(AppServerFileWindowManager)
                    .GetField("_browsers", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(manager);
                registry.TryAdd(
                    AppServerFileWindowKey.CreateBrowser("Bundled", "windows", null, AppInteractionMode.Coding, @"E:\Current"),
                    current);
                registry.TryAdd(
                    AppServerFileWindowKey.CreateBrowser("Wsl", "wsl", null, AppInteractionMode.Coding, "/other"),
                    other);

                await manager.RestoreWatchesAsync();

                CollectionAssert.AreEqual(new[] { @"E:\Current" }, client.WatchedPaths);
            }
        }

        [Test]
        public void PreviewShownHandler_DoesNotRegisterWatchAfterPreviewWasClosedDuringLoad()
        {
            var source = File.ReadAllText(GetSourcePath("AppServerFileWindowManager.cs"));
            var loadIndex = source.IndexOf("await form.LoadAsync().ConfigureAwait(true);");
            var disposedCheckIndex = source.IndexOf("if (form.IsDisposed || form.Disposing)", loadIndex);
            var watchIndex = source.IndexOf("await _watchCoordinator.WatchAsync(form).ConfigureAwait(true);", loadIndex);

            Assert.That(loadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(disposedCheckIndex, Is.GreaterThan(loadIndex));
            Assert.That(watchIndex, Is.GreaterThan(disposedCheckIndex));
            Assert.That(source, Does.Contain("return;"));
        }

        [Test]
        public void BrowserPreviewRequest_RemembersBrowserAsPreviewCloseReturnTarget()
        {
            var source = File.ReadAllText(GetSourcePath("AppServerFileWindowManager.cs"));
            var browserHandlerIndex = source.IndexOf("form.SetPreviewRequestHandlerForTests(path =>");
            Assert.That(browserHandlerIndex, Is.GreaterThanOrEqualTo(0));

            var opensFromBrowserIndex = source.IndexOf("OpenFileFromBrowser(path, form);", browserHandlerIndex);
            Assert.That(opensFromBrowserIndex, Is.GreaterThan(browserHandlerIndex));

            var previewMethodIndex = source.IndexOf("private void OpenPreview(string path, Form returnFocusTarget)");
            Assert.That(previewMethodIndex, Is.GreaterThan(opensFromBrowserIndex));

            var closeHandlerIndex = source.IndexOf("form.FormClosed += (sender, e) =>", previewMethodIndex);
            Assert.That(closeHandlerIndex, Is.GreaterThan(previewMethodIndex));

            var reactivationIndex = source.IndexOf("ReactivatePreviewReturnFocusTarget", closeHandlerIndex);
            Assert.That(reactivationIndex, Is.GreaterThan(closeHandlerIndex));
        }

        [Test]
        public void BrowserPreviewRequest_UsesTextFilePolicyAndScopedLocalPathPolicy()
        {
            var source = File.ReadAllText(GetSourcePath("AppServerFileWindowManager.cs"));
            var handlerIndex = source.IndexOf("form.SetPreviewRequestHandlerForTests(path =>");
            Assert.That(handlerIndex, Is.GreaterThanOrEqualTo(0));

            var policyIndex = source.IndexOf("TextEditableFilePolicy.ShouldOpenWithTextEditor(path)", handlerIndex);
            Assert.That(policyIndex, Is.GreaterThan(handlerIndex));

            var localPathIndex = source.IndexOf("AppServerFileWindowLocalPathPolicy.TryGetLocalWindowsFilePath(", handlerIndex);
            Assert.That(localPathIndex, Is.GreaterThan(handlerIndex));

            var systemOpenIndex = source.IndexOf("OpenWithSystemDefault(localWindowsPath, returnFocusTarget);", policyIndex);
            Assert.That(systemOpenIndex, Is.GreaterThan(policyIndex));
        }

        [Test]
        public void BrowserAndPreview_AreShownAsIndependentWindows()
        {
            var source = File.ReadAllText(GetSourcePath("AppServerFileWindowManager.cs"));

            Assert.That(source, Does.Not.Contain("form.Show(_owner);"));
            Assert.That(source, Does.Contain("form.Show();"));
        }

        private static string GetSourcePath(string fileName)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null);
            return Path.Combine(directory.FullName, "src", "MyCodex.WinForms", "Forms", fileName);
        }

        private sealed class FakeFileSystemClient : IAppServerFileSystemClient
        {
            public readonly List<string> WatchedPaths = new List<string>();

            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
            {
                WatchedPaths.Add(path);
                return Task.FromResult(new AppServerFileSystemWatchResult());
            }

            public Task UnwatchFileSystemAsync(string watchId) { return Task.CompletedTask; }
            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path) { throw new NotSupportedException(); }
            public Task WriteFileAsync(string path, byte[] data) { throw new NotSupportedException(); }
            public Task CreateDirectoryAsync(string path, bool? recursive = null) { throw new NotSupportedException(); }
            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null) { throw new NotSupportedException(); }
            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive) { throw new NotSupportedException(); }
        }
    }
}
