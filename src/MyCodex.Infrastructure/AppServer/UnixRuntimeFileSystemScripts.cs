namespace MyCodex.Infrastructure.AppServer
{
    internal static class UnixRuntimeFileSystemScripts
    {
        private const string RemoteShellPrefix = "emulate -L sh 2>/dev/null || true\n";
        private const string StrictMode = "set -eu\n";

        public static string EnsureDirectory(bool remoteShellPrefix)
        {
            return Prefix(remoteShellPrefix) +
                "target=${1:-}\n" +
                "if [ -z \"$target\" ]; then exit 64; fi\n" +
                "case \"$target\" in /*) ;; *) exit 65 ;; esac\n" +
                "mkdir -p -- \"$1\"";
        }

        private static string Prefix(bool remoteShellPrefix)
        {
            return (remoteShellPrefix ? RemoteShellPrefix : string.Empty) + StrictMode;
        }
    }
}
