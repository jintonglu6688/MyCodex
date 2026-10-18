using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GitHubRepositoryDetailsFormTests
    {
        [Test]
        public void SameNamedRepositoriesKeepTheirOwnTargetServiceAndWindowGroup()
        {
            var serviceA = new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo", Description = "target A" });
            var serviceB = new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo", Description = "target B" });
            using (var mainA = new Form())
            using (var groupA = new TargetWindowGroup(mainA, () => "Ubuntu"))
            using (var repositoryA = CreateForm(serviceA))
            using (var mainB = new Form())
            using (var groupB = new TargetWindowGroup(mainB, () => "Development"))
            using (var repositoryB = CreateForm(serviceB))
            {
                repositoryA.Show();
                repositoryB.Show();
                Assert.That(repositoryA.Text, Does.StartWith("Ubuntu - "));
                Assert.That(repositoryB.Text, Does.StartWith("Development - "));
                Assert.That(repositoryA.Owner, Is.Null);
                var beforeB = serviceB.DetailsCallCount;
                InvokeLoadOverview(repositoryA);
                Assert.That(serviceB.DetailsCallCount, Is.EqualTo(beforeB));
                Assert.That(GetOverviewItems(repositoryA), Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.Description", "Description", "target A")));
                foreach (var window in groupA.GetForms()) window.Close();
                Assert.That(repositoryA.IsDisposed, Is.True);
                Assert.That(repositoryB.Visible, Is.True);
                InvokeLoadOverview(repositoryB);
                Assert.That(serviceB.DetailsCallCount, Is.GreaterThan(beforeB));
                Assert.That(GetOverviewItems(repositoryB), Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.Description", "Description", "target B")));
            }
        }

        [Test]
        public void LoadOverviewAsync_IncludesOpenIssuesAndTopics()
        {
            var details = new GitHubRepositoryEntry
            {
                NameWithOwner = "owner/demo",
                Description = "demo",
                OpenIssues = 7,
                Topics = "csharp,winforms"
            };

            using (var form = CreateForm(new FakeGitHubCliService(details)))
            {
                InvokeLoadOverview(form);

                var items = GetOverviewItems(form);
                Assert.That(items, Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.OpenIssues", "Open issues", "7")));
                Assert.That(items, Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.Topics", "Topics", "csharp,winforms")));
            }
        }

        [Test]
        public void LoadOverviewAsync_FallsBackToInitialRepository_WhenDetailsLoadFails()
        {
            var initial = new GitHubRepositoryEntry
            {
                NameWithOwner = "owner/demo",
                Description = "from search",
                OpenIssues = 3,
                Topics = "public,search",
                HttpsUrl = "https://github.com/owner/demo"
            };

            using (var form = new GitHubRepositoryDetailsForm(new FakeGitHubCliService(new InvalidOperationException("API failed")), initial, null))
            {
                InvokeLoadOverview(form);

                var items = GetOverviewItems(form);
                Assert.That(items, Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.Name", "Name", "owner/demo")));
                Assert.That(items, Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.OpenIssues", "Open issues", "3")));
                Assert.That(items, Does.Contain(BuildOverviewText("GitHubRepositoryDetailsForm.Overview.Topics", "Topics", "public,search")));
            }
        }

        [Test]
        public void PublicBrowseMode_HidesRepositoryMaintenanceActions()
        {
            using (var form = CreateForm(new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }), GitHubRepositoryDetailsMode.PublicBrowse))
            {
                SetSelectedListItem(form, "_branchesListView", new GitHubBranchEntry { Name = "feature", CommitSha = "abcdef123456" });
                FormTestDriver.SetField(form, "_matchingLocalRemoteName", "origin");
                FormTestDriver.SetField(form, "_currentLocalBranch", "main");
                SetSelectedListItem(form, "_issuesListView", new GitHubIssueEntry { Number = 1, Title = "bug", State = "OPEN", Url = "https://github.com/owner/demo/issues/1" });
                SetSelectedListItem(form, "_pullRequestsListView", new GitHubPullRequestEntry { Number = 2, Title = "pr", State = "OPEN", Url = "https://github.com/owner/demo/pull/2" });

                InvokeRefreshContextMenus(form);

                var branchMenu = GetContextMenuTexts(form, "_branchesContextMenu");
                Assert.That(branchMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyBranchName", "Copy Branch Name(&N)")));
                Assert.That(branchMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenBranch", "Open Branch(&O)")));
                Assert.That(branchMenu, Does.Not.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CheckoutBranchInWorkspace", "Fetch and Switch in Current Workspace(&W)")));
                Assert.That(branchMenu, Does.Not.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.SetDefaultBranch", "Set Default Branch(&B)")));
                Assert.That(branchMenu, Does.Not.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.DeleteBranch", "Delete Remote Branch(&D)")));

                var issueMenu = GetContextMenuTexts(form, "_issuesContextMenu");
                Assert.That(issueMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CreateIssue", "Create Issue(&C)")));
                Assert.That(issueMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CommentIssue", "Add Comment(&M)")));
                Assert.That(issueMenu, Does.Not.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CloseIssue", "Close Issue(&L)")));

                var pullRequestMenu = GetContextMenuTexts(form, "_pullRequestsContextMenu");
                Assert.That(pullRequestMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CommentPullRequest", "Add Comment(&M)")));
                Assert.That(pullRequestMenu, Does.Not.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ClosePullRequest", "Close PR(&L)")));
            }
        }

        [Test]
        public void ManageMode_KeepsRepositoryMaintenanceActions()
        {
            using (var form = new GitHubRepositoryDetailsForm(
                new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo", DefaultBranch = "main" }),
                new GitHubRepositoryEntry { NameWithOwner = "owner/demo", DefaultBranch = "main" },
                null,
                "C:\\repo",
                null,
                GitHubRepositoryDetailsMode.Manage))
            {
                SetSelectedListItem(form, "_branchesListView", new GitHubBranchEntry { Name = "feature", CommitSha = "abcdef123456" });
                FormTestDriver.SetField(form, "_matchingLocalRemoteName", "origin");
                FormTestDriver.SetField(form, "_currentLocalBranch", "main");
                SetSelectedListItem(form, "_issuesListView", new GitHubIssueEntry { Number = 1, Title = "bug", State = "OPEN", Url = "https://github.com/owner/demo/issues/1" });
                SetSelectedListItem(form, "_pullRequestsListView", new GitHubPullRequestEntry { Number = 2, Title = "pr", State = "OPEN", Url = "https://github.com/owner/demo/pull/2" });

                InvokeRefreshContextMenus(form);

                var branchMenu = GetContextMenuTexts(form, "_branchesContextMenu");
                Assert.That(branchMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CheckoutBranchInWorkspace", "Fetch and Switch in Current Workspace(&W)")));
                Assert.That(branchMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.SetDefaultBranch", "Set Default Branch(&B)")));
                Assert.That(branchMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.DeleteBranch", "Delete Remote Branch(&D)")));

                var issueMenu = GetContextMenuTexts(form, "_issuesContextMenu");
                Assert.That(issueMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CloseIssue", "Close Issue(&L)")));

                var pullRequestMenu = GetContextMenuTexts(form, "_pullRequestsContextMenu");
                Assert.That(pullRequestMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ClosePullRequest", "Close PR(&L)")));
            }
        }

        [Test]
        public void TagsContextMenu_ExposesReadOnlyNavigationActions()
        {
            using (var form = CreateForm(new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" }), GitHubRepositoryDetailsMode.PublicBrowse))
            {
                SetSelectedListItem(
                    form,
                    "_tagsListView",
                    new GitHubTagEntry
                    {
                        Name = "v1.0.0",
                        CommitSha = "abcdef123456",
                        ZipballUrl = "https://github.com/owner/demo/archive/refs/tags/v1.0.0.zip",
                        TarballUrl = "https://github.com/owner/demo/archive/refs/tags/v1.0.0.tar.gz"
                    });

                InvokeRefreshContextMenus(form);

                var tagMenu = GetContextMenuTexts(form, "_tagsContextMenu");
                Assert.That(tagMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyTagName", "Copy Tag Name(&N)")));
                Assert.That(tagMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTag", "Open Tag(&O)")));
                Assert.That(tagMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyTagCommitSha", "Copy Commit SHA(&S)")));
                Assert.That(tagMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTagZipball", "Open ZIP Archive(&Z)")));
                Assert.That(tagMenu, Does.Contain(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTagTarball", "Open TAR Archive(&T)")));
            }
        }

        [Test]
        public void SelectingUnloadedTab_DoesNotLoadUntilExplicitRefresh()
        {
            var service = new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" });
            using (var form = CreateForm(service))
            {
                form.CreateControl();
                var tabs = FormTestDriver.Field<TabControl>(form, "_tabs");
                var branchesTab = FormTestDriver.Field<TabPage>(form, "_branchesTabPage");

                tabs.SelectedTab = branchesTab;
                PumpEvents(20);

                Assert.That(service.DetailsCallCount, Is.EqualTo(0));
                Assert.That(service.BranchesCallCount, Is.EqualTo(0));
                Assert.That(service.TagsCallCount, Is.EqualTo(0));
                Assert.That(service.ReleasesCallCount, Is.EqualTo(0));
                Assert.That(service.IssuesCallCount, Is.EqualTo(0));
                Assert.That(service.PullRequestsCallCount, Is.EqualTo(0));
                Assert.That(service.ContributorsCallCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void RefreshExplicitlyLoadsSelectedTabOnce()
        {
            var service = new FakeGitHubCliService(new GitHubRepositoryEntry { NameWithOwner = "owner/demo" });
            using (var form = CreateForm(service))
            {
                form.CreateControl();
                var tabs = FormTestDriver.Field<TabControl>(form, "_tabs");
                var branchesTab = FormTestDriver.Field<TabPage>(form, "_branchesTabPage");

                tabs.SelectedTab = branchesTab;
                FormTestDriver.InvokeAndWait(form, "LoadCurrentTabAsync", true, false);

                Assert.That(service.BranchesCallCount, Is.EqualTo(1));
            }
        }

        private static GitHubRepositoryDetailsForm CreateForm(
            IGitHubCliService service,
            GitHubRepositoryDetailsMode mode = GitHubRepositoryDetailsMode.Manage)
        {
            return new GitHubRepositoryDetailsForm(
                service,
                new GitHubRepositoryEntry { NameWithOwner = "owner/demo" },
                null,
                null,
                null,
                mode);
        }

        private static void InvokeLoadOverview(GitHubRepositoryDetailsForm form)
        {
            form.CreateControl();
            var method = typeof(GitHubRepositoryDetailsForm).GetMethod(
                "LoadOverviewAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var task = method.Invoke(form, new object[] { CancellationToken.None }) as Task;
            Assert.That(task, Is.Not.Null);
            task.GetAwaiter().GetResult();
        }

        private static void PumpEvents(int iterations)
        {
            for (var index = 0; index < iterations; index++)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static IReadOnlyList<string> GetOverviewItems(GitHubRepositoryDetailsForm form)
        {
            var field = typeof(GitHubRepositoryDetailsForm).GetField(
                "_overviewListBox",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var listBox = field.GetValue(form) as ListBox;
            Assert.That(listBox, Is.Not.Null);
            return listBox.Items.Cast<object>().Select(item => item == null ? string.Empty : item.ToString()).ToList();
        }

        private static string BuildOverviewText(string key, string fallbackName, string value)
        {
            return string.Format(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Template", "{0}: {1}"),
                UiLocalization.Text(key, fallbackName),
                value);
        }

        private static void InvokeRefreshContextMenus(GitHubRepositoryDetailsForm form)
        {
            form.CreateControl();
            var method = typeof(GitHubRepositoryDetailsForm).GetMethod(
                "RefreshContextMenus",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(form, null);
        }

        private static void SetSelectedListItem(GitHubRepositoryDetailsForm form, string fieldName, object tag)
        {
            form.CreateControl();
            if (!form.Visible)
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
            }

            var listView = FormTestDriver.Field<ListView>(form, fieldName);
            listView.CreateControl();
            listView.Items.Clear();
            var item = new ListViewItem("item") { Tag = tag };
            listView.Items.Add(item);
            listView.Select();
            listView.Focus();
            item.Selected = true;
            item.Focused = true;
            System.Windows.Forms.Application.DoEvents();
        }

        private static IReadOnlyList<string> GetContextMenuTexts(GitHubRepositoryDetailsForm form, string fieldName)
        {
            var contextMenu = FormTestDriver.Field<ContextMenu>(form, fieldName);
            return contextMenu.MenuItems.OfType<MenuItem>().Select(item => item.Text).ToList();
        }

        private sealed class FakeGitHubCliService : IGitHubCliService
        {
            private readonly GitHubRepositoryEntry _details;
            private readonly Exception _detailsException;

            public int DetailsCallCount { get; private set; }

            public int BranchesCallCount { get; private set; }

            public int TagsCallCount { get; private set; }

            public int ReleasesCallCount { get; private set; }

            public int IssuesCallCount { get; private set; }

            public int PullRequestsCallCount { get; private set; }

            public int ContributorsCallCount { get; private set; }

            public FakeGitHubCliService(GitHubRepositoryEntry details)
            {
                _details = details;
            }

            public FakeGitHubCliService(Exception detailsException)
            {
                _detailsException = detailsException;
            }

            public Task<GitHubRepositoryEntry> GetRepositoryDetailsAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken)
            {
                DetailsCallCount++;
                if (_detailsException != null)
                {
                    throw _detailsException;
                }

                return Task.FromResult(_details);
            }

            public Task<GitHubCliAccountSnapshot> ReadAccountAsync(CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<GitHubRepositoryEntry>> ListRepositoriesAsync(string keyword, int limit, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<GitHubRepositorySearchResult> SearchRepositoriesAsync(GitHubRepositorySearchRequest request, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<GitHubRepositoryEntry> ForkRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<GitHubRepositoryEntry>> ListEmptyRepositoriesAsync(string keyword, int limit, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<GitHubBranchEntry>> ListBranchesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                BranchesCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubBranchEntry>)new List<GitHubBranchEntry>());
            }

            public Task DeleteRepositoryBranchAsync(GitHubRepositoryEntry repository, string branchName, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<GitHubTagEntry>> ListTagsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                TagsCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubTagEntry>)new List<GitHubTagEntry>());
            }

            public Task<IReadOnlyList<GitHubReleaseEntry>> ListReleasesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                ReleasesCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubReleaseEntry>)new List<GitHubReleaseEntry>());
            }

            public Task<GitHubReleaseDetailEntry> GetReleaseDetailsAsync(GitHubRepositoryEntry repository, string tagName, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken) { return ListIssuesAsync(repository, limit, "all", "all", cancellationToken); }
            public Task<IReadOnlyList<GitHubIssueEntry>> ListIssuesAsync(GitHubRepositoryEntry repository, int limit, string state, string scope, CancellationToken cancellationToken)
            {
                IssuesCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubIssueEntry>)new List<GitHubIssueEntry>());
            }

            public Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken) { return ListPullRequestsAsync(repository, limit, "all", "all", cancellationToken); }
            public Task<IReadOnlyList<GitHubPullRequestEntry>> ListPullRequestsAsync(GitHubRepositoryEntry repository, int limit, string state, string scope, CancellationToken cancellationToken)
            {
                PullRequestsCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubPullRequestEntry>)new List<GitHubPullRequestEntry>());
            }

            public Task<IReadOnlyList<GitHubContributorEntry>> ListContributorsAsync(GitHubRepositoryEntry repository, int limit, CancellationToken cancellationToken)
            {
                ContributorsCallCount++;
                return Task.FromResult((IReadOnlyList<GitHubContributorEntry>)new List<GitHubContributorEntry>());
            }
            public Task<GitHubDiscussionEntry> GetIssueDiscussionAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<GitHubDiscussionEntry> GetPullRequestDiscussionAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task AddPullRequestCommentAsync(GitHubRepositoryEntry repository, int number, string body, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task AddIssueCommentAsync(GitHubRepositoryEntry repository, int number, string body, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task CloseIssueAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task ReopenIssueAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<string> CreateIssueAsync(GitHubRepositoryEntry repository, string title, string body, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task ClosePullRequestAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task ReopenPullRequestAsync(GitHubRepositoryEntry repository, int number, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<string> CreatePullRequestAsync(GitHubRepositoryEntry repository, string baseBranch, string headBranch, string title, string body, bool draft, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<GitHubCreateRepositoryResult> CreateRepositoryAsync(GitHubCreateRepositoryRequest request, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task ArchiveRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task UnarchiveRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task DeleteRepositoryAsync(GitHubRepositoryEntry repository, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task RenameRepositoryAsync(GitHubRepositoryEntry repository, string newName, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task EditRepositoryMetadataAsync(GitHubRepositoryEntry repository, string description, string homepageUrl, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task SetDefaultBranchAsync(GitHubRepositoryEntry repository, string branchName, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task ChangeRepositoryVisibilityAsync(GitHubRepositoryEntry repository, string visibility, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public Task<GitCloneResult> CloneRepositoryAsync(GitHubRepositoryEntry repository, string targetParentDirectory, string targetFolderName, bool recurseSubmodules, bool useSshUrl, IProgress<GitCloneProgress> progress, CancellationToken cancellationToken) { throw new NotImplementedException(); }
            public void StartInteractiveLogin() { throw new NotImplementedException(); }
        }
    }
}
