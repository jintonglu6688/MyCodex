using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.Persistence
{
    internal interface IRuntimeScopedConfigFileStore
    {
        bool IsRuntimeEndpointStorageAvailableSynchronously(AppConfigSnapshot config);

        bool TryGetRuntimeAppStateFilePathForAsyncAccess(AppConfigSnapshot config, out string path);

        bool TryGetRuntimeModeSettingsFilePathForAsyncAccess(AppConfigSnapshot config, out string path);


        bool ReadTextFile(AppConfigSnapshot config, string path, out string content);

        Task<Tuple<bool, string>> ReadTextFileAsync(AppConfigSnapshot config, string path);

        void WriteTextFile(AppConfigSnapshot config, string path, string content);

        Task WriteTextFileAsync(AppConfigSnapshot config, string path, string content);

        void DeleteFile(AppConfigSnapshot config, string path);

        Task DeleteFileAsync(AppConfigSnapshot config, string path);
    }

    internal sealed class RuntimeScopedConfigFileStore : IRuntimeScopedConfigFileStore
    {
        private const int TextFileTimeoutMilliseconds = 20000;
        private const int WriteFileTimeoutMilliseconds = 30000;
        private const int DeleteFileTimeoutMilliseconds = 20000;
        private const int RemoteReadAttempts = 4;
        private const int RemoteReadRetryDelayMilliseconds = 500;
        private const string MyCodexConfigDirectory = "~/.codex/mycodex";
        private const string AppStateFilePath = MyCodexConfigDirectory + "/app-state.json";
        private const string ModeSettingsFilePath = MyCodexConfigDirectory + "/mode-settings.json";

        private const string ReadTextFileScript =
            "set -eu\n" +
            "original_path=${1:-}\n" +
            "target_file=$(resolve_path \"$original_path\")\n" +
            "if [ ! -f \"$target_file\" ]; then\n" +
            "  if [ -e \"$target_file\" ] || [ -L \"$target_file\" ]; then exit 65; fi\n" +
            "  if ! validate_searchable_parent \"$target_file\"; then exit 65; fi\n" +
            "  legacy_file=$(resolve_legacy_literal_tilde_path \"$original_path\")\n" +
            "  if [ -n \"$legacy_file\" ] && [ -f \"$legacy_file\" ]; then\n" +
            "    mkdir -p \"${target_file%/*}\"\n" +
            "    cp \"$legacy_file\" \"$target_file\"\n" +
            "    chmod 600 \"$target_file\" 2>/dev/null || true\n" +
            "  else\n" +
            "    exit 66\n" +
            "  fi\n" +
            "fi\n" +
            "cat \"$target_file\"";

        private const string WriteTextFileScript =
            "set -eu\n" +
            "target_file=$(resolve_path \"$1\")\n" +
            "target_dir=${target_file%/*}\n" +
            "mkdir -p \"$target_dir\"\n" +
            "tmp_file=\"$target_file.tmp.$$\"\n" +
            "trap 'rm -f \"$tmp_file\"' EXIT\n" +
            "umask 077\n" +
            "cat > \"$tmp_file\"\n" +
            "received_bytes=$(wc -c < \"$tmp_file\")\n" +
            "if ! [ \"$received_bytes\" -eq \"$2\" ]; then\n" +
            "  printf '%s\\n' 'Runtime endpoint write was incomplete.' >&2\n" +
            "  exit 65\n" +
            "fi\n" +
            "chmod 600 \"$tmp_file\" 2>/dev/null || true\n" +
            "mv -f \"$tmp_file\" \"$target_file\"";

        private const string DeleteFileScript =
            "set -eu\n" +
            "target_file=$(resolve_path \"$1\")\n" +
            "rm -f \"$target_file\"";

        private static readonly string ScriptPrelude =
            "resolve_path() {\n" +
            "  p=${1:-}\n" +
            "  if [ \"$p\" = \"~\" ]; then\n" +
            "    printf '%s\\n' \"$HOME\"\n" +
            "    return\n" +
            "  fi\n" +
            "  tail=${p#??}\n" +
            "  head=${p%\"$tail\"}\n" +
            "  if [ \"$head\" = \"~/\" ]; then\n" +
            "    printf '%s\\n' \"$HOME/$tail\"\n" +
            "  else\n" +
            "    printf '%s\\n' \"$p\"\n" +
            "  fi\n" +
            "}\n" +
            "resolve_legacy_literal_tilde_path() {\n" +
            "  p=${1:-}\n" +
            "  tail=${p#??}\n" +
            "  head=${p%\"$tail\"}\n" +
            "  if [ \"$head\" = \"~/\" ]; then\n" +
            "    printf '%s\\n' \"$HOME/~/$tail\"\n" +
            "  else\n" +
            "    printf '\\n'\n" +
            "  fi\n" +
            "}\n" +
            "validate_searchable_parent() {\n" +
            "  current=${1%/*}\n" +
            "  while [ -n \"$current\" ] && [ \"$current\" != \"/\" ]; do\n" +
            "    if [ -e \"$current\" ] || [ -L \"$current\" ]; then\n" +
            "      if [ ! -d \"$current\" ] || [ ! -x \"$current\" ]; then return 1; fi\n" +
            "      return 0\n" +
            "    fi\n" +
            "    parent=${current%/*}\n" +
            "    if [ \"$parent\" = \"$current\" ]; then break; fi\n" +
            "    current=$parent\n" +
            "  done\n" +
            "  return 0\n" +
            "}\n";

        private readonly IWslCommandExecutor _wslExecutor;
        private readonly IRemoteSshCommandExecutor _remoteExecutor;
        private readonly string _appStateFilePath;

        public RuntimeScopedConfigFileStore()
            : this(new WslCommandExecutor(), new RemoteSshCommandExecutor(), AppStateFilePath)
        {
        }

        internal RuntimeScopedConfigFileStore(IWslCommandExecutor wslExecutor, IRemoteSshCommandExecutor remoteExecutor)
            : this(wslExecutor, remoteExecutor, AppStateFilePath)
        {
        }

        internal RuntimeScopedConfigFileStore(string appStateFilePath)
            : this(new WslCommandExecutor(), new RemoteSshCommandExecutor(), appStateFilePath)
        {
        }

        internal RuntimeScopedConfigFileStore(
            IWslCommandExecutor wslExecutor,
            IRemoteSshCommandExecutor remoteExecutor,
            string appStateFilePath)
        {
            _wslExecutor = wslExecutor ?? throw new ArgumentNullException(nameof(wslExecutor));
            _remoteExecutor = remoteExecutor ?? throw new ArgumentNullException(nameof(remoteExecutor));
            _appStateFilePath = string.IsNullOrWhiteSpace(appStateFilePath)
                ? throw new ArgumentException("Runtime app-state path cannot be empty.", nameof(appStateFilePath))
                : appStateFilePath;
        }

        public bool IsRuntimeEndpointStorageAvailableSynchronously(AppConfigSnapshot config)
        {
            return false;
        }

        public bool TryGetRuntimeModeSettingsFilePathForAsyncAccess(AppConfigSnapshot config, out string path)
        {
            path = null;
            if (!IsWsl(config) && !IsRemoteUnix(config))
            {
                return false;
            }

            path = ModeSettingsFilePath;
            return true;
        }

        public bool TryGetRuntimeAppStateFilePathForAsyncAccess(AppConfigSnapshot config, out string path)
        {
            return TryGetRuntimePath(config, _appStateFilePath, out path);
        }

        public bool ReadTextFile(AppConfigSnapshot config, string path, out string content)
        {
            content = null;
            var result = Execute(config, ReadTextFileScript, "mycodex-read-runtime-config", TextFileTimeoutMilliseconds, path);
            if (result.ExitCode == 0)
            {
                content = result.StandardOutput ?? string.Empty;
                return true;
            }

            if (result.ExitCode == 66)
            {
                return false;
            }

            throw BuildRuntimeCommandException(result);
        }

        public async Task<Tuple<bool, string>> ReadTextFileAsync(AppConfigSnapshot config, string path)
        {
            var result = await ExecuteAsync(config, ReadTextFileScript, "mycodex-read-runtime-config", TextFileTimeoutMilliseconds, path).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return Tuple.Create(true, result.StandardOutput ?? string.Empty);
            }

            if (result.ExitCode == 66)
            {
                return Tuple.Create(false, (string)null);
            }

            throw BuildRuntimeCommandException(result);
        }

        public void WriteTextFile(AppConfigSnapshot config, string path, string content)
        {
            WriteTextFileAsync(config, path, content).GetAwaiter().GetResult();
        }

        public async Task WriteTextFileAsync(AppConfigSnapshot config, string path, string content)
        {
            content = content ?? string.Empty;
            var byteCount = Encoding.UTF8.GetByteCount(content).ToString(CultureInfo.InvariantCulture);
            var result = await ExecuteAsync(config, WriteTextFileScript, "mycodex-write-runtime-config",
                WriteFileTimeoutMilliseconds, content, new[] { path, byteCount }).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw BuildRuntimeCommandException(result);
            }
        }

        public void DeleteFile(AppConfigSnapshot config, string path)
        {
            var result = Execute(config, DeleteFileScript, "mycodex-delete-runtime-config", DeleteFileTimeoutMilliseconds, path);
            if (result.ExitCode != 0)
            {
                throw BuildRuntimeCommandException(result);
            }
        }

        public async Task DeleteFileAsync(AppConfigSnapshot config, string path)
        {
            var result = await ExecuteAsync(config, DeleteFileScript, "mycodex-delete-runtime-config", DeleteFileTimeoutMilliseconds, path).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw BuildRuntimeCommandException(result);
            }
        }

        private static bool TryGetRuntimePath(AppConfigSnapshot config, string path, out string runtimePath)
        {
            runtimePath = null;
            if (!UsesRuntimeEndpointStorage(config))
            {
                return false;
            }

            runtimePath = path;
            return true;
        }

        private RuntimeCommandResult Execute(AppConfigSnapshot config, string script, string operationName, int timeoutMilliseconds, params string[] arguments)
        {
            return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments).GetAwaiter().GetResult();
        }

        private Task<RuntimeCommandResult> ExecuteAsync(AppConfigSnapshot config, string script, string operationName, int timeoutMilliseconds, params string[] arguments)
        {
            return ExecuteAsync(config, script, operationName, timeoutMilliseconds, null, arguments);
        }

        private async Task<RuntimeCommandResult> ExecuteAsync(AppConfigSnapshot config, string script, string operationName,
            int timeoutMilliseconds, string standardInput, string[] arguments)
        {
            if (IsWsl(config))
            {
                var result = standardInput == null
                    ? await _wslExecutor.ExecuteAsync(config.WslAppServer, ScriptPrelude + script,
                        operationName, timeoutMilliseconds, arguments).ConfigureAwait(false)
                    : await _wslExecutor.ExecuteWithInputAsync(config.WslAppServer, ScriptPrelude + script,
                        operationName, timeoutMilliseconds, CancellationToken.None, standardInput, arguments).ConfigureAwait(false);
                return new RuntimeCommandResult(result.ExitCode, result.StandardOutput, result.StandardError);
            }

            if (IsRemoteUnix(config))
            {
                for (var attempt = 1; attempt <= RemoteReadAttempts; attempt++)
                {
                    var result = standardInput == null
                        ? await _remoteExecutor.ExecuteAsync(config.GetSelectedRemoteUnixSshConfig(), ScriptPrelude + script,
                            operationName, timeoutMilliseconds, arguments).ConfigureAwait(false)
                        : await _remoteExecutor.ExecuteWithInputAsync(config.GetSelectedRemoteUnixSshConfig(), ScriptPrelude + script,
                            operationName, timeoutMilliseconds, CancellationToken.None, standardInput, arguments).ConfigureAwait(false);
                    if (attempt == RemoteReadAttempts || !ShouldRetryRemoteRead(operationName, result))
                    {
                        return new RuntimeCommandResult(result.ExitCode, result.StandardOutput, result.StandardError);
                    }

                    await Task.Delay(RemoteReadRetryDelayMilliseconds).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Current runtime does not use runtime endpoint storage.");
        }

        private static bool ShouldRetryRemoteRead(string operationName, RemoteSshCommandExecutionResult result)
        {
            if (!string.Equals(operationName, "mycodex-read-runtime-config", StringComparison.Ordinal) ||
                result == null || result.ExitCode != 255 || string.IsNullOrWhiteSpace(result.StandardError))
            {
                return false;
            }

            return result.StandardError.IndexOf("Exceeded MaxStartups", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.StandardError.IndexOf("Connection closed by", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.StandardError.IndexOf("kex_exchange_identification", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool UsesRuntimeEndpointStorage(AppConfigSnapshot config)
        {
            return IsWslDefaultHome(config) || IsRemoteUnix(config);
        }

        private static bool IsWslDefaultHome(AppConfigSnapshot config)
        {
            return IsWsl(config);
        }

        private static bool IsWsl(AppConfigSnapshot config)
        {
            return config != null &&
                string.Equals(config.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRemoteUnix(AppConfigSnapshot config)
        {
            return config != null && CodexRuntimeSources.IsRemoteUnixSsh(config.CodexRuntimeSource);
        }

        private static InvalidOperationException BuildRuntimeCommandException(RuntimeCommandResult result)
        {
            var message = result != null && !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : "Runtime endpoint command failed.";
            return new InvalidOperationException(message);
        }

        private sealed class RuntimeCommandResult
        {
            public RuntimeCommandResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; private set; }

            public string StandardOutput { get; private set; }

            public string StandardError { get; private set; }
        }
    }
}
