using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormLayoutPlanner
    {
        internal const int SplitterWidth = 6;
        internal const int DefaultNavigationWidth = 280;
        internal const int MinimumNavigationWidth = 200;
        internal const int MinimumConversationWidth = 440;
        internal const int MinimumActivityWidth = 320;
        internal const int DefaultActivityWidth = 470;
        internal const int MaximumActivityWidth = 520;
        internal const int HeaderPanelHeight = 64;
        internal const int HeaderSummaryVerticalPadding = 4;
        internal const int ComposePanelHeight = 150;
        internal const int ActivityCommandRowHeight = 44;
        internal const int WorkspaceSourceColumnWidth = 60;

        internal static readonly Size DefaultWindowSize = new Size(1500, 950);
        internal static readonly Size MinimumWindowSize = new Size(1024, 640);
        internal static readonly FormWindowState DefaultWindowState = FormWindowState.Normal;

        internal static Size ConstrainMinimumSize(Size minimumSize, Rectangle workingArea)
        {
            return new Size(Math.Min(minimumSize.Width, workingArea.Width), Math.Min(minimumSize.Height, workingArea.Height));
        }

        internal static Rectangle ConstrainWindowBounds(Rectangle bounds, Rectangle workingArea)
        {
            var width = Math.Min(Math.Max(bounds.Width, UiTheme.Scale(MinimumWindowSize.Width)), workingArea.Width);
            var height = Math.Min(Math.Max(bounds.Height, UiTheme.Scale(MinimumWindowSize.Height)), workingArea.Height);
            return new Rectangle(
                Clamp(bounds.X, workingArea.Left, workingArea.Right - width),
                Clamp(bounds.Y, workingArea.Top, workingArea.Bottom - height),
                width,
                height);
        }

        internal static int ResolveMainSplitterDistance(int width, int? savedDistance)
        {
            return Clamp(
                UiTheme.Scale(savedDistance ?? DefaultNavigationWidth),
                UiTheme.Scale(MinimumNavigationWidth),
                Math.Max(UiTheme.Scale(MinimumNavigationWidth), width - UiTheme.Scale(SplitterWidth) - MinimumContentWidth));
        }

        internal static int ResolveContentSplitterDistance(int width, int? savedDistance)
        {
            width -= UiTheme.Scale(SplitterWidth);
            var desiredCenterWidth = savedDistance.HasValue
                ? UiTheme.Scale(savedDistance.Value)
                : Math.Max(UiTheme.Scale(MinimumConversationWidth), width - UiTheme.Scale(DefaultActivityWidth));
            var maxCenterWidth = Math.Max(UiTheme.Scale(MinimumConversationWidth), width - UiTheme.Scale(MinimumActivityWidth));
            var centerWidth = Clamp(desiredCenterWidth, UiTheme.Scale(MinimumConversationWidth), maxCenterWidth);
            var activityWidth = width - centerWidth;
            if (activityWidth > UiTheme.Scale(MaximumActivityWidth))
            {
                centerWidth = Math.Min(maxCenterWidth, width - UiTheme.Scale(MaximumActivityWidth));
            }

            return Clamp(centerWidth, UiTheme.Scale(MinimumConversationWidth), maxCenterWidth);
        }

        internal static WorkspaceListColumnWidths ResolveWorkspaceListColumnWidths(int width, bool showSource)
        {
            var availableWidth = Math.Max(UiTheme.Scale(120), width - UiTheme.Scale(4));
            var sourceWidth = showSource ? Math.Min(UiTheme.Scale(WorkspaceSourceColumnWidth), Math.Max(0, availableWidth / 3)) : 0;
            var remainingWidth = Math.Max(UiTheme.Scale(120), availableWidth - sourceWidth);
            var branchWidth = Clamp(remainingWidth / 3, UiTheme.Scale(72), UiTheme.Scale(96));
            var nameWidth = Math.Max(UiTheme.Scale(96), remainingWidth - branchWidth);
            return new WorkspaceListColumnWidths(nameWidth, sourceWidth, branchWidth);
        }

        private static int MinimumContentWidth
        {
            get { return UiTheme.Scale(MinimumConversationWidth) + UiTheme.Scale(SplitterWidth) + UiTheme.Scale(MinimumActivityWidth); }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }
    }

    internal sealed class WorkspaceListColumnWidths
    {
        public WorkspaceListColumnWidths(int nameWidth, int sourceWidth, int branchWidth)
        {
            NameWidth = nameWidth;
            SourceWidth = sourceWidth;
            BranchWidth = branchWidth;
        }

        public int NameWidth { get; }

        public int SourceWidth { get; }

        public int BranchWidth { get; }
    }
}
