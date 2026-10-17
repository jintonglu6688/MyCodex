namespace MyCodex.Infrastructure.AppServer
{
    internal static class UnixFileSystemBridgeScripts
    {
        public const string ResolveHomeDirectoryScript = "printf '%s\\n' \"$HOME\"";
        public const string DirectoryExistsScript = "test -d \"$1\"";
        public const string FileExistsScript = "test -f \"$1\"";

        public const string GitInfoScript =
            "workspace_path=${1:-}\n" +
            "if [ -z \"$workspace_path\" ]; then exit 64; fi\n" +
            "if ! git -C \"$workspace_path\" rev-parse --is-inside-work-tree >/dev/null 2>&1; then exit 2; fi\n" +
            "branch=$(git -C \"$workspace_path\" symbolic-ref --quiet --short HEAD 2>/dev/null || true)\n" +
            "sha=$(git -C \"$workspace_path\" rev-parse --verify HEAD 2>/dev/null || true)\n" +
            "if [ -z \"$branch\" ] && [ -n \"$sha\" ]; then short_sha=$(printf '%s' \"$sha\" | cut -c 1-8); branch=\"detached@$short_sha\"; fi\n" +
            "origin=$(git -C \"$workspace_path\" config --get remote.origin.url 2>/dev/null || true)\n" +
            "printf 'branch=%s\\n' \"$branch\"\n" +
            "printf 'sha=%s\\n' \"$sha\"\n" +
            "printf 'origin=%s\\n' \"$origin\"";

        public const string GitStatusScript =
            "workspace_path=${1:-}\n" +
            "if [ -z \"$workspace_path\" ]; then exit 64; fi\n" +
            "if ! git -C \"$workspace_path\" rev-parse --is-inside-work-tree >/dev/null 2>&1; then exit 2; fi\n" +
            "git -C \"$workspace_path\" status --short -z --untracked-files=all --no-renames";

        public const string GitStagedDiffScript =
            "git -C \"$1\" diff --cached --no-ext-diff --binary -- \"$2\" 2>/dev/null || true";

        public const string GitUnstagedDiffScript =
            "git -C \"$1\" diff --no-ext-diff --binary -- \"$2\" 2>/dev/null || true";
    }
}
