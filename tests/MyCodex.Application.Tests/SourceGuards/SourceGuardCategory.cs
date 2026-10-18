namespace MyCodex.Application.Tests.SourceGuards
{
    /// <summary>
    /// NUnit categories for tests that inspect repository files instead of exercising behavior.
    /// Run one group with: dotnet test ... --filter "TestCategory=SourceGuard.C".
    /// </summary>
    internal static class SourceGuardCategory
    {
        /// <summary>Confirms that retired identifiers, files, or features stay removed.</summary>
        public const string Retired = "SourceGuard.A";

        /// <summary>Pins code to a specific file, type, or size budget.</summary>
        public const string Placement = "SourceGuard.B";

        /// <summary>Uses source text as a stand-in for runtime behavior; prefer a behavior test when the area changes.</summary>
        public const string SourceBehavior = "SourceGuard.C";

        /// <summary>Enforces layer boundaries, security, privacy, and accessibility rules across the codebase.</summary>
        public const string Architecture = "SourceGuard.D";

        /// <summary>Checks scripts, project files, resources, manuals, and packaging assets.</summary>
        public const string Assets = "SourceGuard.E";
    }
}
