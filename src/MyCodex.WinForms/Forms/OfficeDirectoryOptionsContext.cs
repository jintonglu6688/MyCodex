using System;
using System.Windows.Forms;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal sealed class OfficeDirectoryOptionsContext
    {
        public string RuntimeDisplayName { get; set; }

        public AppServerHostPlatform HostPlatform { get; set; }

        public string ConfiguredRootPath { get; set; }

        public string EffectiveRootPath { get; set; }

        public bool ModeSettingsAvailable { get; set; } = true;

        public string ModeSettingsError { get; set; }

        public Func<IWin32Window, string, string> BrowseDirectory { get; set; }
    }
}
