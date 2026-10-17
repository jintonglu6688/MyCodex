using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class TargetExitAllForm : Form
    {
        private readonly TargetExitAllCoordinator _coordinator;
        private readonly Func<TargetInstanceRecord, string> _name;
        private readonly ListView _targets;
        private readonly Label _status;
        private readonly Button _exit;
        private readonly Button _wait;
        private readonly Button _cancel;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _busy;
        private bool _committing;
        private bool _cancelRequested;

        public TargetExitAllForm(TargetExitAllCoordinator coordinator, Func<TargetInstanceRecord, string> targetName,
            string appDisplayName, bool installingUpdate = false)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _name = targetName ?? throw new ArgumentNullException(nameof(targetName));
            Text = string.Format(installingUpdate
                ? UiLocalization.Text("TargetExitAllForm.Title.Update", "Exit all {0} and install update")
                : UiLocalization.Text("TargetExitAllForm.Title", "Exit all {0}"), appDisplayName);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 340);
            Size = new Size(760, 440);
            _targets = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, TabIndex = 0,
                AccessibleName = UiLocalization.Text("TargetManagerForm.Accessible.Targets", "Targets")
            };
            _targets.Columns.Add(UiLocalization.Text("TargetManagerForm.Column.Name", "Name"), 260);
            _targets.Columns.Add(UiLocalization.Text("TargetManagerForm.Column.State", "State"), 400);
            _status = new Label { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), TabStop = false };
            _exit = CreateButton(installingUpdate
                ? UiLocalization.Text("TargetExitAllForm.Button.Update", "Exit all and install update(&X)")
                : UiLocalization.Text("TargetExitAllForm.Button.Exit", "Exit all(&X)"),
                UiLocalization.Text("TargetExitAllForm.Accessible.Exit", "Exit all"), 0);
            _exit.Enabled = false;
            _wait = CreateButton(UiLocalization.Text("TargetExitAllForm.Button.Wait", "Continue waiting(&W)"),
                UiLocalization.Text("TargetExitAllForm.Accessible.Wait", "Continue waiting"), 1);
            _wait.Visible = false;
            _cancel = CreateButton(UiLocalization.Text("TargetExitAllForm.Button.Cancel", "Cancel(&C)"),
                UiLocalization.Text("TargetExitAllForm.Accessible.Cancel", "Cancel"), 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), TabIndex = 1, TabStop = false };
            buttons.Controls.AddRange(new Control[] { _exit, _wait, _cancel });
            Controls.Add(_targets);
            Controls.Add(_status);
            Controls.Add(buttons);
            CancelButton = _cancel;
            AcceptButton = _cancel;
            ActiveControl = _cancel;
            _exit.Click += async (s, e) => await ExitAsync();
            _wait.Click += async (s, e) => await WaitAsync();
            _cancel.Click += async (s, e) => await CancelAsync();
            Shown += async (s, e) => await LoadScopeAsync();
            FormClosing += async (s, e) =>
            {
                if (DialogResult == DialogResult.OK) return;
                if (_busy) { e.Cancel = true; await CancelAsync(); }
            };
            UiTheme.ApplyForm(this);
            UiTheme.ApplyListView(_targets);
        }

        private static Button CreateButton(string text, string name, int index)
        {
            var button = new Button { AutoSize = true, Text = text, AccessibleName = name, TabIndex = index };
            UiTheme.ApplyDialogButton(button, UiGlyph.Ok);
            return button;
        }

        private async Task LoadScopeAsync()
        {
            _busy = true;
            _status.Text = UiLocalization.Text("TargetExitAllForm.Status.Inspecting", "Checking running targets...");
            try
            {
                var instances = await Task.Run(() => _coordinator.CaptureScope());
                ShowInstances(instances);
                _status.Text = UiLocalization.Text("TargetExitAllForm.Status.Confirm", "Only the listed targets in this edition will exit. Cancel keeps them running.");
                _exit.Enabled = !_cancelRequested;
            }
            catch (Exception ex)
            {
                ShowInstances(_coordinator.LastInspection);
                _status.Text = FailureText(ex);
            }
            finally { _busy = false; if (_cancelRequested) DialogResult = DialogResult.Cancel; }
        }

        private async Task ExitAsync()
        {
            if (_busy || _committing || _cancelRequested) return;
            _busy = true;
            _exit.Enabled = false;
            try
            {
                await _coordinator.PrepareAsync(_cancellation.Token, instance =>
                    _status.Text = string.Format(UiLocalization.Text("TargetExitAllForm.Status.Preparing", "Preparing {0} to exit..."), _name(instance)));
                _cancellation.Token.ThrowIfCancellationRequested();
                _committing = true;
                _status.Text = UiLocalization.Text("TargetExitAllForm.Status.Closing", "All targets agreed to exit. Waiting for them to close...");
                _cancel.Text = UiLocalization.Text("TargetExitAllForm.Button.Return", "Return to running windows(&C)");
                _cancel.AccessibleName = UiLocalization.Text("TargetExitAllForm.Accessible.Return", "Return to running windows");
                await _coordinator.CommitOtherTargetsAsync();
                await WaitForOthersAsync();
            }
            catch (OperationCanceledException) { await _coordinator.AbortAsync(); }
            catch (Exception ex)
            {
                if (!_committing) await _coordinator.AbortAsync();
                _status.Text = FailureText(ex);
                if (_committing) await ShowRemainingAsync();
            }
            finally
            {
                _busy = false;
                _exit.Enabled = !_committing && !_cancelRequested;
                if (_cancelRequested) DialogResult = DialogResult.Cancel;
            }
        }

        private async Task WaitAsync()
        {
            if (_busy || _cancelRequested) return;
            _busy = true;
            _wait.Enabled = false;
            try { await WaitForOthersAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = FailureText(ex); }
            finally { _busy = false; _wait.Enabled = true; if (_cancelRequested) DialogResult = DialogResult.Cancel; }
        }

        private async Task WaitForOthersAsync()
        {
            var remaining = await _coordinator.WaitForOtherTargetsAsync(_cancellation.Token);
            if (remaining.Count == 0) { DialogResult = DialogResult.OK; return; }
            ShowInstances(remaining);
            _wait.Visible = true;
            _status.Text = UiLocalization.Text("TargetExitAllForm.Status.Remaining", "These targets are still running or cannot be verified. Continue waiting, or return to their windows.");
        }

        private async Task ShowRemainingAsync()
        {
            try { ShowInstances(await Task.Run(() => _coordinator.GetRemainingOtherTargets())); }
            catch (Exception ex) { _status.Text = FailureText(ex); }
            _wait.Visible = true;
        }

        private async Task CancelAsync()
        {
            if (_cancelRequested) return;
            _cancelRequested = true;
            _cancellation.Cancel();
            _cancel.Enabled = false;
            await _coordinator.AbortAsync();
            if (!_busy) DialogResult = DialogResult.Cancel;
        }

        private void ShowInstances(IReadOnlyList<TargetInstanceReply> instances)
        {
            _targets.Items.Clear();
            foreach (var instance in instances)
            {
                var row = new ListViewItem(instance.Instance == null
                    ? UiLocalization.Text("TargetExitAllForm.Target.Unknown", "Unverified target") : _name(instance.Instance)) { Tag = instance.Instance };
                var status = instance.Status;
                row.SubItems.Add(TargetManagerForm.StatusText(instance) + (status == null ? string.Empty : " — " + string.Format(
                    UiLocalization.Text("TargetExitAllForm.State.Counts", "{0} active operations, {1} unsaved files"), status.ActiveWorkCount, status.UnsavedWindowCount)));
                _targets.Items.Add(row);
            }
        }

        private static string FailureText(Exception exception)
        {
            return UiLocalization.Text("TargetExitAllForm.Status.Failed", "Exit was not completed: ") + exception.Message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _cancellation.Dispose();
            base.Dispose(disposing);
        }
    }
}
