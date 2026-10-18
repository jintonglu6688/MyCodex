using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AppServerFileBrowserFormTests
    {
        [Test]
        public void Title_UsesCurrentDirectoryNameAndFileBrowserLabel()
        {
            var client = new FakeAppServerFilesClient();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Projects\BoyRemote",
                paths => Task.CompletedTask,
                message => { }))
            {
                Assert.That(
                    form.Text,
                    Is.EqualTo("BoyRemote - " + UiLocalization.Text("AppServerFileBrowserForm.Title", "File Browser")));
                Assert.That(form.Text, Does.Not.Contain("App Server"));
            }
        }

        [Test]
        public void Constructor_CreatesIndependentAltTabWindow()
        {
            var client = new FakeAppServerFilesClient();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                Assert.That(form.ShowInTaskbar, Is.True);
                Assert.That(form.StartPosition, Is.EqualTo(FormStartPosition.CenterScreen));
            }
        }

        [Test]
        public async Task LoadInitialPathAsync_BindsDirectoryEntriesAndFocusesFirstItem()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "AGENTS.md", IsFile = true },
                new AppServerFileSystemEntry { FileName = "src", IsDirectory = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();

                Assert.That(form.ItemCountForTests, Is.EqualTo(2));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("src"));
                Assert.That(form.CurrentPathForTests, Is.EqualTo(@"E:\Repo"));
                Assert.That(
                    GetItemListView(form).Items.Cast<ListViewItem>().All(item => item.SubItems.Count == 3),
                    Is.True);
            }
        }

        [Test]
        public void Constructor_CreatesExplorerDetailColumnsInOrder()
        {
            using (var form = AppServerFileBrowserForm.CreateForTests(
                new FakeAppServerFilesClient(),
                @"E:\Repo",
                new AppServerFileSystemEntry[0]))
            {
                CollectionAssert.AreEqual(
                    new[]
                    {
                        UiLocalization.Text("AppServerFileBrowserForm.Column.Name", "Name"),
                        UiLocalization.Text("AppServerFileBrowserForm.Column.Modified", "Date modified"),
                        UiLocalization.Text("AppServerFileBrowserForm.Column.Type", "Type")
                    },
                    GetItemListView(form).Columns.Cast<ColumnHeader>().Select(column => column.Text));

                var listView = GetItemListView(form);
                Assert.That(listView.View, Is.EqualTo(View.Details));
                Assert.That(listView.FullRowSelect, Is.True);
                Assert.That(listView.HideSelection, Is.False);
                Assert.That(listView.MultiSelect, Is.True);
            }
        }

        [Test]
        public async Task LoadInitialPathAsync_ShowsPlaceholderThenUpdatesModifiedTime()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };
            var metadata = new TaskCompletionSource<AppServerFileSystemMetadata>();
            client.PendingMetadata[@"E:\Repo\notes.md"] = metadata;

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                Assert.That(GetItemListView(form).Items[0].SubItems[1].Text, Is.EqualTo("—"));

                const long modifiedAtMs = 1784189350000;
                metadata.SetResult(new AppServerFileSystemMetadata { ModifiedAtMs = modifiedAtMs });
                await form.MetadataLoadForTestsAsync;

                Assert.That(
                    GetItemListView(form).Items[0].SubItems[1].Text,
                    Is.EqualTo(AppServerFileBrowserMetadataLoader.FormatModifiedTime(modifiedAtMs)));
            }
        }

        [Test]
        public async Task MetadataLoad_FromPreviousDirectoryDoesNotUpdateCurrentRows()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\One"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "same.txt", IsFile = true }
            };
            client.Directories[@"E:\Two"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "same.txt", IsFile = true }
            };
            var oldMetadata = new TaskCompletionSource<AppServerFileSystemMetadata>();
            client.PendingMetadata[@"E:\One\same.txt"] = oldMetadata;
            client.Metadata[@"E:\Two\same.txt"] = new AppServerFileSystemMetadata { ModifiedAtMs = 2 };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\One",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.LoadPathForTestsAsync(@"E:\Two");
                await form.MetadataLoadForTestsAsync;
                oldMetadata.SetResult(new AppServerFileSystemMetadata { ModifiedAtMs = 1 });
                await Task.Yield();

                Assert.That(
                    GetItemListView(form).Items[0].SubItems[1].Text,
                    Is.EqualTo(AppServerFileBrowserMetadataLoader.FormatModifiedTime(2)));
            }
        }

        [Test]
        public async Task LoadUnixRoot_FromEmptyRootRaisesCurrentPathChanged()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories["/"] = new List<AppServerFileSystemEntry>();
            var changedPaths = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                string.Empty,
                paths => Task.CompletedTask,
                message => { }))
            {
                form.CurrentPathChanged += (sender, e) => changedPaths.Add(e.CurrentPath);

                await form.LoadPathForTestsAsync("/");

                CollectionAssert.AreEqual(new[] { "/" }, changedPaths);
            }
        }

        [Test]
        public async Task LeftRightNavigation_AnnouncesOnlyTheNewActiveColumnValue()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "docs", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };
            var announcements = new List<string>();
            const long modifiedAtMs = 1784189350000;
            client.Metadata[@"E:\Repo\docs"] = new AppServerFileSystemMetadata { ModifiedAtMs = modifiedAtMs };
            client.Metadata[@"E:\Repo\notes.md"] = new AppServerFileSystemMetadata { ModifiedAtMs = modifiedAtMs };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                announcements.Add))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.MetadataLoadForTestsAsync;
                var listView = GetItemListView(form);
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("docs"));
                var directoryReadCount = client.ReadDirectories.Count;
                var metadataReadCount = client.MetadataRequests.Count;

                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Left));
                Assert.That(announcements, Is.Empty);

                var right = new KeyEventArgs(Keys.Right);
                InvokeListViewKeyDown(form, listView, right);
                Assert.That(right.Handled, Is.True);
                Assert.That(announcements, Is.EqualTo(new[]
                {
                    AppServerFileBrowserMetadataLoader.FormatModifiedTime(modifiedAtMs)
                }));

                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Right));
                Assert.That(announcements.Last(), Is.EqualTo(
                    UiLocalization.Text("AppServerFileBrowserForm.EntryType.Folder", "Folder")));
                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Left));
                Assert.That(announcements.Last(), Is.EqualTo(
                    AppServerFileBrowserMetadataLoader.FormatModifiedTime(modifiedAtMs)));
                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Right));
                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Right));
                var countAtLastColumn = announcements.Count;
                InvokeListViewKeyDown(form, listView, new KeyEventArgs(Keys.Right));
                Assert.That(announcements, Has.Count.EqualTo(countAtLastColumn));
                Assert.That(client.ReadDirectories, Has.Count.EqualTo(directoryReadCount));
                Assert.That(client.MetadataRequests, Has.Count.EqualTo(metadataReadCount));
            }
        }

        [Test]
        public async Task NativeSelectionKeys_AreNotInterceptedOrAnnounced()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "docs", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };
            var announcements = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                announcements.Add))
            {
                await form.LoadCurrentPathForTestsAsync();
                var listView = GetItemListView(form);
                var directoryReadCount = client.ReadDirectories.Count;
                var metadataReadCount = client.MetadataRequests.Count;
                var keys = new[]
                {
                    Keys.Up,
                    Keys.Down,
                    Keys.Shift | Keys.Up,
                    Keys.Shift | Keys.Down,
                    Keys.Shift | Keys.Left,
                    Keys.Control | Keys.Up,
                    Keys.Control | Keys.Down,
                    Keys.Control | Keys.Right,
                    Keys.Enter,
                    Keys.Space
                };

                foreach (var key in keys)
                {
                    var args = new KeyEventArgs(key);
                    InvokeListViewKeyDown(form, listView, args);
                    Assert.That(args.Handled, Is.False, key.ToString());
                    Assert.That(args.SuppressKeyPress, Is.False, key.ToString());
                }

                Assert.That(listView.MultiSelect, Is.True);
                Assert.That(announcements, Is.Empty);
                Assert.That(client.ReadDirectories, Has.Count.EqualTo(directoryReadCount));
                Assert.That(client.MetadataRequests, Has.Count.EqualTo(metadataReadCount));
            }
        }

        [Test]
        public async Task LoadInitialPathAsync_WithEmptyPathBindsWindowsDriveRoot()
        {
            var client = new FakeAppServerFilesClient();

            using (var form = AppServerFileBrowserForm.CreateForTests(
                client,
                string.Empty,
                new[]
                {
                    new AppServerFileSystemEntry { FileName = @"C:\", IsDirectory = true },
                    new AppServerFileSystemEntry { FileName = @"D:\", IsDirectory = true }
                }))
            {
                await form.LoadCurrentPathForTestsAsync();

                Assert.That(form.ItemCountForTests, Is.EqualTo(2));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo(@"C:\"));
                Assert.That(form.CurrentPathForTests, Is.EqualTo(string.Empty));
                Assert.That(
                    form.Text,
                    Is.EqualTo(UiLocalization.Text("AppServerFileBrowserForm.Address.WindowsRoot", "This PC") + " - " +
                               UiLocalization.Text("AppServerFileBrowserForm.Title", "File Browser")));
                Assert.That(client.ReadDirectories, Is.Empty);
            }
        }

        [Test]
        public async Task LoadInitialPathAsync_SortsFoldersBeforeFilesUsingNaturalNameOrder()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "file10.txt", IsFile = true },
                new AppServerFileSystemEntry { FileName = "folder10", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "file2.txt", IsFile = true },
                new AppServerFileSystemEntry { FileName = "folder2", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "file1.txt", IsFile = true },
                new AppServerFileSystemEntry { FileName = "folder1", IsDirectory = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();

                CollectionAssert.AreEqual(
                    new[] { "folder1", "folder2", "folder10", "file1.txt", "file2.txt", "file10.txt" },
                    GetItemNames(form));
            }
        }

        [Test]
        public async Task LoadInitialPathAsync_HidesWindowsSystemFoldersAtDriveRoot()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"C:\"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "$Recycle.Bin", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "Users", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "System Volume Information", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "readme.txt", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"C:\",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();

                CollectionAssert.AreEqual(
                    new[] { "Users", "readme.txt" },
                    GetItemNames(form));
            }
        }

        [Test]
        public void Layout_KeepsActionCommandsOutOfTabOrder()
        {
            var client = new FakeAppServerFilesClient();

            using (var form = AppServerFileBrowserForm.CreateForTests(
                client,
                @"E:\Repo",
                new AppServerFileSystemEntry[0]))
            {
                Assert.That(form.NavigationToolStripTabStopForTests, Is.False);
                Assert.That(form.SelectionTabStopForTests, Is.False);
            }
        }

        [Test]
        public async Task SearchCurrentPath_UsesCurrentPathAndBindsResults()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "src", IsDirectory = true }
            };
            client.SearchResponse = new AppServerFuzzyFileSearchSessionEvent
            {
                Files = new List<AppServerFuzzyFileSearchResult>
                {
                    new AppServerFuzzyFileSearchResult
                    {
                        FileName = "Program.cs",
                        MatchType = "file",
                        Path = @"E:\Repo\src\Program.cs"
                    },
                    new AppServerFuzzyFileSearchResult
                    {
                        FileName = "Tests",
                        MatchType = "directory",
                        Path = @"E:\Repo\Tests"
                    }
                }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.SearchForTestsAsync("cs");

                Assert.That(client.SearchRequests.Single().Query, Is.EqualTo("cs"));
                Assert.That(client.SearchRequests.Single().Roots.Single(), Is.EqualTo(@"E:\Repo"));
                CollectionAssert.AreEqual(new[] { "Tests", "Program.cs" }, GetItemNames(form));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("Tests"));
                Assert.That(form.CurrentPathForTests, Is.EqualTo(@"E:\Repo"));
            }
        }

        [Test]
        public async Task SearchCurrentPath_WithWindowsRootDoesNotCallSearchClient()
        {
            var client = new FakeAppServerFilesClient();

            using (var form = AppServerFileBrowserForm.CreateForTests(
                client,
                string.Empty,
                new[]
                {
                    new AppServerFileSystemEntry { FileName = @"C:\", IsDirectory = true }
                }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.SearchForTestsAsync("cs");

                Assert.That(client.SearchRequests, Is.Empty);
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo(@"C:\"));
            }
        }

        [Test]
        public async Task ActivateFocusedDirectory_LoadsChildPath()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "src", IsDirectory = true }
            };
            client.Directories[@"E:\Repo\src"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "Program.cs", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.ActivateFocusedItemForTestsAsync();

                Assert.That(form.CurrentPathForTests, Is.EqualTo(@"E:\Repo\src"));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("Program.cs"));
                Assert.That(
                    form.Text,
                    Is.EqualTo("src - " + UiLocalization.Text("AppServerFileBrowserForm.Title", "File Browser")));
            }
        }

        [Test]
        public async Task CopySelectedPath_ReturnsFullPathText()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "AGENTS.md", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();

                Assert.That(
                    GetItemListView(form).Items[0].SubItems.Cast<ListViewItem.ListViewSubItem>().Select(item => item.Text),
                    Does.Not.Contain(@"E:\Repo\AGENTS.md"));
                Assert.That(form.CopySelectedPathsForTests(), Is.EqualTo(@"E:\Repo\AGENTS.md"));
            }
        }

        [Test]
        public async Task CopySelectedRuntimeFiles_RemembersSelectedPathsForAttachmentPaste()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories["/home/demo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "AGENTS.md", IsFile = true }
            };
            IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths = null;
            string clipboardMarker = null;

            using (var form = new AppServerFileBrowserForm(
                client,
                "/home/demo",
                paths => Task.CompletedTask,
                (paths, text) =>
                {
                    copiedPaths = paths;
                    clipboardMarker = text;
                },
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();

                Assert.That(form.CopySelectedFilesForTests(), Is.EqualTo("/home/demo/AGENTS.md"));
                Assert.That(copiedPaths.Single().Path, Is.EqualTo("/home/demo/AGENTS.md"));
                Assert.That(copiedPaths.Single().IsDirectory, Is.False);
                Assert.That(clipboardMarker, Is.Not.Null.And.Not.Empty);
                Assert.That(clipboardMarker, Is.Not.EqualTo("/home/demo/AGENTS.md"));
            }
        }

        [Test]
        public void BuildCopyFileDataObject_ForWindowsFilesUsesFileDropListWithoutText()
        {
            var dataObject = AppServerFileBrowserForm.BuildCopyFileDataObjectForTests(new[]
            {
                new RuntimeFileBrowserSelectedPath(@"E:\Repo\AGENTS.md", false)
            });

            Assert.That(dataObject.GetDataPresent(DataFormats.FileDrop), Is.True);
            Assert.That(dataObject.GetDataPresent(AppServerFileBrowserForm.ClipboardFormat), Is.True);
            Assert.That(dataObject.GetData(AppServerFileBrowserForm.ClipboardFormat), Is.TypeOf<string>());
            Assert.That(dataObject.GetDataPresent(DataFormats.UnicodeText), Is.False);
        }

        [Test]
        public void BuildCopyFileDataObject_ForRuntimePathsUsesAppServerBrowserMarkerWithoutText()
        {
            var dataObject = AppServerFileBrowserForm.BuildCopyFileDataObjectForTests(new[]
            {
                new RuntimeFileBrowserSelectedPath("/home/demo/AGENTS.md", false)
            });

            Assert.That(dataObject.GetDataPresent(AppServerFileBrowserForm.ClipboardFormat), Is.True);
            Assert.That(dataObject.GetData(AppServerFileBrowserForm.ClipboardFormat), Is.TypeOf<string>());
            Assert.That(dataObject.GetDataPresent(DataFormats.UnicodeText), Is.False);
        }

        [Test]
        public void ClipboardState_RequiresSameRuntimeScopeAndCurrentMarker()
        {
            var sourceScope = CreateScope(CodexRuntimeSources.RemoteUnixSsh, "runtime-1", "remote-1", AppInteractionMode.Coding);
            var state = new AppServerFileBrowserClipboardState(
                sourceScope,
                new[] { new RuntimeFileBrowserSelectedPath("/source/a.txt", false) },
                "/source/a.txt");
            var data = new DataObject();
            data.SetData(AppServerFileBrowserForm.ClipboardFormat, "/source/a.txt");

            Assert.That(state.Matches(CreateScope(CodexRuntimeSources.RemoteUnixSsh, "runtime-1", "remote-1", AppInteractionMode.Coding), data), Is.True);
            Assert.That(state.Matches(CreateScope(CodexRuntimeSources.Wsl, "runtime-1", "remote-1", AppInteractionMode.Coding), data), Is.False);
            Assert.That(state.Matches(CreateScope(CodexRuntimeSources.RemoteUnixSsh, "runtime-2", "remote-1", AppInteractionMode.Coding), data), Is.False);
            Assert.That(state.Matches(CreateScope(CodexRuntimeSources.RemoteUnixSsh, "runtime-1", "remote-2", AppInteractionMode.Coding), data), Is.False);
            Assert.That(state.Matches(CreateScope(CodexRuntimeSources.RemoteUnixSsh, "runtime-1", "remote-1", AppInteractionMode.Office), data), Is.False);

            data.SetData(AppServerFileBrowserForm.ClipboardFormat, "/new/value.txt");
            Assert.That(state.Matches(sourceScope, data), Is.False);
        }

        [Test]
        public async Task PasteFiles_CrossWindowUsesOnlyClipboardStateFromTheSameScope()
        {
            var sourceScope = CreateScope(CodexRuntimeSources.Wsl, "wsl-1", null, AppInteractionMode.Coding);
            var otherScope = CreateScope(CodexRuntimeSources.Wsl, "wsl-2", null, AppInteractionMode.Coding);
            var state = new AppServerFileBrowserClipboardState(
                sourceScope,
                new[] { new RuntimeFileBrowserSelectedPath("/source/a.txt", false) },
                "/source/a.txt");
            var data = new DataObject();
            data.SetData(AppServerFileBrowserForm.ClipboardFormat, "/source/a.txt");
            var client = new FakeAppServerFilesClient();
            client.Directories["/same"] = new List<AppServerFileSystemEntry>();
            client.Directories["/other"] = new List<AppServerFileSystemEntry>();

            using (var sameScopeWindow = AppServerFileBrowserForm.CreateForTests(client, "/same", null))
            using (var otherScopeWindow = AppServerFileBrowserForm.CreateForTests(client, "/other", null))
            {
                sameScopeWindow.SetPastePathsProvider(
                    () => state.Matches(sourceScope, data) ? state.Paths : Array.Empty<RuntimeFileBrowserSelectedPath>(),
                    false);
                otherScopeWindow.SetPastePathsProvider(
                    () => state.Matches(otherScope, data) ? state.Paths : Array.Empty<RuntimeFileBrowserSelectedPath>(),
                    false);
                await sameScopeWindow.LoadCurrentPathForTestsAsync();
                await otherScopeWindow.LoadCurrentPathForTestsAsync();

                await sameScopeWindow.PasteFilesForTestsAsync();
                await otherScopeWindow.PasteFilesForTestsAsync();

                Assert.That(client.CopiedPaths.Select(item => item.DestinationPath), Is.EqualTo(new[] { "/same/a.txt" }));
                Assert.That(GetStatusText(otherScopeWindow), Is.EqualTo(UiLocalization.Text(
                    "AppServerFileBrowserForm.Status.NothingToPaste",
                    "The clipboard does not contain files that can be pasted here.")));
            }
        }

        [Test]
        public async Task PasteFiles_CopiesFilesAndDirectoriesRecursivelyThenFocusesFirstSuccess()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories["/target"] = new List<AppServerFileSystemEntry>();
            var announcements = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                "/target",
                paths => Task.CompletedTask,
                announcements.Add))
            {
                form.SetPastePathsProvider(
                    () => new[]
                    {
                        new RuntimeFileBrowserSelectedPath("/source/a.txt", false),
                        new RuntimeFileBrowserSelectedPath("/source/folder", true)
                    },
                    false);
                await form.LoadCurrentPathForTestsAsync();
                await form.PasteFilesForTestsAsync();

                Assert.That(client.CopiedPaths.Select(item => item.DestinationPath),
                    Is.EqualTo(new[] { "/target/a.txt", "/target/folder" }));
                Assert.That(client.CopiedPaths.Select(item => item.Recursive), Is.EqualTo(new[] { false, true }));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("a.txt"));
                Assert.That(announcements, Is.Empty);
            }
        }

        [Test]
        public async Task PasteFiles_SkipsConflictsAndUnsafeDirectoryTargetsWithoutOverwriting()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories["/source/sub"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "existing.txt", IsFile = true }
            };

            using (var form = AppServerFileBrowserForm.CreateForTests(client, "/source/sub", null))
            {
                form.SetPastePathsProvider(
                    () => new[]
                    {
                        new RuntimeFileBrowserSelectedPath("/elsewhere/existing.txt", false),
                        new RuntimeFileBrowserSelectedPath("/source", true),
                        new RuntimeFileBrowserSelectedPath("/source/sub/new.txt", false)
                    },
                    false);
                await form.LoadCurrentPathForTestsAsync();
                await form.PasteFilesForTestsAsync();

                Assert.That(client.CopiedPaths, Is.Empty);
                Assert.That(GetStatusText(form), Is.EqualTo(GetPasteFailure(3, false)));
            }
        }

        [Test]
        public async Task PasteFiles_ContinuesAfterFailureAndReportsPartialResult()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Target"] = new List<AppServerFileSystemEntry>();
            client.CopyFailures.Add(@"E:\Source\bad.txt");

            using (var form = AppServerFileBrowserForm.CreateForTests(client, @"E:\Target", null))
            {
                form.SetPastePathsProvider(
                    () => new[]
                    {
                        new RuntimeFileBrowserSelectedPath(@"E:\Source\good.txt", false),
                        new RuntimeFileBrowserSelectedPath(@"E:\Source\bad.txt", false)
                    },
                    false);
                await form.LoadCurrentPathForTestsAsync();
                await form.PasteFilesForTestsAsync();

                Assert.That(client.CopiedPaths.Count, Is.EqualTo(2));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("good.txt"));
                Assert.That(GetStatusText(form), Is.EqualTo(GetPasteFailure(1, true)));
            }
        }

        [Test]
        public async Task PasteFiles_UsesWindowsInsensitiveAndUnixSensitiveNameComparison()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Target"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "A.txt", IsFile = true }
            };
            client.Directories["/target"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "A.txt", IsFile = true }
            };

            using (var windowsForm = AppServerFileBrowserForm.CreateForTests(client, @"E:\Target", null))
            using (var unixForm = AppServerFileBrowserForm.CreateForTests(client, "/target", null))
            {
                windowsForm.SetPastePathsProvider(
                    () => new[] { new RuntimeFileBrowserSelectedPath(@"E:\Source\a.txt", false) },
                    false);
                unixForm.SetPastePathsProvider(
                    () => new[] { new RuntimeFileBrowserSelectedPath("/source/a.txt", false) },
                    false);
                await windowsForm.LoadCurrentPathForTestsAsync();
                await unixForm.LoadCurrentPathForTestsAsync();

                await windowsForm.PasteFilesForTestsAsync();
                await unixForm.PasteFilesForTestsAsync();

                Assert.That(client.CopiedPaths.Select(item => item.DestinationPath), Is.EqualTo(new[] { "/target/a.txt" }));
            }
        }

        [Test]
        public async Task PasteMenuAndControlV_UseTheSameAvailablePasteState()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories["/target"] = new List<AppServerFileSystemEntry>();

            using (var form = AppServerFileBrowserForm.CreateForTests(client, "/target", null))
            {
                form.SetPastePathsProvider(
                    () => new[] { new RuntimeFileBrowserSelectedPath("/source/a.txt", false) },
                    false);
                await form.LoadCurrentPathForTestsAsync();
                Assert.That(form.PasteMenuEnabledForTests, Is.True);

                var args = new KeyEventArgs(Keys.Control | Keys.V);
                InvokeListViewKeyDown(form, GetItemListView(form), args);
                Assert.That(args.Handled, Is.True);
                Assert.That(args.SuppressKeyPress, Is.True);
            }
        }

        [Test]
        public async Task PasteMenu_IsDisabledInSearchMode()
        {
            var client = new FakeAppServerFilesClient
            {
                SearchResponse = new AppServerFuzzyFileSearchSessionEvent()
            };
            client.Directories["/target"] = new List<AppServerFileSystemEntry>();

            using (var form = AppServerFileBrowserForm.CreateForTests(client, "/target", null))
            {
                form.SetPastePathsProvider(
                    () => new[] { new RuntimeFileBrowserSelectedPath("/source/a.txt", false) },
                    false);
                await form.LoadCurrentPathForTestsAsync();
                await form.SearchForTestsAsync("a");

                Assert.That(form.PasteMenuEnabledForTests, Is.False);
            }
        }

        [Test]
        public void WindowsFileDropPaths_AcceptsExistingFilesAndDirectories()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "a.txt");
            File.WriteAllText(file, "a");
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { file, root, Path.Combine(root, "missing.txt") });
                var paths = AppServerFileBrowserForm.GetWindowsFileDropPathsForTests(data);

                Assert.That(paths.Select(item => item.Path), Is.EqualTo(new[] { file, root }));
                Assert.That(paths.Select(item => item.IsDirectory), Is.EqualTo(new[] { false, true }));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Category(SourceGuardCategory.Retired)]
        [Test]
        public void Commands_DoNotExposeInsertPath()
        {
            var source = RepositorySource.ReadSourceFile("AppServerFileBrowserForm.cs");

            Assert.That(source, Does.Not.Contain("InsertPath"));
            Assert.That(source, Does.Not.Contain("InsertSelectedPaths"));
        }

        [Test]
        public async Task AttachSelectedPath_SendsSelectedFilePath()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "AGENTS.md", IsFile = true }
            };
            var attached = new List<RuntimeFileBrowserSelectedPath>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths =>
                {
                    attached.AddRange(paths);
                    return Task.CompletedTask;
                },
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.AttachSelectedPathsForTestsAsync();

                Assert.That(attached.Single().Path, Is.EqualTo(@"E:\Repo\AGENTS.md"));
                Assert.That(attached.Single().IsDirectory, Is.False);
            }
        }

        [Test]
        public async Task CreateFolder_CallsCreateDirectoryAndFocusesCreatedItem()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.CreateFolderForTestsAsync("docs");

                Assert.That(client.CreatedDirectories.Single(), Is.EqualTo(@"E:\Repo\docs"));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("docs"));
            }
        }

        [Test]
        public async Task CreateFile_WritesEmptyFileAndFocusesCreatedItem()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.CreateFileForTestsAsync("notes.md");

                Assert.That(client.WrittenFiles.Single().Path, Is.EqualTo(@"E:\Repo\notes.md"));
                Assert.That(client.WrittenFiles.Single().Data, Is.Empty);
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("notes.md"));
            }
        }

        [Test]
        public async Task DeleteSelected_RemovesSelectedPathAndFocusesFallbackItem()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "first.txt", IsFile = true },
                new AppServerFileSystemEntry { FileName = "second.txt", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                null,
                message => { },
                paths => true))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.DeleteSelectedForTestsAsync();

                Assert.That(client.RemovedPaths.Single(), Is.EqualTo(@"E:\Repo\first.txt"));
                Assert.That(form.FocusedItemTextForTests, Is.EqualTo("second.txt"));
            }
        }

        [Test]
        public async Task ActivateFocusedFile_UsesPreviewRequestCallback()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "AGENTS.md", IsFile = true }
            };
            string previewPath = null;

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                form.SetPreviewRequestHandlerForTests(path =>
                {
                    previewPath = path;
                    return Task.CompletedTask;
                });
                await form.LoadCurrentPathForTestsAsync();
                await form.ActivateFocusedItemForTestsAsync();

                Assert.That(previewPath, Is.EqualTo(@"E:\Repo\AGENTS.md"));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_ReloadsCurrentPath()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "first.txt", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
                {
                    new AppServerFileSystemEntry { FileName = "second.txt", IsFile = true }
                };

                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                CollectionAssert.AreEqual(new[] { "first.txt" }, GetItemNames(form));
                await form.FlushPendingFileChangesForTestsAsync();

                CollectionAssert.AreEqual(new[] { "second.txt" }, GetItemNames(form));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_MergesBurstIntoOneDirectoryReload()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
                {
                    ChangedPaths = new List<string> { @"E:\Repo\a.txt" }
                });
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
                {
                    ChangedPaths = new List<string> { @"E:\Repo\b.txt" }
                });
                await form.FlushPendingFileChangesForTestsAsync();

                Assert.That(client.ReadDirectories.Count(path => path == @"E:\Repo"), Is.EqualTo(2));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_UpdatesVisibleStatusWithoutForcedSpeech()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();
            var announcements = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                announcements.Add))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.FlushPendingFileChangesForTestsAsync();

                Assert.That(announcements, Is.Empty);
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_RestoresMultiSelectionAndCurrentColumn()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "docs", IsDirectory = true },
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                var listView = GetItemListView(form);
                Assert.That(listView.Handle, Is.Not.EqualTo(IntPtr.Zero));
                listView.Items[1].Focused = true;
                listView.Items[0].Selected = true;
                listView.Items[1].Selected = true;
                SetActiveColumn(form, 2);

                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.FlushPendingFileChangesForTestsAsync();

                Assert.That(listView.SelectedItems, Has.Count.EqualTo(2));
                Assert.That(listView.FocusedItem.Index, Is.EqualTo(1));
                Assert.That(GetActiveColumn(form), Is.EqualTo(2));
                Assert.That(listView.FocusedItem.SubItems[2].Text, Is.EqualTo(
                    UiLocalization.Text("AppServerFileBrowserForm.EntryType.File", "File")));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_InvalidatesEquivalentWindowsPathMetadata()
        {
            var client = new FakeAppServerFilesClient();
            const string filePath = @"E:\Repo\notes.md";
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };
            client.Metadata[filePath] = new AppServerFileSystemMetadata { ModifiedAtMs = 1 };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.MetadataLoadForTestsAsync;
                client.Metadata[filePath] = new AppServerFileSystemMetadata { ModifiedAtMs = 2 };

                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
                {
                    ChangedPaths = new List<string> { "e:/repo/NOTES.md" }
                });
                await form.FlushPendingFileChangesForTestsAsync();
                await form.MetadataLoadForTestsAsync;

                Assert.That(client.MetadataRequests.Count(path => path == filePath), Is.EqualTo(2));
                Assert.That(
                    GetItemListView(form).Items[0].SubItems[1].Text,
                    Is.EqualTo(AppServerFileBrowserMetadataLoader.FormatModifiedTime(2)));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_ForCurrentDirectoryRefreshesAllMetadata()
        {
            var client = new FakeAppServerFilesClient();
            const string filePath = @"E:\Repo\notes.md";
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "notes.md", IsFile = true }
            };
            client.Metadata[filePath] = new AppServerFileSystemMetadata { ModifiedAtMs = 1 };

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.MetadataLoadForTestsAsync;
                client.Metadata[filePath] = new AppServerFileSystemMetadata { ModifiedAtMs = 2 };

                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
                {
                    ChangedPaths = new List<string> { @"E:\Repo" }
                });
                await form.FlushPendingFileChangesForTestsAsync();
                await form.MetadataLoadForTestsAsync;

                Assert.That(client.MetadataRequests.Count(path => path == filePath), Is.EqualTo(2));
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_DuringDirectoryLoadRunsPendingRefreshAfterLoad()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();
            var pendingRead = new TaskCompletionSource<List<AppServerFileSystemEntry>>();
            client.PendingDirectories[@"E:\Other"] = pendingRead;

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                var loadTask = form.LoadPathForTestsAsync(@"E:\Other");
                await Task.Yield();
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                client.Directories[@"E:\Other"] = new List<AppServerFileSystemEntry>
                {
                    new AppServerFileSystemEntry { FileName = "fresh.txt", IsFile = true }
                };
                pendingRead.SetResult(new List<AppServerFileSystemEntry>());
                await loadTask;
                await form.FlushPendingFileChangesForTestsAsync();

                CollectionAssert.AreEqual(new[] { "fresh.txt" }, GetItemNames(form));
            }
        }

        [Test]
        public async Task DirectoryLoad_CompletingAfterDisposeDoesNotTouchDisposedControls()
        {
            var client = new FakeAppServerFilesClient();
            var pendingRead = new TaskCompletionSource<List<AppServerFileSystemEntry>>();
            client.PendingDirectories[@"E:\Other"] = pendingRead;

            var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { });
            var loadTask = form.LoadPathForTestsAsync(@"E:\Other");
            await Task.Yield();
            form.Dispose();

            pendingRead.SetResult(new List<AppServerFileSystemEntry>());
            await loadTask;
        }

        [Test]
        public async Task HandleFileSystemChanged_DuringModalDialogWaitsUntilDialogCloses()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                form.SetModalDialogActiveForTests(true);
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.FlushPendingFileChangesForTestsAsync();
                Assert.That(client.ReadDirectories, Has.Count.EqualTo(1));

                form.SetModalDialogActiveForTests(false);
                await form.FlushPendingFileChangesForTestsAsync();
                Assert.That(client.ReadDirectories, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public async Task ClearSearch_ConsumesPendingFileRefreshOnce()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                await form.LoadCurrentPathForTestsAsync();
                await form.SearchForTestsAsync("txt");
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.ClearSearchForTestsAsync();
                await form.FlushPendingFileChangesForTestsAsync();

                Assert.That(client.ReadDirectories.Count(path => path == @"E:\Repo"), Is.EqualTo(2));
            }
        }

        [Test]
        public async Task LoadDirectory_RaisesCurrentPathChanged()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>
            {
                new AppServerFileSystemEntry { FileName = "src", IsDirectory = true }
            };
            client.Directories[@"E:\Repo\src"] = new List<AppServerFileSystemEntry>();
            var changedPaths = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                form.CurrentPathChanged += (sender, e) => changedPaths.Add(e.CurrentPath);
                await form.LoadCurrentPathForTestsAsync();
                await form.ActivateFocusedItemForTestsAsync();

                Assert.That(changedPaths, Does.Contain(@"E:\Repo\src"));
            }
        }

        private sealed class FakeAppServerFilesClient : IAppServerFileSystemClient, IAppServerFuzzyFileSearchClient
        {
            public readonly Dictionary<string, List<AppServerFileSystemEntry>> Directories =
                new Dictionary<string, List<AppServerFileSystemEntry>>();
            public readonly List<string> CreatedDirectories = new List<string>();
            public readonly List<WrittenFile> WrittenFiles = new List<WrittenFile>();
            public readonly List<string> RemovedPaths = new List<string>();
            public readonly List<string> ReadDirectories = new List<string>();
            public readonly Dictionary<string, TaskCompletionSource<List<AppServerFileSystemEntry>>> PendingDirectories =
                new Dictionary<string, TaskCompletionSource<List<AppServerFileSystemEntry>>>();
            public readonly List<AppServerFuzzyFileSearchRequest> SearchRequests = new List<AppServerFuzzyFileSearchRequest>();
            public readonly Dictionary<string, AppServerFileSystemMetadata> Metadata =
                new Dictionary<string, AppServerFileSystemMetadata>();
            public readonly Dictionary<string, TaskCompletionSource<AppServerFileSystemMetadata>> PendingMetadata =
                new Dictionary<string, TaskCompletionSource<AppServerFileSystemMetadata>>();
            public readonly List<string> MetadataRequests = new List<string>();
            public readonly List<CopiedPath> CopiedPaths = new List<CopiedPath>();
            public readonly HashSet<string> CopyFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public AppServerFuzzyFileSearchSessionEvent SearchResponse { get; set; }

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
            {
                ReadDirectories.Add(path);
                TaskCompletionSource<List<AppServerFileSystemEntry>> pending;
                if (PendingDirectories.TryGetValue(path, out pending))
                {
                    PendingDirectories.Remove(path);
                    return pending.Task;
                }

                List<AppServerFileSystemEntry> entries;
                return Task.FromResult(Directories.TryGetValue(path, out entries)
                    ? entries
                    : new List<AppServerFileSystemEntry>());
            }

            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
            {
                return Task.FromResult(new AppServerFileSystemReadResult { DataBase64 = string.Empty });
            }

            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
            {
                MetadataRequests.Add(path);
                TaskCompletionSource<AppServerFileSystemMetadata> pending;
                if (PendingMetadata.TryGetValue(path, out pending))
                {
                    return pending.Task;
                }

                AppServerFileSystemMetadata metadata;
                return Task.FromResult(Metadata.TryGetValue(path, out metadata)
                    ? metadata
                    : new AppServerFileSystemMetadata());
            }

            public Task WriteFileAsync(string path, byte[] data)
            {
                WrittenFiles.Add(new WrittenFile(path, data ?? new byte[0]));
                AddDirectoryEntry(path, false);
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string path, bool? recursive = null)
            {
                CreatedDirectories.Add(path);
                AddDirectoryEntry(path, true);
                return Task.CompletedTask;
            }

            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null)
            {
                RemovedPaths.Add(path);
                RemoveDirectoryEntry(path);
                return Task.CompletedTask;
            }

            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive)
            {
                CopiedPaths.Add(new CopiedPath(sourcePath, destinationPath, recursive));
                if (CopyFailures.Contains(sourcePath))
                {
                    throw new IOException("Copy failed.");
                }

                AddDirectoryEntry(destinationPath, recursive);
                return Task.CompletedTask;
            }

            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
            {
                return Task.FromResult(new AppServerFileSystemWatchResult());
            }

            public Task UnwatchFileSystemAsync(string watchId)
            {
                return Task.CompletedTask;
            }

            public Task<AppServerFuzzyFileSearchSessionEvent> StartFuzzyFileSearchAsync(AppServerFuzzyFileSearchRequest request, CancellationToken cancellationToken)
            {
                SearchRequests.Add(request);
                return Task.FromResult(SearchResponse ?? new AppServerFuzzyFileSearchSessionEvent());
            }

            private void AddDirectoryEntry(string path, bool isDirectory)
            {
                var parent = GetParentPath(path);
                var name = GetName(path);
                if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                List<AppServerFileSystemEntry> entries;
                if (!Directories.TryGetValue(parent, out entries))
                {
                    entries = new List<AppServerFileSystemEntry>();
                    Directories[parent] = entries;
                }

                entries.Add(new AppServerFileSystemEntry
                {
                    FileName = name,
                    IsDirectory = isDirectory,
                    IsFile = !isDirectory
                });

                if (isDirectory && !Directories.ContainsKey(path))
                {
                    Directories[path] = new List<AppServerFileSystemEntry>();
                }
            }

            private void RemoveDirectoryEntry(string path)
            {
                var parent = GetParentPath(path);
                var name = GetName(path);
                List<AppServerFileSystemEntry> entries;
                if (!Directories.TryGetValue(parent, out entries))
                {
                    return;
                }

                entries.RemoveAll(entry => entry.FileName == name);
            }

            private static string GetParentPath(string path)
            {
                var index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                return index <= 0 ? string.Empty : path.Substring(0, index);
            }

            private static string GetName(string path)
            {
                var index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
                return index < 0 ? path : path.Substring(index + 1);
            }

            public sealed class CopiedPath
            {
                public CopiedPath(string sourcePath, string destinationPath, bool recursive)
                {
                    SourcePath = sourcePath;
                    DestinationPath = destinationPath;
                    Recursive = recursive;
                }

                public string SourcePath { get; private set; }

                public string DestinationPath { get; private set; }

                public bool Recursive { get; private set; }
            }

            public sealed class WrittenFile
            {
                public WrittenFile(string path, byte[] data)
                {
                    Path = path;
                    Data = data;
                }

                public string Path { get; private set; }

                public byte[] Data { get; private set; }
            }
        }

        private static IReadOnlyList<string> GetItemNames(AppServerFileBrowserForm form)
        {
            return GetItemListView(form).Items
                .Cast<ListViewItem>()
                .Select(item => item.Text)
                .ToList();
        }

        private static AppServerFileWindowRuntimeScope CreateScope(
            string runtimeSource,
            string runtimeProfileId,
            string remoteProfileId,
            AppInteractionMode interactionMode)
        {
            return new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = runtimeSource,
                RuntimeProfileId = runtimeProfileId,
                RemoteProfileId = remoteProfileId,
                InteractionMode = interactionMode
            };
        }

        private static string GetStatusText(AppServerFileBrowserForm form)
        {
            return ((Label)typeof(AppServerFileBrowserForm)
                .GetField("_statusLabel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form)).Text;
        }

        private static string GetPasteFailure(int notPasted, bool partiallySucceeded)
        {
            return string.Format(
                partiallySucceeded
                    ? UiLocalization.Text(
                        "AppServerFileBrowserForm.Status.PastePartial.Template",
                        "Paste partially succeeded; {0} failed.")
                    : UiLocalization.Text(
                        "AppServerFileBrowserForm.Status.PasteFailed.Template",
                        "Paste failed; {0} item(s) were not pasted."),
                notPasted);
        }

        [Test]
        public async Task RefreshSameDirectory_DoesNotRaiseCurrentPathChanged()
        {
            var client = new FakeAppServerFilesClient();
            client.Directories[@"E:\Repo"] = new List<AppServerFileSystemEntry>();
            var changedPaths = new List<string>();

            using (var form = new AppServerFileBrowserForm(
                client,
                @"E:\Repo",
                paths => Task.CompletedTask,
                message => { }))
            {
                form.CurrentPathChanged += (sender, e) => changedPaths.Add(e.CurrentPath);
                await form.LoadCurrentPathForTestsAsync();
                await form.LoadCurrentPathForTestsAsync();
                client.Directories[@"e:/repo/"] = new List<AppServerFileSystemEntry>();
                await form.LoadPathForTestsAsync(@"e:/repo/");

                Assert.That(changedPaths, Is.Empty);
            }
        }

        private static ListView GetItemListView(AppServerFileBrowserForm form)
        {
            var field = typeof(AppServerFileBrowserForm).GetField(
                "_itemListView",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (ListView)field.GetValue(form);
        }

        private static void InvokeListViewKeyDown(AppServerFileBrowserForm form, ListView listView, KeyEventArgs args)
        {
            typeof(AppServerFileBrowserForm)
                .GetMethod("ItemListView_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { listView, args });
        }

        private static void SetActiveColumn(AppServerFileBrowserForm form, int column)
        {
            typeof(AppServerFileBrowserForm)
                .GetField("_activeColumnIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(form, column);
        }

        private static int GetActiveColumn(AppServerFileBrowserForm form)
        {
            return (int)typeof(AppServerFileBrowserForm)
                .GetField("_activeColumnIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(form);
        }
    }
}
