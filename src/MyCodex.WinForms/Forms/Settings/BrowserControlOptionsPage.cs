using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class BrowserControlOptionsPage : UserControl
    {
        private readonly IBrowserExtensionSetupService _service;
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        private readonly CheckBox _enabled;
        private readonly Label _status;
        private readonly Label[] _browserStatus = new Label[2];
        private readonly Button[] _actions = new Button[2];
        private readonly BrowserExtensionSetupPage[] _pages = new BrowserExtensionSetupPage[2];
        private bool _checking;
        private bool _disposed;
        private bool _configured;

        internal BrowserControlOptionsPage(IBrowserExtensionSetupService service, bool supported, bool enabled)
        {
            _service = supported ? service : null;
            AccessibleName = UiLocalization.Text("BrowserControl.Title", "Browser Control");
            TabStop = true;
            AutoScroll = true;
            BackColor = UiTheme.WindowBackColor;
            _enabled = new CheckBox
            {
                Name = "BrowserControlEnabled", AutoSize = true, TabIndex = 0,
                Text = UiLocalization.Text("BrowserControl.Enabled", "Enable &browser control"),
                AccessibleName = UiLocalization.Text("BrowserControl.Accessible.Enabled", "Enable browser control"),
                Enabled = _service != null, Checked = enabled
            };
            UiTheme.ApplyCheckBox(_enabled);
            _status = UiControlFactory.CreateMutedLabel(_service == null
                ? UiLocalization.Text("BrowserControl.Unsupported", "Browser control requires a local Windows target and Codex 0.164 or newer.")
                : UiLocalization.Text("BrowserControl.Help", "Select OK to save and automatically prepare browser control. Cancel leaves the setting unchanged. Install the extension in each browser you want to use."));
            _status.AutoSize = true;
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(UiTheme.OuterPadding), TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(_enabled, 0, 0);
            root.Controls.Add(_status, 0, 1);
            for (var i = 0; i < 2; i++)
            {
                var index = i;
                _browserStatus[i] = UiControlFactory.CreateMutedLabel(i == 0 ? "Microsoft Edge" : "Google Chrome");
                _browserStatus[i].AutoSize = true;
                _browserStatus[i].Padding = new Padding(0, 16, 0, 4);
                _actions[i] = UiControlFactory.CreateActionButton(string.Empty, string.Empty, i + 1, 240);
                _actions[i].Name = i == 0 ? "EdgeExtension" : "ChromeExtension";
                _actions[i].AutoSize = true;
                _actions[i].Enabled = false;
                SetAction(i, false);
                _actions[i].Click += (sender, args) => OpenPage(index);
                root.Controls.Add(_browserStatus[i], 0, i * 2 + 2);
                root.Controls.Add(_actions[i], 0, i * 2 + 3);
            }
            Controls.Add(root);
            _enabled.CheckedChanged += (sender, args) => RefreshSummary();
            RefreshSummary();
            VisibleChanged += async (sender, args) =>
            {
                if (Visible && _service != null)
                {
                    _timer.Start();
                    await RefreshAsync();
                }
                else _timer.Stop();
            };
            _timer.Tick += async (sender, args) => await RefreshAsync();
        }

        internal bool? Selection => _service == null ? (bool?)null : _enabled.Checked;

        private async Task RefreshAsync()
        {
            if (_checking || _disposed || _service == null) return;
            _checking = true;
            try
            {
                var configured = true;
                for (var i = 0; i < 2; i++)
                {
                    var report = await _service.CheckAsync((BrowserExtensionBrowser)i, _closing.Token);
                    if (_disposed) return;
                    configured &= (report.Tools == BrowserAutomationToolState.Configured || report.Tools == BrowserAutomationToolState.Ready) &&
                        (report.BrowserInstalled == false || report.Bridge == BrowserExtensionBridgeState.Registered);
                    var name = i == 0 ? "Microsoft Edge" : "Google Chrome";
                    var installed = report.Profiles.Any(profile => profile.State == BrowserExtensionInstallState.Enabled);
                    var missing = report.Profiles.Count == 0 || report.Profiles.All(profile => profile.State == BrowserExtensionInstallState.NotInstalled);
                    var state = !report.BrowserInstalled.HasValue
                        ? UiLocalization.Text("BrowserControl.BrowserUnknown", "Unable to determine whether the browser is installed")
                        : report.BrowserInstalled == false
                        ? UiLocalization.Text("BrowserControl.BrowserMissing", "Browser not installed")
                        : installed
                            ? UiLocalization.Text("BrowserControl.ExtensionInstalled", "Extension enabled; live connection not checked")
                            : missing
                                ? UiLocalization.Text("BrowserControl.ExtensionMissing", "Extension not installed")
                                : UiLocalization.Text("BrowserControl.ExtensionNeedsAttention", "Extension disabled or status uncertain; check extensions");
                    var profiles = report.Profiles.Count > 1
                        ? "\r\n" + string.Join("; ", report.Profiles.Select(profile => (profile.ProfileDirectory ?? "?") + ": " +
                            (profile.State == BrowserExtensionInstallState.Enabled
                                ? UiLocalization.Text("BrowserControl.ProfileEnabled", "enabled")
                                : UiLocalization.Text("BrowserControl.ProfileCheck", "check extensions")))) : string.Empty;
                    _browserStatus[i].Text = name + ": " + state + profiles;
                    var uncertain = !missing && !report.Profiles.Any(profile =>
                        profile.State == BrowserExtensionInstallState.Enabled || profile.State == BrowserExtensionInstallState.Disabled);
                    _pages[i] = missing || uncertain ? BrowserExtensionSetupPage.Store : BrowserExtensionSetupPage.Extensions;
                    SetAction(i, missing, uncertain);
                    _actions[i].Enabled = report.BrowserInstalled == true;
                }
                _configured = configured;
                RefreshSummary();
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!_disposed) _status.Text = UiLocalization.Text("BrowserControl.CheckFailed", "Unable to check browser settings. Detection will retry automatically.");
            }
            finally { _checking = false; }
        }

        private void RefreshSummary()
        {
            if (_service == null) return;
            var state = !_enabled.Checked
                ? UiLocalization.Text("BrowserControl.Disabled", "Browser control is off.")
                : _configured
                    ? UiLocalization.Text("BrowserControl.EnabledState", "Browser control enabled; local components configured.")
                    : UiLocalization.Text("BrowserControl.NeedsSetup", "Select OK to enable browser control and automatically prepare missing components.");
            _status.Text = state + "\r\n" + UiLocalization.Text("BrowserControl.Help", "Select OK to save and automatically prepare browser control. Cancel leaves the setting unchanged. Install the extension in each browser you want to use.");
        }

        private void SetAction(int index, bool install, bool uncertain = false)
        {
            var text = index == 0
                ? install ? UiLocalization.Text("BrowserControl.InstallEdge", "Install &Edge extension") : UiLocalization.Text("BrowserControl.ManageEdge", "Manage &Edge extension")
                : install ? UiLocalization.Text("BrowserControl.InstallChrome", "Install C&hrome extension") : UiLocalization.Text("BrowserControl.ManageChrome", "Manage C&hrome extension");
            if (uncertain) text = index == 0
                ? UiLocalization.Text("BrowserControl.CheckEdge", "Check / install &Edge extension")
                : UiLocalization.Text("BrowserControl.CheckChrome", "Check / install C&hrome extension");
            _actions[index].Text = text;
            _actions[index].AccessibleName = text.Replace("&", string.Empty);
        }

        private void OpenPage(int index)
        {
            try { _service.OpenPage((BrowserExtensionBrowser)index, _pages[index]); }
            catch (Exception)
            {
                MessageBox.Show(this, UiLocalization.Text("BrowserControl.OpenFailed", "Unable to open this browser. Check that it is installed."),
                    UiLocalization.Text("BrowserControl.Title", "Browser Control"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _timer.Dispose();
                _closing.Cancel();
                _closing.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
