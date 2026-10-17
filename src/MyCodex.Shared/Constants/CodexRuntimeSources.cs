namespace MyCodex.Shared.Constants
{
    public static class CodexRuntimeSources
    {
        public const string Bundled = "Bundled";
        public const string BundledDefaultVersion = "0.147.0";
        public const string System = "System";
        public const string Custom = "Custom";
        public const string Wsl = "Wsl";
        public const string RemoteMacSsh = "RemoteMacSsh";
        public const string RemoteUnixSsh = "RemoteUnixSsh";

        public static bool IsBundledFamily(string source)
        {
            return string.Equals(source, Bundled, global::System.StringComparison.OrdinalIgnoreCase);
        }

        public static string GetBundledRequiredVersion(string source)
        {
            return BundledDefaultVersion;
        }

        public static bool IsRemoteUnixSsh(string source)
        {
            return string.Equals(source, RemoteUnixSsh, global::System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(source, RemoteMacSsh, global::System.StringComparison.OrdinalIgnoreCase);
        }

        public static string CanonicalizeForExecution(string source)
        {
            var canonicalSource = CanonicalizeForPersistence(source);
            return IsRemoteUnixSsh(canonicalSource) ? RemoteUnixSsh : canonicalSource;
        }

        public static string CanonicalizeForPersistence(string source)
        {
            if (string.Equals(source, System, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return System;
            }

            if (string.Equals(source, Custom, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return Custom;
            }

            if (string.Equals(source, Wsl, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return Wsl;
            }

            if (string.Equals(source, RemoteMacSsh, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return RemoteMacSsh;
            }

            if (string.Equals(source, RemoteUnixSsh, global::System.StringComparison.OrdinalIgnoreCase))
            {
                return RemoteUnixSsh;
            }

            return Bundled;
        }
    }
}
