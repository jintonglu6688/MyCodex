using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    /// <summary>Reviews hooks for one target's workspace or thread; trust only ever covers the hash the user saw.</summary>
    public sealed class HooksManagementForm : Form
    {
        private readonly HookManagementService _service;
        private readonly string _workspacePath;
        private readonly string _threadId;
        private readonly ListView _hookListView;
        private readonly TextBox _detailsTextBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _trustButton;
        private readonly Button _revokeButton;
        private readonly Button _enableButton;
        private readonly Button _disableButton;
        private readonly Button _closeButton;
        private AppServerHooksListResult _list;
        private bool _isBusy;

        public HooksManagementForm(HookManagementService service, string workspacePath, string threadId)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _workspacePath = workspacePath;
            _threadId = threadId;

            Text = UiLocalization.Text("HooksManagementForm.Title", "Hooks Management");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 560);
            Size = new Size(1040, 700);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _hookListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("HooksManagementForm.Accessible.Hooks", "Hooks"),
                TabIndex = 0
            };
            UiTheme.ApplyListView(_hookListView);
            AddColumns(_hookListView);

            _detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("HooksManagementForm.Accessible.Details", "Details"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_detailsTextBox, true, true);

            _refreshButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("HooksManagementForm.Accessible.Refresh", "Refresh"),
                2);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            _trustButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Trust", "Trust(&T)..."),
                UiLocalization.Text("HooksManagementForm.Accessible.Trust", "Trust"),
                3);
            UiTheme.ApplyButton(_trustButton, UiGlyph.Ok);
            _revokeButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Revoke", "Revoke Trust(&V)"),
                UiLocalization.Text("HooksManagementForm.Accessible.Revoke", "Revoke trust"),
                4);
            UiTheme.ApplyButton(_revokeButton, UiGlyph.Delete);
            _enableButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Enable", "Enable(&E)"),
                UiLocalization.Text("HooksManagementForm.Accessible.Enable", "Enable"),
                5);
            UiTheme.ApplyButton(_enableButton, UiGlyph.Ok);
            _disableButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Disable", "Disable(&D)"),
                UiLocalization.Text("HooksManagementForm.Accessible.Disable", "Disable"),
                6);
            UiTheme.ApplyButton(_disableButton, UiGlyph.Cancel);
            _closeButton = UiControlFactory.CreateListActionButton(
                UiLocalization.Text("HooksManagementForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("HooksManagementForm.Accessible.Close", "Close"),
                7);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            _statusLabel = UiControlFactory.CreateMutedLabel(string.Empty);
            _statusLabel.AutoSize = true;

            _hookListView.SelectedIndexChanged += (sender, e) => ShowSelection();
            _refreshButton.Click += async (sender, e) => await RunAsync(LoadAsync);
            _trustButton.Click += async (sender, e) => await RunAsync(TrustSelectedAsync);
            _revokeButton.Click += async (sender, e) => await RunAsync(() => ApplySelectedAsync(hook => _service.RevokeAsync(_workspacePath, _threadId, hook)));
            _enableButton.Click += async (sender, e) => await RunAsync(() => ApplySelectedAsync(hook => _service.SetEnabledAsync(_workspacePath, _threadId, hook, true)));
            _disableButton.Click += async (sender, e) => await RunAsync(() => ApplySelectedAsync(hook => _service.SetEnabledAsync(_workspacePath, _threadId, hook, false)));
            _closeButton.Click += (sender, e) => Close();
            KeyDown += async (sender, e) =>
            {
                if (e.KeyCode == Keys.F5 && !_isBusy)
                {
                    e.SuppressKeyPress = true;
                    await RunAsync(LoadAsync);
                }
            };
            Shown += async (sender, e) => await RunAsync(LoadAsync);

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("HooksManagementForm.Status.Loading", "Reading hooks."));
            RefreshButtons();
        }

        private void BuildLayout()
        {
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, UiTheme.ControlSpacing, 0, 0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            actions.Controls.AddRange(new Control[] { _refreshButton, _trustButton, _revokeButton, _enableButton, _disableButton, _closeButton });

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_hookListView, 0, 0);
            root.Controls.Add(_detailsTextBox, 0, 1);
            root.Controls.Add(actions, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            Controls.Add(root);
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            RefreshButtons();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // A failed read must not look like an empty list, so the previous list stays and the error is shown.
                SetStatus(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("HooksManagementForm.Status.Failed", "Hooks operation failed: {0}"),
                    ex.Message));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isBusy = false;
                RefreshButtons();
            }
        }

        private async Task LoadAsync()
        {
            ShowList(await _service.ListAsync(_workspacePath, _threadId));
            SetStatus(BuildSummary());
        }

        private async Task TrustSelectedAsync()
        {
            var hook = SelectedHook;
            if (hook == null)
            {
                return;
            }

            var prompt = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("HooksManagementForm.Confirm.Trust", "Trust this hook? It will run on {0} events until its definition changes.{1}{1}{2}"),
                hook.EventName,
                Environment.NewLine,
                DescribeHandler(hook));
            if (MessageBox.Show(this, prompt, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            await ApplySelectedAsync(selected => _service.TrustAsync(_workspacePath, _threadId, new[] { selected }));
        }

        private async Task ApplySelectedAsync(Func<AppServerHookInfo, Task<HookStateChangeResult>> change)
        {
            var hook = SelectedHook;
            if (hook == null)
            {
                return;
            }

            var result = await change(hook);
            if (result.List != null)
            {
                ShowList(result.List, hook.Key);
            }

            switch (result.Status)
            {
                case HookStateChangeStatus.Changed:
                    SetStatus(UiLocalization.Text("HooksManagementForm.Status.Changed", "The hook changed since it was shown. Review it again before saving."));
                    break;
                case HookStateChangeStatus.VersionUnavailable:
                    SetStatus(UiLocalization.Text("HooksManagementForm.Status.VersionUnavailable", "The user configuration version could not be confirmed. Nothing was saved."));
                    break;
                case HookStateChangeStatus.Saved:
                    SetStatus(result.Overridden
                        ? UiLocalization.Text("HooksManagementForm.Status.Overridden", "Saved, but a higher configuration layer overrides it.")
                        : UiLocalization.Text("HooksManagementForm.Status.Saved", "Saved. Hooks already running may finish with the old setting."));
                    break;
            }
        }

        private AppServerHookInfo SelectedHook
        {
            get { return _hookListView.SelectedItems.Count == 0 ? null : _hookListView.SelectedItems[0].Tag as AppServerHookInfo; }
        }

        private void ShowList(AppServerHooksListResult list, string selectedKey = null)
        {
            selectedKey = selectedKey ?? (SelectedHook == null ? null : SelectedHook.Key);
            _list = list ?? new AppServerHooksListResult();
            _hookListView.BeginUpdate();
            _hookListView.Items.Clear();
            // Hooks that need review come first; OrderBy is stable, so the server's order is kept within each group.
            foreach (var hook in _list.Hooks.OrderBy(hook => HookStateEditPlanner.NeedsReview(hook) ? 0 : 1))
            {
                var item = CreateItem(hook);
                _hookListView.Items.Add(item);
                item.Selected = string.Equals(hook.Key, selectedKey, StringComparison.Ordinal);
            }

            if (_hookListView.SelectedItems.Count == 0 && _hookListView.Items.Count > 0)
            {
                _hookListView.Items[0].Selected = true;
            }

            _hookListView.EndUpdate();
            if (_hookListView.SelectedItems.Count > 0)
            {
                _hookListView.SelectedItems[0].Focused = true;
            }

            ShowSelection();
        }

        private void ShowSelection()
        {
            var text = new StringBuilder(BuildDetails(SelectedHook));

            if (_list != null && !_list.IsThreadView && !string.IsNullOrWhiteSpace(_threadId))
            {
                text.AppendLine(UiLocalization.Text(
                    "HooksManagementForm.Details.WorkspaceViewOnly",
                    "This list shows the workspace configuration. Project hooks the session loads may be missing until the session is loaded by this runtime."));
            }

            foreach (var problem in _list == null ? Enumerable.Empty<string>() : _list.Errors.Concat(_list.Warnings))
            {
                text.AppendLine(problem);
            }

            _detailsTextBox.Text = text.ToString();
            RefreshButtons();
        }

        internal static string BuildDetails(AppServerHookInfo hook)
        {
            var text = new StringBuilder();
            if (hook != null)
            {
                AppendLine(text, "HooksManagementForm.Details.Event", "Event", hook.EventName);
                AppendLine(text, "HooksManagementForm.Details.Matcher", "Matcher", hook.Matcher);
                AppendLine(text, "HooksManagementForm.Details.Handler", "Handler", DescribeHandler(hook));
                AppendLine(text, "HooksManagementForm.Details.Timeout", "Timeout (seconds)", hook.TimeoutSec.HasValue ? hook.TimeoutSec.Value.ToString(CultureInfo.InvariantCulture) : null);
                AppendLine(text, "HooksManagementForm.Details.Status", "Status", DescribeStatus(hook));
                AppendLine(text, "HooksManagementForm.Details.Source", "Source", DescribeSource(hook));
                AppendLine(text, "HooksManagementForm.Details.SourcePath", "Source file", hook.SourcePath);
                AppendLine(text, "HooksManagementForm.Details.Plugin", "Plugin", hook.PluginId);
                AppendLine(text, "HooksManagementForm.Details.Hash", "Definition hash", hook.CurrentHash);
                AppendLine(text, "HooksManagementForm.Details.Key", "Key", hook.Key);
            }

            return text.ToString();
        }

        private static void AppendLine(StringBuilder text, string key, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                text.Append(UiLocalization.Text(key, label)).Append(": ").AppendLine(value);
            }
        }

        private string BuildSummary()
        {
            var hooks = _list == null ? new AppServerHookInfo[0] : _list.Hooks.ToArray();
            return string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("HooksManagementForm.Status.Summary", "{0} hooks, {1} need review before they run, {2} warnings or errors."),
                hooks.Length,
                hooks.Count(hook => hook.Enabled && HookStateEditPlanner.NeedsReview(hook)),
                _list == null ? 0 : _list.Warnings.Count + _list.Errors.Count);
        }

        internal static string DescribeHandler(AppServerHookInfo hook)
        {
            switch (hook.HandlerType)
            {
                case "command":
                    return hook.Command;
                case "mcpTool":
                    return "MCP " + hook.McpServer + " / " + hook.McpTool;
                default:
                    return hook.HandlerType;
            }
        }

        internal static void AddColumns(ListView listView)
        {
            listView.Columns.Add(UiLocalization.Text("HooksManagementForm.Column.Hook", "Hook"), 460);
            listView.Columns.Add(UiLocalization.Text("HooksManagementForm.Column.Handler", "Handler"), 420);
        }

        /// <summary>The first column leads with trust so a screen reader announces it before anything else.</summary>
        internal static ListViewItem CreateItem(AppServerHookInfo hook)
        {
            var parts = new[]
            {
                DescribeTrust(hook),
                hook.Enabled ? null : UiLocalization.Text("HooksManagementForm.State.Disabled", "Disabled"),
                DescribeSource(hook),
                DescribeEvent(hook.EventName)
            };
            return new ListViewItem(new[]
            {
                string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part))),
                DescribeHandler(hook)
            }) { Tag = hook };
        }

        internal static string DescribeSource(AppServerHookInfo hook)
        {
            switch (hook.Source)
            {
                case "plugin":
                    var pluginId = hook.PluginId ?? string.Empty;
                    var at = pluginId.IndexOf('@');
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        UiLocalization.Text("HooksManagementForm.Source.Plugin", "{0} plugin"),
                        at > 0 ? pluginId.Substring(0, at) : pluginId);
                case "project":
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        UiLocalization.Text("HooksManagementForm.Source.Project", "Project {0}"),
                        GetProjectRoot(hook.SourcePath));
                case "user":
                    return UiLocalization.Text("HooksManagementForm.Source.User", "User config");
                default:
                    return hook.Source;
            }
        }

        private static string GetProjectRoot(string sourcePath)
        {
            // Project hooks live in <project>\.codex\hooks.json or config.toml.
            var directory = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetDirectoryName(sourcePath);
            return directory != null && string.Equals(Path.GetFileName(directory), ".codex", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : directory ?? sourcePath;
        }

        private static string DescribeEvent(string eventName)
        {
            return string.IsNullOrEmpty(eventName) ? eventName : char.ToUpperInvariant(eventName[0]) + eventName.Substring(1);
        }

        private static string DescribeTrust(AppServerHookInfo hook)
        {
            switch (hook.TrustStatus)
            {
                case "trusted":
                    return UiLocalization.Text("HooksManagementForm.TrustShort.Trusted", "Trusted");
                case "managed":
                    return UiLocalization.Text("HooksManagementForm.TrustShort.Managed", "Managed");
                case "modified":
                    return UiLocalization.Text("HooksManagementForm.TrustShort.Modified", "Changed");
                default:
                    return UiLocalization.Text("HooksManagementForm.TrustShort.Untrusted", "Untrusted");
            }
        }

        internal static string DescribeStatus(AppServerHookInfo hook)
        {
            var enabled = hook.Enabled
                ? UiLocalization.Text("HooksManagementForm.State.Enabled", "Enabled")
                : UiLocalization.Text("HooksManagementForm.State.Disabled", "Disabled");
            string trust;
            switch (hook.TrustStatus)
            {
                case "trusted":
                    trust = UiLocalization.Text("HooksManagementForm.Trust.Trusted", "Trusted");
                    break;
                case "managed":
                    trust = UiLocalization.Text("HooksManagementForm.Trust.Managed", "Managed by policy");
                    break;
                case "modified":
                    trust = UiLocalization.Text("HooksManagementForm.Trust.Modified", "Changed since trusted, needs review");
                    break;
                default:
                    trust = UiLocalization.Text("HooksManagementForm.Trust.Untrusted", "Not trusted, needs review");
                    break;
            }

            return enabled + ", " + trust;
        }

        private void RefreshButtons()
        {
            var hook = SelectedHook;
            var canModify = !_isBusy && HookStateEditPlanner.CanModify(hook);
            _refreshButton.Enabled = !_isBusy;
            _trustButton.Enabled = canModify && HookStateEditPlanner.NeedsReview(hook);
            _revokeButton.Enabled = canModify && !HookStateEditPlanner.NeedsReview(hook);
            _enableButton.Enabled = canModify && !hook.Enabled;
            _disableButton.Enabled = canModify && hook.Enabled;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
            _statusLabel.AccessibleName = text;
        }
    }
}
