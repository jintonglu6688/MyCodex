using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.RemoteRelay;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class RemotePairingConfirmationForm : Form
    {
        private readonly Button _allowButton;
        private readonly Button _rejectButton;
        private readonly Button _laterButton;
        private readonly ListBox _detailsListBox;

        public RemotePairingConfirmationForm(
            string targetName,
            VerifiedPairingClaim verifiedClaim)
        {
            if (verifiedClaim == null || verifiedClaim.Claim == null)
            {
                throw new ArgumentNullException(nameof(verifiedClaim));
            }

            Text = UiLocalization.Text(
                "RemotePairingConfirmationForm.Title",
                "Confirm Pairing");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 320);
            Size = new Size(600, 380);
            ShowInTaskbar = false;
            TabStop = false;
            UiTheme.ApplyForm(this);

            _allowButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemotePairingConfirmationForm.Button.Allow", "Allow(&A)"),
                UiLocalization.Text("RemotePairingConfirmationForm.Accessible.Allow", "Allow pairing"),
                0,
                UiGlyph.Ok,
                DialogResult.OK);
            _rejectButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemotePairingConfirmationForm.Button.Reject", "Reject(&R)"),
                UiLocalization.Text("RemotePairingConfirmationForm.Accessible.Reject", "Reject pairing"),
                1,
                UiGlyph.Cancel,
                DialogResult.No);
            _laterButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemotePairingConfirmationForm.Button.Later", "Later(&L)"),
                UiLocalization.Text("RemotePairingConfirmationForm.Accessible.Later", "Decide later"),
                2,
                UiGlyph.Cancel,
                DialogResult.Cancel);

            _detailsListBox = new ListBox
            {
                AccessibleName = UiLocalization.Text(
                    "RemotePairingConfirmationForm.Accessible.Details",
                    "Pairing details"),
                Dock = DockStyle.Fill,
                TabIndex = 0,
                IntegralHeight = false
            };
            _detailsListBox.Items.Add(string.Format(
                UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.Target.Template",
                    "Target: {0}"),
                targetName ?? verifiedClaim.Claim.HostId));
            _detailsListBox.Items.Add(string.Format(
                UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.Device.Template",
                    "Device: {0}"),
                verifiedClaim.Claim.DeviceName ?? verifiedClaim.Claim.DeviceId));
            _detailsListBox.Items.Add(string.Format(
                UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.Platform.Template",
                    "Platform: {0}"),
                verifiedClaim.Claim.Platform ?? string.Empty));
            _detailsListBox.Items.Add(string.Format(
                UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.Code.Template",
                    "Verification code: {0}"),
                FormatDigitReadableCode(verifiedClaim.ShortCode)));
            _detailsListBox.Items.Add(string.Format(
                UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.RequestTime.Template",
                    "Requested: {0}"),
                FormatRequestTime(verifiedClaim.Claim.CreatedAt)));
            UiTheme.ApplyListBox(_detailsListBox);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                TabStop = false
            };
            buttons.Controls.Add(_laterButton);
            buttons.Controls.Add(_rejectButton);
            buttons.Controls.Add(_allowButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(CreateInstructionLabel(), 0, 0);
            root.Controls.Add(_detailsListBox, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            CancelButton = _laterButton;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_detailsListBox.Items.Count > 0)
            {
                _detailsListBox.SelectedIndex = 0;
            }

            ActiveControl = _detailsListBox;
            _detailsListBox.Focus();
        }

        private static Label CreateInstructionLabel()
        {
            var label = new Label
            {
                Text = UiLocalization.Text(
                    "RemotePairingConfirmationForm.Instruction",
                    "Compare the six-digit code with the phone before allowing access."),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabStop = false,
                Margin = new Padding(0, 0, 0, 12)
            };
            UiTheme.ApplyLabel(label, false);
            return label;
        }

        private static string FormatDigitReadableCode(string value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty).Select(character => character.ToString()).ToArray());
        }

        private static string FormatRequestTime(long unixMilliseconds)
        {
            if (unixMilliseconds <= 0)
            {
                return UiLocalization.Text(
                    "RemotePairingConfirmationForm.Detail.RequestTime.Unknown",
                    "Unknown");
            }

            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMilliseconds(unixMilliseconds)
                .ToLocalTime()
                .ToString("g");
        }
    }
}
