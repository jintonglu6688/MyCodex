using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class DonateForm : Form
    {
        private const string WechatDonateResourceName = "MyCodex.WinForms.Resources.Donate.Wechat";

        public DonateForm()
        {
            Text = UiLocalization.Text("DonateForm.Title", "Support MyCodex");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(420, 560);
            Size = new Size(460, 620);
            ShowInTaskbar = false;
            KeyPreview = true;
            AccessibleName = UiLocalization.Text("DonateForm.Accessible.Title", "Support MyCodex");
            UiTheme.ApplyForm(this);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = UiLocalization.Text("DonateForm.Header", "Support the project with WeChat"),
                TabStop = false,
                AccessibleName = UiLocalization.Text("DonateForm.Accessible.Header", "Support the project")
            };

            var infoTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 108,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.TextColor,
                BackColor = UiTheme.WindowBackColor,
                BorderStyle = BorderStyle.FixedSingle,
                Multiline = true,
                ReadOnly = true,
                TabStop = true,
                TabIndex = 0,
                ShortcutsEnabled = false,
                WordWrap = true,
                Text = string.Join(
                    Environment.NewLine,
                    UiLocalization.Text("DonateForm.Description", "If MyCodex has helped you, scan the QR code below in WeChat to support the author directly."),
                    UiLocalization.Text("DonateForm.SupportMotivation", "Your support helps keep development going.")),
                AccessibleName = UiLocalization.Text("DonateForm.Accessible.Description", "Support description")
            };
            TextBoxLineEndingNormalizer.NormalizeTextBox(infoTextBox);
            TextBoxCaretHelper.MoveToStart(infoTextBox);

            var closeButton = new Button
            {
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Size = new Size(96, 32),
                Text = UiLocalization.Text("DonateForm.Button.Close", "Close(&C)"),
                AccessibleName = UiLocalization.Text("DonateForm.Accessible.Close", "Close"),
                TabIndex = 2
            };
            UiTheme.ApplyButton(closeButton, UiGlyph.Cancel);
            closeButton.Click += delegate { Close(); };
            AcceptButton = closeButton;
            CancelButton = closeButton;

            var imagePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 1
            };

            var qrPictureBox = new FocusablePictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                TabStop = true,
                TabIndex = 1,
                AccessibleName = UiLocalization.Text("DonateForm.Accessible.WechatCode", "WeChat QR"),
                AccessibleRole = AccessibleRole.Graphic
            };

            var image = LoadWechatDonateImage();
            if (image != null)
            {
                qrPictureBox.Image = image;
            }
            else
            {
                qrPictureBox.Visible = false;
                var unavailableLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                    ForeColor = UiTheme.MutedTextColor,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = UiLocalization.Text("DonateForm.ImageUnavailable", "The WeChat QR code is currently unavailable."),
                    TabStop = false,
                    AccessibleName = UiLocalization.Text("DonateForm.Accessible.ImageUnavailable", "WeChat QR unavailable")
                };
                imagePanel.Controls.Add(unavailableLabel);
            }

            if (qrPictureBox.Visible)
            {
                imagePanel.Controls.Add(qrPictureBox);
            }

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 2
            };
            closeButton.Location = new Point(footerPanel.Width - closeButton.Width, 6);
            footerPanel.Resize += delegate
            {
                closeButton.Location = new Point(footerPanel.ClientSize.Width - closeButton.Width, UiTheme.Scale(6));
            };
            footerPanel.Controls.Add(closeButton);

            mainPanel.Controls.Add(imagePanel);
            mainPanel.Controls.Add(infoTextBox);
            mainPanel.Controls.Add(titleLabel);
            mainPanel.Controls.Add(footerPanel);
            Controls.Add(mainPanel);

            FormClosed += DonateForm_FormClosed;
            KeyDown += DonateForm_KeyDown;
            Shown += delegate { infoTextBox.Focus(); infoTextBox.SelectionLength = 0; };
        }

        private static Image LoadWechatDonateImage()
        {
            var assembly = typeof(DonateForm).Assembly;
            using (var stream = assembly.GetManifestResourceStream(WechatDonateResourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    return Image.FromStream(copy);
                }
            }
        }

        private void DonateForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            DisposePictureBoxImages(this);
        }

        private static void DisposePictureBoxImages(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                var pictureBox = control as PictureBox;
                if (pictureBox != null && pictureBox.Image != null)
                {
                    pictureBox.Image.Dispose();
                    pictureBox.Image = null;
                }

                if (control.HasChildren)
                {
                    DisposePictureBoxImages(control);
                }
            }
        }

        private void DonateForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            Close();
        }

        private sealed class FocusablePictureBox : PictureBox
        {
            public FocusablePictureBox()
            {
                SetStyle(ControlStyles.Selectable, true);
            }

            protected override void OnEnter(EventArgs e)
            {
                base.OnEnter(e);
                Invalidate();
            }

            protected override void OnLeave(EventArgs e)
            {
                base.OnLeave(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs pe)
            {
                base.OnPaint(pe);

                if (Focused)
                {
                    var bounds = ClientRectangle;
                    bounds.Inflate(-2, -2);
                    ControlPaint.DrawFocusRectangle(pe.Graphics, bounds);
                }
            }
        }
    }
}
