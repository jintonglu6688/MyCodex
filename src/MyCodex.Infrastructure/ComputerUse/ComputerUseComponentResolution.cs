using System;
using System.Collections.Generic;

namespace MyCodex.Infrastructure.ComputerUse
{
    public enum ComputerUseComponentStatus
    {
        Ready,
        UnsupportedPlatform,
        SelectedRuntimeMissing,
        UnsupportedRuntimeVersion,
        MissingComponents,
        InvalidComponents,
        UnsupportedComponentVersion,
        ConflictingComponents
    }

    public sealed class ComputerUseComponents
    {
        public string RuntimeRoot { get; internal set; }
        public string NodePath { get; internal set; }
        public string NodeReplPath { get; internal set; }
        public string NodeModulesPath { get; internal set; }
        public string SkyTransportPath { get; internal set; }
        public string HelperPath { get; internal set; }
        public string SelectedCodexPath { get; internal set; }
        public string SelectedCodexVersion { get; internal set; }
        public string RuntimeVersion { get; internal set; }
        public string NodeVersion { get; internal set; }
        public string SkyVersion { get; internal set; }
        // Only for replacing stale configuration references; never executable candidates.
        public IReadOnlyList<string> StaleOfficialRuntimeRoots { get; internal set; } = Array.Empty<string>();
    }

    public sealed class ComputerUseComponentResolution
    {
        public ComputerUseComponentStatus Status { get; internal set; }
        public ComputerUseComponents Components { get; internal set; }
        // A stable, non-secret diagnostic code, localized by the presentation layer.
        public string DetailCode { get; internal set; }
    }
}
