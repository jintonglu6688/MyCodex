using System.Drawing;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal sealed class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
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
