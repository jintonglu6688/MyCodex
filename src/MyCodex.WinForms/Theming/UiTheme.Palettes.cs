using System.Drawing;

namespace MyCodex.WinForms.Theming
{
    internal static partial class UiTheme
    {
        private static UiThemePalette CreateLightPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = Color.FromArgb(240, 244, 248),
                PanelBackColor = Color.FromArgb(248, 250, 253),
                CardBackColor = Color.FromArgb(255, 255, 255),
                SubtleBackColor = Color.FromArgb(238, 243, 249),
                BorderColor = Color.FromArgb(120, 134, 155),
                ActiveBorderColor = Color.FromArgb(30, 92, 162),
                SelectedBackColor = Color.FromArgb(219, 234, 254),
                SelectedTextColor = Color.FromArgb(30, 58, 138),
                TextColor = Color.FromArgb(30, 41, 59),
                MutedTextColor = Color.FromArgb(71, 85, 105),
                DisabledTextColor = Color.FromArgb(121, 132, 147),
                AccentColor = Color.FromArgb(30, 92, 162),
                AccentSoftBackColor = Color.FromArgb(228, 239, 252),
                GlyphBlueColor = Color.FromArgb(30, 92, 162),
                GlyphRedColor = Color.FromArgb(190, 18, 60),
                GlyphGreenColor = Color.FromArgb(22, 132, 77),
                GlyphTealColor = Color.FromArgb(15, 118, 110),
                GlyphAmberColor = Color.FromArgb(180, 83, 9),
                GlyphIndigoColor = Color.FromArgb(79, 70, 229),
                GlyphCyanColor = Color.FromArgb(14, 116, 144),
                GlyphNeutralColor = Color.FromArgb(100, 116, 139),
                ChatProcessingColor = Color.FromArgb(217, 119, 6),
                ChatErrorBackColor = Color.FromArgb(255, 241, 242),
                ChatApprovalBackColor = Color.FromArgb(254, 252, 232),
                ChatCommandBackColor = Color.FromArgb(245, 243, 255),
                ChatFileChangeBackColor = Color.FromArgb(240, 253, 244),
                ChatAccountBackColor = Color.FromArgb(236, 254, 255),
                ChatSystemBackColor = Color.FromArgb(248, 250, 252),
                ChatProcessingBackColor = Color.FromArgb(255, 251, 235),
                ChatCodexBackColor = Color.FromArgb(239, 246, 255),
                ChatUserBackColor = Color.FromArgb(240, 253, 250),
                StatusReadyColor = Color.FromArgb(21, 128, 61),
                StatusProcessingColor = Color.FromArgb(180, 83, 9),
                StatusErrorColor = Color.FromArgb(220, 38, 38),
                ShadowColor = Color.FromArgb(20, 0, 0, 0),
                ShadowCornerColor = Color.FromArgb(30, 0, 0, 0),
                ShadowFadeColor = Color.FromArgb(0, 0, 0, 0)
            };
        }

        // Softer beige surfaces with less glare, offered as a separate choice for readers who find white too bright.
        private static UiThemePalette CreateWarmLightPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = Color.FromArgb(244, 241, 232),
                PanelBackColor = Color.FromArgb(251, 250, 245),
                CardBackColor = Color.FromArgb(255, 253, 247),
                SubtleBackColor = Color.FromArgb(236, 232, 220),
                BorderColor = Color.FromArgb(122, 133, 142),
                ActiveBorderColor = Color.FromArgb(45, 106, 116),
                SelectedBackColor = Color.FromArgb(178, 211, 199),
                SelectedTextColor = Color.FromArgb(16, 58, 52),
                TextColor = Color.FromArgb(31, 41, 51),
                MutedTextColor = Color.FromArgb(88, 101, 111),
                DisabledTextColor = Color.FromArgb(132, 140, 144),
                AccentColor = Color.FromArgb(46, 111, 115),
                AccentSoftBackColor = Color.FromArgb(226, 239, 234),
                GlyphBlueColor = Color.FromArgb(36, 94, 156),
                GlyphRedColor = Color.FromArgb(168, 45, 68),
                GlyphGreenColor = Color.FromArgb(36, 117, 76),
                GlyphTealColor = Color.FromArgb(29, 117, 111),
                GlyphAmberColor = Color.FromArgb(141, 90, 18),
                GlyphIndigoColor = Color.FromArgb(91, 83, 183),
                GlyphCyanColor = Color.FromArgb(36, 122, 140),
                GlyphNeutralColor = Color.FromArgb(101, 113, 124),
                ChatProcessingColor = Color.FromArgb(166, 101, 13),
                ChatErrorBackColor = Color.FromArgb(252, 235, 236),
                ChatApprovalBackColor = Color.FromArgb(250, 243, 218),
                ChatCommandBackColor = Color.FromArgb(239, 237, 251),
                ChatFileChangeBackColor = Color.FromArgb(233, 245, 236),
                ChatAccountBackColor = Color.FromArgb(230, 243, 244),
                ChatSystemBackColor = Color.FromArgb(241, 241, 236),
                ChatProcessingBackColor = Color.FromArgb(250, 243, 228),
                ChatCodexBackColor = Color.FromArgb(234, 242, 249),
                ChatUserBackColor = Color.FromArgb(233, 244, 240),
                StatusReadyColor = Color.FromArgb(31, 118, 74),
                StatusProcessingColor = Color.FromArgb(151, 90, 18),
                StatusErrorColor = Color.FromArgb(181, 45, 55),
                ShadowColor = Color.FromArgb(26, 34, 29, 20),
                ShadowCornerColor = Color.FromArgb(36, 34, 29, 20),
                ShadowFadeColor = Color.FromArgb(0, 0, 0, 0)
            };
        }

        private static UiThemePalette CreateDarkPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = Color.FromArgb(7, 10, 15),
                PanelBackColor = Color.FromArgb(13, 17, 24),
                CardBackColor = Color.FromArgb(21, 27, 36),
                SubtleBackColor = Color.FromArgb(16, 22, 32),
                BorderColor = Color.FromArgb(92, 105, 125),
                ActiveBorderColor = Color.FromArgb(121, 199, 216),
                SelectedBackColor = Color.FromArgb(40, 91, 112),
                SelectedTextColor = Color.FromArgb(255, 255, 255),
                TextColor = Color.FromArgb(244, 247, 250),
                MutedTextColor = Color.FromArgb(196, 207, 219),
                DisabledTextColor = Color.FromArgb(142, 151, 163),
                AccentColor = Color.FromArgb(113, 197, 214),
                AccentSoftBackColor = Color.FromArgb(18, 41, 54),
                GlyphBlueColor = Color.FromArgb(130, 185, 255),
                GlyphRedColor = Color.FromArgb(255, 135, 157),
                GlyphGreenColor = Color.FromArgb(113, 226, 163),
                GlyphTealColor = Color.FromArgb(95, 225, 204),
                GlyphAmberColor = Color.FromArgb(255, 209, 102),
                GlyphIndigoColor = Color.FromArgb(184, 181, 255),
                GlyphCyanColor = Color.FromArgb(125, 230, 255),
                GlyphNeutralColor = Color.FromArgb(169, 183, 200),
                ChatProcessingColor = Color.FromArgb(255, 190, 92),
                ChatErrorBackColor = Color.FromArgb(59, 17, 24),
                ChatApprovalBackColor = Color.FromArgb(58, 43, 8),
                ChatCommandBackColor = Color.FromArgb(37, 33, 75),
                ChatFileChangeBackColor = Color.FromArgb(14, 52, 36),
                ChatAccountBackColor = Color.FromArgb(11, 51, 64),
                ChatSystemBackColor = Color.FromArgb(21, 27, 36),
                ChatProcessingBackColor = Color.FromArgb(59, 40, 11),
                ChatCodexBackColor = Color.FromArgb(16, 43, 74),
                ChatUserBackColor = Color.FromArgb(13, 51, 49),
                StatusReadyColor = Color.FromArgb(103, 230, 160),
                StatusProcessingColor = Color.FromArgb(255, 209, 102),
                StatusErrorColor = Color.FromArgb(255, 128, 150),
                ShadowColor = Color.FromArgb(32, 0, 0, 0),
                ShadowCornerColor = Color.FromArgb(48, 0, 0, 0),
                ShadowFadeColor = Color.FromArgb(0, 0, 0, 0)
            };
        }

        private static UiThemePalette CreateHighContrastPalette()
        {
            return new UiThemePalette
            {
                WindowBackColor = SystemColors.Control,
                PanelBackColor = SystemColors.Control,
                CardBackColor = SystemColors.Window,
                SubtleBackColor = SystemColors.Control,
                BorderColor = SystemColors.ControlDark,
                ActiveBorderColor = SystemColors.Highlight,
                SelectedBackColor = SystemColors.Highlight,
                SelectedTextColor = SystemColors.HighlightText,
                TextColor = SystemColors.ControlText,
                MutedTextColor = SystemColors.ControlText,
                DisabledTextColor = SystemColors.GrayText,
                AccentColor = SystemColors.Highlight,
                AccentSoftBackColor = SystemColors.Window,
                GlyphBlueColor = SystemColors.ControlText,
                GlyphRedColor = SystemColors.ControlText,
                GlyphGreenColor = SystemColors.ControlText,
                GlyphTealColor = SystemColors.ControlText,
                GlyphAmberColor = SystemColors.ControlText,
                GlyphIndigoColor = SystemColors.ControlText,
                GlyphCyanColor = SystemColors.ControlText,
                GlyphNeutralColor = SystemColors.ControlText,
                ChatProcessingColor = SystemColors.Highlight,
                ChatErrorBackColor = SystemColors.Window,
                ChatApprovalBackColor = SystemColors.Window,
                ChatCommandBackColor = SystemColors.Window,
                ChatFileChangeBackColor = SystemColors.Window,
                ChatAccountBackColor = SystemColors.Window,
                ChatSystemBackColor = SystemColors.Window,
                ChatProcessingBackColor = SystemColors.Window,
                ChatCodexBackColor = SystemColors.Window,
                ChatUserBackColor = SystemColors.Window,
                StatusReadyColor = SystemColors.HighlightText,
                StatusProcessingColor = SystemColors.HighlightText,
                StatusErrorColor = SystemColors.HighlightText,
                ShadowColor = SystemColors.Window,
                ShadowCornerColor = SystemColors.Window,
                ShadowFadeColor = SystemColors.Window
            };
        }
    }
}
