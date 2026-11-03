using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedRadioButton : RadioButton
    {
        public ThemedRadioButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return UiTheme.GetTogglePreferredSize(this, base.GetPreferredSize(proposedSize));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Appearance == Appearance.Button || !UiTheme.PaintToggleControl(this, e))
            {
                base.OnPaint(e);
            }
        }
    }
}
