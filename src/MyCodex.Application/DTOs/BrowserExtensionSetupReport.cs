using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public enum BrowserExtensionBrowser { Edge, Chrome }
    public enum BrowserExtensionSetupPage { Store, Extensions }
    public enum BrowserExtensionInstallState { NotInstalled, Enabled, Disabled, Unknown }
    public enum BrowserExtensionBridgeState { Missing, Registered, Invalid, Unknown }
    public enum BrowserAutomationToolState { Unknown, Missing, Disabled, Configured, Ready }

    public sealed class BrowserExtensionProfileStatus
    {
        public string ProfileDirectory { get; set; }
        public BrowserExtensionInstallState State { get; set; }
    }

    public sealed class BrowserExtensionSetupReport
    {
        public bool? BrowserInstalled { get; set; }
        public BrowserExtensionBridgeState Bridge { get; set; }
        public BrowserAutomationToolState Tools { get; set; }
        public List<BrowserExtensionProfileStatus> Profiles { get; } = new List<BrowserExtensionProfileStatus>();
    }
}
