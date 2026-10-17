using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static int? _uiFontScalePercent;
        private static readonly ConditionalWeakTable<Form, FormScalingState> InitializedForms = new ConditionalWeakTable<Form, FormScalingState>();
        private static readonly Lazy<float> SystemDpi = new Lazy<float>(() =>
        {
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                return graphics.DpiX;
            }
        });

        internal static float UserFontScale { get { return (_uiFontScalePercent ?? 100) / 100F; } }
        internal static float ScaleFactor { get { return CalculateScaleFactor(SystemDpi.Value, UserFontScale); } }

        internal static void InitializeUiFontScale(int percent)
        {
            if (!_uiFontScalePercent.HasValue)
            {
                _uiFontScalePercent = percent == 125 || percent == 150 ? percent : 100;
            }
        }

        internal static float CalculateScaleFactor(float dpi, float userFontScale)
        {
            return dpi / 96F * userFontScale;
        }

        public static int Scale(int pixels)
        {
            return (int)Math.Round(pixels * ScaleFactor, MidpointRounding.AwayFromZero);
        }

        public static int Unscale(int pixels)
        {
            return (int)Math.Round(pixels / ScaleFactor, MidpointRounding.AwayFromZero);
        }

        internal static SizeF GetBaselineAutoScaleDimensions()
        {
            // A pixel font keeps this measurement at 96 DPI even on a high-DPI desktop.
            using (var font = new Font(_uiFontFamily ?? ResolveUiFontFamily(CultureInfo.CurrentUICulture.Name),
                BodyFontSize * 96F / 72F, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var baseline = new ContainerControl { Font = font, AutoScaleMode = AutoScaleMode.Font })
            {
                return baseline.CurrentAutoScaleDimensions;
            }
        }

        private static void InitializeFormScaling(Form form)
        {
            if (InitializedForms.TryGetValue(form, out _))
            {
                return;
            }

            InitializedForms.Add(form, new FormScalingState());
            // In net48, enabling Font scaling during construction either consumes the baseline
            // before children exist or repeatedly scales nested-container margins while suspended.
            // Set Font mode with neutral dimensions now, then apply the baseline once the tree exists.
            form.Font = CreateUiFont(BodyFontSize, FontStyle.Regular);
            form.AutoScaleMode = AutoScaleMode.Font;
            form.HandleCreated += CompleteFormScaling;
            // Subscribe before the form's own Shown handlers so focus is restored before they run.
            form.Shown += RestoreInitialFocus;
        }

        private static void CompleteFormScaling(object sender, EventArgs e)
        {
            var form = (Form)sender;
            form.HandleCreated -= CompleteFormScaling;
            var baseline = GetBaselineAutoScaleDimensions();
            // Avoid a redundant scaling pass: net48 otherwise delays its initial focus selection.
            if (form.CurrentAutoScaleDimensions == baseline)
            {
                return;
            }

            // Scaling MinimumSize repositions the hidden window without SWP_NOACTIVATE, so net48
            // activates the form before its children have handles and parks focus on the form.
            // Showing an already active form does not repeat the initial focus selection.
            var activated = false;
            EventHandler onActivated = (activatedSender, args) => activated = true;
            form.Activated += onActivated;
            try
            {
                // Resize handlers may replace fitted column widths during automatic layout.
                ScaleNativeMetrics(form);
                form.SuspendLayout();
                form.AutoScaleDimensions = baseline;
                form.PerformAutoScale();
                form.ResumeLayout(true);
            }
            finally
            {
                form.Activated -= onActivated;
            }

            FormScalingState state;
            if (activated && InitializedForms.TryGetValue(form, out state))
            {
                state.RestoreInitialFocus = true;
            }
        }

        private static void RestoreInitialFocus(object sender, EventArgs e)
        {
            var form = (Form)sender;
            form.Shown -= RestoreInitialFocus;
            FormScalingState state;
            // Respect focus that the form or the user has already moved to a child control.
            if (!InitializedForms.TryGetValue(form, out state) || !state.RestoreInitialFocus || !form.Focused)
            {
                return;
            }

            state.RestoreInitialFocus = false;
            var target = form.ActiveControl;
            var container = target as ContainerControl;
            while (container != null && container.ActiveControl != null)
            {
                target = container.ActiveControl;
                container = target as ContainerControl;
            }

            // Repeat the selection net48 performs on activation, now that children can take focus.
            if (target == null || !target.Focus())
            {
                form.SelectNextControl(null, true, true, true, false);
            }
        }

        private static void ScaleNativeMetrics(Control control)
        {
            // net48 scales bounds and SplitterDistance, but not column widths.
            // Panel minima are assigned by their layout owners, including Resize handlers.
            var list = control as ListView;
            if (list != null)
            {
                foreach (ColumnHeader column in list.Columns)
                {
                    if (column.Width > 0)
                    {
                        column.Width = Scale(column.Width);
                    }
                }
            }

            foreach (Control child in control.Controls)
            {
                // Embedded settings forms own their own initialization.
                if (!(child is Form))
                {
                    ScaleNativeMetrics(child);
                }
            }
        }

        private sealed class FormScalingState
        {
            internal bool RestoreInitialFocus;
        }
    }
}
