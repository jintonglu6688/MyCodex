using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedComboBox : ComboBox
    {
        private const int WindowPaint = 0x000F;
        private const int WindowPrint = 0x0317;
        private const int WindowPrintClient = 0x0318;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WindowPaint && IsHandleCreated && Visible)
            {
                UiTheme.PaintComboBoxChrome(this);
            }
            else if ((m.Msg == WindowPrint || m.Msg == WindowPrintClient) && m.WParam != System.IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(m.WParam))
                {
                    UiTheme.PaintComboBoxChrome(this, graphics);
                }
            }
        }
    }
}
