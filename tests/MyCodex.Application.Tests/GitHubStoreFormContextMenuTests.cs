using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GitHubStoreFormContextMenuTests
    {
        [Test]
        public void RefreshRepositoryContextMenu_HidesActions_WhenNoRepositoryIsAvailable()
        {
            using (var form = CreateForm())
            {
                SetRepositoryEntries(form, null, Array.Empty<GitHubRepositoryEntry>());

                InvokePrivate(form, "RefreshRepositoryContextMenu");

                Assert.That(GetContextMenuItems(form), Is.Empty);
            }
        }

        [Test]
        public void RefreshRepositoryContextMenu_ShowsOpenAndCopyUrlOnly_WhenOnlyWebUrlIsAvailable()
        {
            using (var form = CreateForm())
            {
                SetRepositoryEntries(form, "repo", new[] { new GitHubRepositoryEntry { HttpsUrl = "https://github.com/demo/example" } });

                InvokePrivate(form, "RefreshRepositoryContextMenu");

                Assert.That(
                    GetContextMenuItems(form),
                    Is.EqualTo(
                        new[]
                        {
                            UiLocalization.Text("GitHubStoreForm.Menu.OpenRepository", "Open Repository(&O)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)")
                        }));
            }
        }

        [Test]
        public void RefreshRepositoryContextMenu_ShowsRepositoryActions_WhenOnlyNameWithOwnerIsAvailable()
        {
            using (var form = CreateForm())
            {
                SetRepositoryEntries(form, "repo", new[] { new GitHubRepositoryEntry { NameWithOwner = "owner/example" } });

                InvokePrivate(form, "RefreshRepositoryContextMenu");

                Assert.That(
                    GetContextMenuItems(form),
                    Is.EqualTo(
                        new[]
                        {
                            UiLocalization.Text("GitHubStoreForm.Menu.ViewDetails", "View Details(&D)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.OpenRepository", "Open Repository(&O)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)"),
                            "-",
                            UiLocalization.Text("GitHubStoreForm.Menu.CopyFullName", "Copy Full Name(&N)"),
                            "-",
                            UiLocalization.Text("GitHubStoreForm.Menu.Clone", "Clone(&L)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.ForkAndClone", "Fork and Clone(&F)")
                }));
            }
        }

        [Test]
        public void RefreshRepositoryContextMenu_ShowsFetchAndPull_WhenLocalCloneExists()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "MyCodex.MarketFetchMenuTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempPath);
                var repository = new GitHubRepositoryEntry
                {
                    NameWithOwner = "owner/example",
                    HttpsUrl = "https://github.com/owner/example"
                };
                var localPath = Path.Combine(tempPath, GitClonePathPlanner.InferTargetFolderName(repository.HttpsUrl));
                Directory.CreateDirectory(localPath);
                Directory.CreateDirectory(Path.Combine(localPath, ".git"));

                using (var form = CreateForm(tempPath))
                {
                    SetRepositoryEntries(form, "repo", new[] { repository });

                    InvokePrivate(form, "RefreshRepositoryContextMenu");

                    Assert.That(
                        GetContextMenuItems(form),
                        Is.EqualTo(
                            new[]
                            {
                                UiLocalization.Text("GitHubStoreForm.Menu.ViewDetails", "View Details(&D)"),
                                UiLocalization.Text("GitHubStoreForm.Menu.OpenRepository", "Open Repository(&O)"),
                                UiLocalization.Text("GitHubStoreForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)"),
                                "-",
                                UiLocalization.Text("GitHubStoreForm.Menu.CopyFullName", "Copy Full Name(&N)"),
                                "-",
                                UiLocalization.Text("GitHubStoreForm.Menu.Clone", "Clone(&L)"),
                                UiLocalization.Text("GitHubStoreForm.Menu.ForkAndClone", "Fork and Clone(&F)"),
                                "-",
                                UiLocalization.Text("GitHubStoreForm.Menu.Fetch", "Fetch(&F)"),
                                UiLocalization.Text("GitHubStoreForm.Menu.Pull", "Pull(&P)")
                            }));
                }
            }
            finally
            {
                if (Directory.Exists(tempPath))
                {
                    Directory.Delete(tempPath, true);
                }
            }
        }

        [Test]
        public void RefreshRepositoryContextMenu_HidesFetch_WhenNoLocalCloneExists()
        {
            using (var form = CreateForm(Path.Combine(Path.GetTempPath(), "MyCodex.MarketFetchMenuTest_" + Guid.NewGuid().ToString("N"))))
            {
                var repository = new GitHubRepositoryEntry
                {
                    NameWithOwner = "owner/example",
                    HttpsUrl = "https://github.com/owner/example"
                };
                SetRepositoryEntries(form, "repo", new[] { repository });

                InvokePrivate(form, "RefreshRepositoryContextMenu");

                Assert.That(
                    GetContextMenuItems(form),
                    Is.EqualTo(
                        new[]
                        {
                            UiLocalization.Text("GitHubStoreForm.Menu.ViewDetails", "View Details(&D)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.OpenRepository", "Open Repository(&O)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)"),
                            "-",
                            UiLocalization.Text("GitHubStoreForm.Menu.CopyFullName", "Copy Full Name(&N)"),
                            "-",
                            UiLocalization.Text("GitHubStoreForm.Menu.Clone", "Clone(&L)"),
                            UiLocalization.Text("GitHubStoreForm.Menu.ForkAndClone", "Fork and Clone(&F)")
                        }));
            }
        }

        [Test]
        public void BuildRankingRequest_RecentCreatedPreset_AppendsCreatedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.RecentCreated", "Recent Created"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Keyword, Does.Match(@"\bcreated:>="));

                var createdMatch = Regex.Match(request.Keyword, @"created:>=([0-9]{4}-[0-9]{2}-[0-9]{2})");
                Assert.That(createdMatch.Success, Is.True);
                Assert.That(
                    DateTime.TryParseExact(
                        createdMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var _),
                    Is.True);
            }
        }

        [Test]
        public void BuildRankingRequest_TrendingTodayPreset_AppendsPushedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.TrendingToday", "Trending Today"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.Sort, Is.EqualTo("stars"));
                Assert.That(request.Order, Is.EqualTo("desc"));

                var pushedMatch = Regex.Match(request.Keyword, @"pushed:>=([0-9]{4}-[0-9]{2}-[0-9]{2})");
                Assert.That(pushedMatch.Success, Is.True);
                Assert.That(
                    DateTime.TryParseExact(
                        pushedMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date),
                    Is.True);
                Assert.That(date, Is.InRange(DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date));
            }
        }

        [Test]
        public void BuildRankingRequest_TrendingWeekPreset_AppendsPushedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.TrendingWeek", "Trending Week"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.Sort, Is.EqualTo("stars"));
                Assert.That(request.Order, Is.EqualTo("desc"));

                var pushedMatch = Regex.Match(request.Keyword, @"pushed:>=([0-9]{4}-[0-9]{2}-[0-9]{2})");
                Assert.That(pushedMatch.Success, Is.True);
                Assert.That(
                    DateTime.TryParseExact(
                        pushedMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date),
                    Is.True);
                Assert.That(date, Is.InRange(DateTime.UtcNow.Date.AddDays(-7), DateTime.UtcNow.Date));
            }
        }

        [Test]
        public void BuildRankingRequest_TrendingMonthPreset_AppendsPushedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.TrendingMonth", "Trending Month"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.Sort, Is.EqualTo("stars"));
                Assert.That(request.Order, Is.EqualTo("desc"));

                var pushedMatch = Regex.Match(request.Keyword, @"pushed:>=([0-9]{4}-[0-9]{2}-[0-9]{2})");
                Assert.That(pushedMatch.Success, Is.True);
                Assert.That(
                    DateTime.TryParseExact(
                        pushedMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date),
                    Is.True);
                Assert.That(date, Is.InRange(DateTime.UtcNow.Date.AddDays(-30), DateTime.UtcNow.Date));
            }
        }

        [Test]
        public void BuildRankingRequest_NewReposPreset_AppendsCreatedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.NewRepos", "New Repositories"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Sort, Is.EqualTo("created"));
                Assert.That(request.Order, Is.EqualTo("desc"));
                Assert.That(request.Keyword, Does.Match(@"\bcreated:>="));

                var createdMatch = Regex.Match(request.Keyword, @"created:>=([0-9]{4}-[0-9]{2}-[0-9]{2})");
                Assert.That(createdMatch.Success, Is.True);
                Assert.That(
                    DateTime.TryParseExact(
                        createdMatch.Groups[1].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date),
                    Is.True);
                Assert.That(date, Is.InRange(DateTime.UtcNow.Date.AddDays(-31), DateTime.UtcNow.Date.AddDays(-29)));
            }
        }

        [Test]
        public void BuildRankingRequest_NewStarsPreset_AppendsPushedQualifierToKeyword()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.NewStars", "New Stars"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Sort, Is.EqualTo("stars"));
                Assert.That(request.Order, Is.EqualTo("desc"));
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.MinStars.GetValueOrDefault(), Is.EqualTo(0));
            }
        }

        [Test]
        public void BuildRankingRequest_NewForksPreset_AppendsPushedQualifierAndMinForks()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.NewForks", "New Forks"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Sort, Is.EqualTo("forks"));
                Assert.That(request.Order, Is.EqualTo("desc"));
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.MinForks.GetValueOrDefault(), Is.GreaterThanOrEqualTo(20));
            }
        }

        [Test]
        public void BuildRankingRequest_HighActivityPreset_AppendsRecentQualifierAndSortsUpdated()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                var presetItem = presetCombo.Items
                    .Cast<object>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            item?.ToString(),
                            UiLocalization.Text("GitHubStoreForm.Preset.HighActivity", "High Activity"),
                            StringComparison.Ordinal));
                Assert.That(presetItem, Is.Not.Null);
                presetCombo.SelectedItem = presetItem;

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildRankingRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Sort, Is.EqualTo("updated"));
                Assert.That(request.Order, Is.EqualTo("desc"));
                Assert.That(request.Keyword, Does.Match(@"\bpushed:>="));
                Assert.That(request.MinStars.GetValueOrDefault(), Is.GreaterThanOrEqualTo(50));
            }
        }

        [Test]
        public void BuildSearchRequest_DoesNotApplyRankingPreset()
        {
            using (var form = CreateForm())
            {
                var presetCombo = GetField<ComboBox>(form, "_storePresetComboBox");
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                keywordTextBox.Text = "ui";
                presetCombo.SelectedIndex = Math.Min(1, presetCombo.Items.Count - 1);

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildSearchRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.Keyword, Is.EqualTo("ui"));
                Assert.That(request.Keyword, Does.Not.Contain("pushed:>="));
                Assert.That(request.Keyword, Does.Not.Contain("created:>="));
            }
        }

        [Test]
        public void BuildSearchRequest_AdvancedFilters_MapsAllFields()
        {
            using (var form = CreateForm())
            {
                var keywordTextBox = GetField<TextBox>(form, "_keywordTextBox");
                var languageTextBox = GetField<TextBox>(form, "_languageTextBox");
                var topicTextBox = GetField<TextBox>(form, "_topicTextBox");
                var licenseTextBox = GetField<TextBox>(form, "_licenseTextBox");
                var minStarsTextBox = GetField<TextBox>(form, "_minStarsTextBox");
                var maxStarsTextBox = GetField<TextBox>(form, "_maxStarsTextBox");
                var minForksTextBox = GetField<TextBox>(form, "_minForksTextBox");
                var maxForksTextBox = GetField<TextBox>(form, "_maxForksTextBox");
                var minOpenIssuesTextBox = GetField<TextBox>(form, "_minOpenIssuesTextBox");
                var maxOpenIssuesTextBox = GetField<TextBox>(form, "_maxOpenIssuesTextBox");

                keywordTextBox.Text = "lang";
                languageTextBox.Text = "csharp";
                topicTextBox.Text = "wpf";
                licenseTextBox.Text = " mit ";
                minStarsTextBox.Text = "100";
                maxStarsTextBox.Text = "200";
                minForksTextBox.Text = "2";
                maxForksTextBox.Text = "20";
                minOpenIssuesTextBox.Text = "1";
                maxOpenIssuesTextBox.Text = "9";

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildSearchRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.License, Is.EqualTo("mit"));
                Assert.That(request.MinStars, Is.EqualTo(100));
                Assert.That(request.MaxStars, Is.EqualTo(200));
                Assert.That(request.MinForks, Is.EqualTo(2));
                Assert.That(request.MaxForks, Is.EqualTo(20));
                Assert.That(request.MinOpenIssues, Is.EqualTo(1));
                Assert.That(request.MaxOpenIssues, Is.EqualTo(9));
            }
        }

        [Test]
        public void BuildSearchRequest_AdvancedFilters_InvalidValues_AreIgnored()
        {
            using (var form = CreateForm())
            {
                var minStarsTextBox = GetField<TextBox>(form, "_minStarsTextBox");
                var maxStarsTextBox = GetField<TextBox>(form, "_maxStarsTextBox");
                var minForksTextBox = GetField<TextBox>(form, "_minForksTextBox");
                var maxForksTextBox = GetField<TextBox>(form, "_maxForksTextBox");

                minStarsTextBox.Text = "-1";
                maxStarsTextBox.Text = "abc";
                minForksTextBox.Text = " ";
                maxForksTextBox.Text = "0";

                var request = InvokePrivate<GitHubRepositorySearchRequest>(form, "BuildSearchRequest");
                Assert.That(request, Is.Not.Null);
                Assert.That(request.MinStars, Is.Null);
                Assert.That(request.MaxStars, Is.Null);
                Assert.That(request.MinForks, Is.Null);
                Assert.That(request.MaxForks, Is.EqualTo(0));
            }
        }

        [Test]
        public void CreateRepositoryItem_IncludesExtendedColumns()
        {
            var repository = new GitHubRepositoryEntry
            {
                NameWithOwner = "owner/repo",
                Description = "desc",
                PrimaryLanguage = "C#",
                Stars = 10,
                Forks = 5,
                OpenIssues = 3,
                License = "MIT",
                PushedAt = new DateTimeOffset(2026, 01, 01, 12, 30, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 01, 02, 14, 45, 0, TimeSpan.Zero),
                Topics = "wpf winforms"
            };

            var item = InvokePrivateStatic<ListViewItem>(
                typeof(GitHubStoreForm),
                "CreateRepositoryItem",
                new object[] { repository });

            Assert.That(item, Is.Not.Null);
            Assert.That(item.SubItems.Count, Is.EqualTo(10));
            Assert.That(item.SubItems[5].Text, Is.EqualTo("3"));
            Assert.That(item.SubItems[6].Text, Is.EqualTo("MIT"));
            Assert.That(item.SubItems[9].Text, Is.EqualTo("wpf winforms"));
        }

        private static GitHubStoreForm CreateForm(string defaultCloneParentDirectory = null)
        {
            return new GitHubStoreForm(
                new GitHubCliService(),
                null,
                string.IsNullOrWhiteSpace(defaultCloneParentDirectory)
                    ? Environment.CurrentDirectory
                    : defaultCloneParentDirectory);
        }

        private static void SetRepositoryEntries(
            GitHubStoreForm form,
            string selectedItemText,
            IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            var listView = GetField<ListView>(form, "_repositoriesListView");
            var source = repositories == null || repositories.Count == 0
                ? new List<GitHubRepositoryEntry>()
                : repositories.ToList();
            var items = source
                .Select(item => new ListViewItem(string.IsNullOrWhiteSpace(selectedItemText) ? "Repository" : selectedItemText)
                {
                    Tag = item
                })
                .ToList();

            listView.BeginUpdate();
            form.CreateControl();
            listView.Items.Clear();
            foreach (var item in items)
            {
                listView.Items.Add(item);
            }

            listView.EndUpdate();
            if (source.Count > 0 && listView.Items.Count > 0)
            {
                listView.Items[0].Selected = true;
                listView.Items[0].Focused = true;
                listView.Select();
                listView.Focus();
            }

            var repositoriesField = GetFieldInfo(form, "_repositories");
            repositoriesField.SetValue(form, source);
        }

        private static IReadOnlyList<string> GetContextMenuItems(GitHubStoreForm form)
        {
            var contextMenu = GetField<ContextMenu>(form, "_repositoryContextMenu");
            return contextMenu
                .MenuItems
                .OfType<MenuItem>()
                .Select(item => item.Text)
                .ToList();
        }

        private static T GetField<T>(GitHubStoreForm form, string fieldName) where T : class
        {
            var field = typeof(GitHubStoreForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected private field not found: " + fieldName);
            return field.GetValue(form) as T;
        }

        private static FieldInfo GetFieldInfo(object instance, string fieldName)
        {
            var targetType = instance == null ? typeof(GitHubStoreForm) : instance.GetType();
            var field = targetType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected private field not found: " + fieldName);
            return field;
        }

        private static void InvokePrivate(GitHubStoreForm form, string methodName)
        {
            var method = typeof(GitHubStoreForm).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected private method not found: " + methodName);
            method.Invoke(form, null);
        }

        private static T InvokePrivate<T>(GitHubStoreForm form, string methodName)
        {
            var method = typeof(GitHubStoreForm).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected private method not found: " + methodName);
            return (T)method.Invoke(form, null);
        }

        private static T InvokePrivateStatic<T>(Type targetType, string methodName, object[] parameters)
        {
            var method = targetType.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Expected private static method not found: " + methodName);
            return (T)method.Invoke(null, parameters);
        }
    }
}

