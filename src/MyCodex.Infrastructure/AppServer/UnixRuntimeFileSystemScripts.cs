namespace MyCodex.Infrastructure.AppServer
{
    internal enum UnixRuntimeRenameParentMode
    {
        Dirname = 0,
        ParameterExpansion = 1
    }

    internal static class UnixRuntimeFileSystemScripts
    {
        private const string RemoteShellPrefix = "emulate -L sh 2>/dev/null || true\n";
        private const string StrictMode = "set -eu\n";
        private const string NamedInput =
            "parent=${1:-}\n" +
            "name=${2:-}\n" +
            "if [ -z \"$parent\" ] || [ -z \"$name\" ]; then exit 64; fi\n" +
            "case \"$parent\" in /*) ;; *) exit 65 ;; esac\n" +
            "case \"$name\" in */*|*\\\\*|.|..) exit 65 ;; esac\n" +
            "[ ! -e \"$parent/$name\" ] && [ ! -L \"$parent/$name\" ] || exit 73\n";

        public static string EnsureDirectory(bool remoteShellPrefix)
        {
            return Prefix(remoteShellPrefix) +
                "target=${1:-}\n" +
                "if [ -z \"$target\" ]; then exit 64; fi\n" +
                "case \"$target\" in /*) ;; *) exit 65 ;; esac\n" +
                "mkdir -p -- \"$1\"";
        }

        public static string CreateDirectory(bool remoteShellPrefix)
        {
            return Prefix(remoteShellPrefix) + NamedInput + "mkdir -- \"$parent/$name\"";
        }

        public static string CreateFile(bool remoteShellPrefix)
        {
            return Prefix(remoteShellPrefix) + NamedInput + ": > \"$parent/$name\"";
        }

        public static string Rename(bool remoteShellPrefix, UnixRuntimeRenameParentMode parentMode)
        {
            return Prefix(remoteShellPrefix) +
                "target=${1:-}\n" +
                "name=${2:-}\n" +
                "if [ -z \"$target\" ] || [ -z \"$name\" ]; then exit 64; fi\n" +
                "case \"$target\" in /*) ;; *) exit 65 ;; esac\n" +
                "case \"$name\" in */*|*\\\\*|.|..) exit 65 ;; esac\n" +
                BuildRenameParent(parentMode) +
                "[ ! -e \"$parent/$name\" ] && [ ! -L \"$parent/$name\" ] || exit 73\n" +
                "mv -- \"$target\" \"$parent/$name\"";
        }

        public static string Delete(bool remoteShellPrefix)
        {
            return Prefix(remoteShellPrefix) +
                "target=${1:-}\n" +
                "mode=${2:-}\n" +
                "if [ -z \"$target\" ]; then exit 64; fi\n" +
                "case \"$target\" in /*) ;; *) exit 65 ;; esac\n" +
                "case \"$target\" in /|/.) exit 65 ;; esac\n" +
                "case \"$target\" in */../*|*/..|*/./*|*/.) exit 65 ;; esac\n" +
                "[ -e \"$target\" ] || [ -L \"$target\" ] || exit 66\n" +
                "if [ -d \"$target\" ]; then\n" +
                "  if [ \"$mode\" = \"recursive\" ]; then rm -rf -- \"$target\"; else rmdir -- \"$target\"; fi\n" +
                "else\n" +
                "  rm -f -- \"$target\"\n" +
                "fi";
        }

        private static string Prefix(bool remoteShellPrefix)
        {
            return (remoteShellPrefix ? RemoteShellPrefix : string.Empty) + StrictMode;
        }

        private static string BuildRenameParent(UnixRuntimeRenameParentMode parentMode)
        {
            return parentMode == UnixRuntimeRenameParentMode.ParameterExpansion
                ? "parent=${target%/*}\nif [ -z \"$parent\" ]; then parent=/; fi\n"
                : "parent=$(dirname -- \"$target\")\n";
        }
    }
}
