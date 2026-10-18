using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class TargetMigrationForm : Form
    {
        private readonly Func<TargetCatalogSnapshot> _migrate;
        private readonly ListBox _status;
        private readonly Button _retry;
        private readonly Button _continue;
        private readonly Button _close;
        private bool _running;

        public TargetMigrationForm(TargetDataMigration migration)
            : this((migration ?? throw new ArgumentNullException(nameof(migration))).Run)
        {
        }

        internal TargetMigrationForm(Func<TargetCatalogSnapshot> migrate)
        {
            _migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
            Text = UiLocalization.Text("TargetMigrationForm.Title", "Upgrade target data");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 280);
            Size = new Size(680, 340);
            _status = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, TabIndex = 0,
                AccessibleName = UiLocalization.Text("TargetMigrationForm.Accessible.Progress", "Upgrade progress")
            };
            _retry = CreateButton("TargetMigrationForm.Button.Retry", "Retry(&R)", "TargetMigrationForm.Accessible.Retry", "Retry", 1, UiGlyph.Refresh);
            _continue = CreateButton("TargetMigrationForm.Button.Continue", "Continue(&C)", "TargetMigrationForm.Accessible.Continue", "Continue", 2, UiGlyph.Ok);
            _close = CreateButton("TargetMigrationForm.Button.Close", "Close(&L)", "TargetMigrationForm.Accessible.Close", "Close", 3, UiGlyph.Cancel);
            _retry.Enabled = false;
            _continue.Enabled = false;
            _retry.Click += async (s, e) => await RunMigrationAsync();
            _continue.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            _close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, TabStop = false, Padding = new Padding(8) };
            buttons.Controls.AddRange(new Control[] { _retry, _continue, _close });
            Controls.Add(_status);
            Controls.Add(buttons);
            AcceptButton = _continue;
            CancelButton = _close;
            UiTheme.ApplyForm(this);
            UiTheme.ApplyListBox(_status);
            Shown += async (s, e) => await RunMigrationAsync();
            FormClosing += (s, e) => { if (_running) e.Cancel = true; };
        }

        public TargetCatalogSnapshot Catalog { get; private set; }

        private async Task RunMigrationAsync()
        {
            if (_running) return;
            _running = true;
            _retry.Enabled = _continue.Enabled = _close.Enabled = false;
            _status.Items.Add(UiLocalization.Text("TargetMigrationForm.Status.Starting", "Preserving legacy files and generating the new target layout..."));
            try
            {
                Catalog = await Task.Run(_migrate);
                _status.Items.Add(UiLocalization.Text("TargetMigrationForm.Status.Complete", "Upgrade complete. Legacy files and a migration-backup copy are retained; the original target keeps its settings and phone pairing."));
                _continue.Enabled = true;
                _continue.Focus();
            }
            catch (Exception ex)
            {
                _status.Items.Add(UiLocalization.Text("TargetMigrationForm.Status.Failed", "Upgrade failed. No partial target was opened. Close other instances, check the original files and migration-backup, then retry."));
                // A parser exception can contain secret-bearing input. Show the error category only.
                _status.Items.Add(string.Format(UiLocalization.Text("TargetMigrationForm.Status.ErrorCategory", "Error category: {0}"), ex.GetType().Name));
                _retry.Enabled = true;
                _retry.Focus();
            }
            finally
            {
                _running = false;
                _close.Enabled = true;
                _status.SelectedIndex = _status.Items.Count - 1;
            }
        }

        private static Button CreateButton(string key, string fallback, string accessibleKey, string accessibleFallback, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = UiLocalization.Text(key, fallback), AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback),
                TabIndex = tabIndex, AutoSize = true, MinimumSize = new Size(100, 32)
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }
    }
}
