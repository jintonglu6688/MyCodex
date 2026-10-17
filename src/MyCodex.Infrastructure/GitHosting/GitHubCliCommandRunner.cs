using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.GitHosting
{
    internal interface IGitHubCliCommandRunner
    {
        Task<GitHubCliCommandResult> RunAsync(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken);

        GitCloneResult CloneRepository(
            GitHubRepositoryEntry repository,
            string targetParentDirectory,
            string targetFolderName,
            bool recurseSubmodules,
            bool useSshUrl,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken);

        void StartInteractiveLogin();
    }

    internal sealed class GitHubCliCommandResult
    {
        public const int NotFoundExitCode = -9009;

        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }

        public bool Success
        {
            get { return ExitCode == 0; }
        }
    }

    internal static class GitHubCliArgumentParser
    {
        public static string[] Split(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return new string[0];
            }

            var result = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < arguments.Length; i++)
            {
                var ch = arguments[i];
                if (ch == '\\' && i + 1 < arguments.Length && arguments[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Length = 0;
                    }

                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0 || inQuotes)
            {
                result.Add(current.ToString());
            }

            return result.ToArray();
        }

        public static string BuildRepoCloneArguments(string repositoryArgument, string folderName, bool recurseSubmodules)
        {
            var arguments = new System.Collections.Generic.List<string>
            {
                "repo",
                "clone",
                repositoryArgument,
                folderName,
                "--",
                "--progress"
            };
            if (recurseSubmodules)
            {
                arguments.Add("--recurse-submodules");
            }

            return GitCommandLineBuilder.BuildArguments(arguments);
        }
    }

    internal sealed class LocalGitHubCliCommandRunner : IGitHubCliCommandRunner
    {
        public Task<GitHubCliCommandResult> RunAsync(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            return Task.Run(() => Run(arguments, timeoutMilliseconds, cancellationToken), cancellationToken);
        }

        public GitCloneResult CloneRepository(
            GitHubRepositoryEntry repository,
            string targetParentDirectory,
            string targetFolderName,
            bool recurseSubmodules,
            bool useSshUrl,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            var repositoryArgument = useSshUrl && !string.IsNullOrWhiteSpace(repository.SshUrl)
                ? repository.SshUrl.Trim()
                : NormalizeRequired(repository.NameWithOwner, "repository");
            var parentDirectory = NormalizeRequired(targetParentDirectory, "targetParentDirectory");
            var folderName = NormalizeRequired(targetFolderName, "targetFolderName");
            ValidateFolderName(folderName);

            if (!Directory.Exists(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            var targetPath = Path.Combine(parentDirectory, folderName);
            if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                throw new InvalidOperationException(SharedLocalization.Text("GitCloneService.Error.TargetExists", "The target folder already exists and is not empty."));
            }

            return RunCloneProcess(parentDirectory, targetPath, GitHubCliArgumentParser.BuildRepoCloneArguments(repositoryArgument, folderName, recurseSubmodules), progress, cancellationToken);
        }

        public void StartInteractiveLogin()
        {
            var arguments = "/c \"gh auth login --hostname github.com --git-protocol https --web --scopes repo,workflow,gist,read:org & pause\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }

        private static GitHubCliCommandResult Run(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
                {
                    StartInfo = startInfo,
                    TimeoutMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : 15000,
                    TimeoutMessage = SharedLocalization.Text("GitHubCli.Error.Timeout", "GitHub CLI command timed out."),
                    CancellationToken = cancellationToken,
                    OutputTransform = GitOutputSanitizer.RedactSensitiveText
                });
                return new GitHubCliCommandResult
                {
                    ExitCode = result.ExitCode,
                    StandardOutput = result.StandardOutput,
                    StandardError = result.StandardError
                };
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                return new GitHubCliCommandResult
                {
                    ExitCode = GitHubCliCommandResult.NotFoundExitCode,
                    StandardError = GitOutputSanitizer.RedactSensitiveText(ex.Message)
                };
            }
        }

        private static GitCloneResult RunCloneProcess(
            string parentDirectory,
            string targetPath,
            string arguments,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = arguments,
                    WorkingDirectory = parentDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process.OutputDataReceived += (sender, e) =>
                {
                    AppendCloneLine(output, e.Data);
                    ReportCloneProgress(progress, e.Data, false);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    AppendCloneLine(error, e.Data);
                    ReportCloneProgress(progress, e.Data, true);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(200))
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }

                    Kill(process);
                    throw new OperationCanceledException(cancellationToken);
                }

                process.WaitForExit();

                return new GitCloneResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    TargetPath = targetPath,
                    StandardOutput = output.ToString(),
                    StandardError = error.ToString()
                };
            }
        }

        private static void AppendCloneLine(StringBuilder builder, string text)
        {
            if (builder == null || text == null)
            {
                return;
            }

            builder.AppendLine(GitOutputSanitizer.RedactSensitiveText(text));
        }

        private static void ReportCloneProgress(IProgress<GitCloneProgress> progress, string text, bool isError)
        {
            if (progress == null || text == null)
            {
                return;
            }

            progress.Report(new GitCloneProgress
            {
                Text = GitOutputSanitizer.RedactSensitiveText(text),
                IsError = isError
            });
        }

        private static string NormalizeRequired(string value, string argumentName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(argumentName + " cannot be empty.", argumentName);
            }

            return value.Trim();
        }

        private static void ValidateFolderName(string folderName)
        {
            if (string.Equals(folderName, ".", StringComparison.Ordinal) ||
                string.Equals(folderName, "..", StringComparison.Ordinal) ||
                folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(SharedLocalization.Text("GitCloneService.Error.InvalidTargetFolder", "Target folder name contains invalid characters."));
            }
        }

        private static void Kill(Process process)
        {
            MyCodex.Infrastructure.Processes.ProcessTreeTerminator.TryKill(process);
        }
    }

    internal sealed class RemoteSshGitHubCliCommandRunner : IGitHubCliCommandRunner
    {
        private const int CloneTimeoutMilliseconds = 30 * 60 * 1000;

        private const string GhScript =
            "set -u\n" +
            "if ! command -v gh >/dev/null 2>&1; then echo 'GitHub CLI is not installed on the remote SSH host.' >&2; exit 127; fi\n" +
            "gh \"$@\"";

        private const string GhCloneScript =
            "set -u\n" +
            "parent=${1:-}\n" +
            "folder=${2:-}\n" +
            "if [ -z \"$parent\" ]; then exit 64; fi\n" +
            "shift 2\n" +
            "case \"$parent\" in '~') parent=\"$HOME\" ;; '~/'*) parent=\"$HOME/${parent#~/}\" ;; esac\n" +
            "mkdir -p \"$parent\"\n" +
            "cd \"$parent\"\n" +
            "if ! command -v gh >/dev/null 2>&1; then echo 'GitHub CLI is not installed on the remote SSH host.' >&2; exit 127; fi\n" +
            "gh \"$@\"\n" +
            "exit_code=$?\n" +
            "if [ \"$exit_code\" -eq 0 ] && [ -n \"$folder\" ]; then printf '\\nMYCODEX_CLONE_TARGET\\t%s/%s\\n' \"$parent\" \"$folder\"; fi\n" +
            "exit \"$exit_code\"";

        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly IRemoteSshCommandExecutor _executor;

        public RemoteSshGitHubCliCommandRunner(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteSshGitHubCliCommandRunner(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<GitHubCliCommandResult> RunAsync(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _executor.ExecuteAsync(
                _config,
                GhScript,
                "mycodex-remote-gh",
                timeoutMilliseconds,
                cancellationToken,
                GitHubCliArgumentParser.Split(arguments)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertResult(result);
        }

        public GitCloneResult CloneRepository(
            GitHubRepositoryEntry repository,
            string targetParentDirectory,
            string targetFolderName,
            bool recurseSubmodules,
            bool useSshUrl,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            var repositoryArgument = useSshUrl && !string.IsNullOrWhiteSpace(repository.SshUrl)
                ? repository.SshUrl.Trim()
                : UnixGitCloneScript.NormalizeRequired(repository.NameWithOwner, "repository");
            var parentDirectory = UnixGitCloneScript.NormalizeRequired(targetParentDirectory, "targetParentDirectory");
            var folderName = UnixGitCloneScript.NormalizeRequired(targetFolderName, "targetFolderName");
            UnixGitCloneScript.ValidateFolderName(folderName);
            var arguments = GitHubCliArgumentParser.BuildRepoCloneArguments(repositoryArgument, folderName, recurseSubmodules);
            var commandArguments = new[] { parentDirectory, folderName }
                .Concat(GitHubCliArgumentParser.Split(arguments))
                .ToArray();

            string streamedTargetPath = null;
            var streamingExecutor = _executor as IRemoteSshStreamingCommandExecutor;
            var result = streamingExecutor != null
                ? streamingExecutor.ExecuteStreamingAsync(
                    _config,
                    GhCloneScript,
                    "mycodex-remote-gh-clone",
                    CloneTimeoutMilliseconds,
                    cancellationToken,
                    (line, isError) =>
                    {
                        var target = ReportStreamingCloneProgress(progress, line, isError);
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            streamedTargetPath = target;
                        }
                    },
                    commandArguments).GetAwaiter().GetResult()
                : _executor.ExecuteAsync(
                    _config,
                    GhCloneScript,
                    "mycodex-remote-gh-clone",
                    CloneTimeoutMilliseconds,
                    cancellationToken,
                    commandArguments).GetAwaiter().GetResult();

            string targetPath;
            var standardOutput = UnixGitCloneScript.ExtractCloneTargetPath(result.StandardOutput, out targetPath);
            if (streamingExecutor == null)
            {
                UnixGitCloneScript.ReportBufferedProgress(progress, standardOutput, false);
                UnixGitCloneScript.ReportBufferedProgress(progress, result.StandardError, true);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = streamedTargetPath;
            }

            return new GitCloneResult
            {
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode == 127 ? GitHubCliCommandResult.NotFoundExitCode : result.ExitCode,
                TargetPath = string.IsNullOrWhiteSpace(targetPath) ? UnixGitCloneScript.CombineUnixPath(parentDirectory, folderName) : targetPath,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(standardOutput ?? string.Empty),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError ?? string.Empty)
            };
        }

        public void StartInteractiveLogin()
        {
            throw new NotSupportedException(SharedLocalization.Text("GitHubCli.Error.RemoteInteractiveLoginUnsupported", "Interactive GitHub CLI login is not supported for remote SSH runtime."));
        }

        private static GitHubCliCommandResult ConvertResult(RemoteSshCommandExecutionResult result)
        {
            result = result ?? new RemoteSshCommandExecutionResult();
            return new GitHubCliCommandResult
            {
                ExitCode = result.ExitCode == 127 ? GitHubCliCommandResult.NotFoundExitCode : result.ExitCode,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(result.StandardOutput ?? string.Empty),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError ?? string.Empty)
            };
        }

        private static string ReportStreamingCloneProgress(
            IProgress<GitCloneProgress> progress,
            string line,
            bool isError)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            if (line.StartsWith("MYCODEX_CLONE_TARGET\t", StringComparison.Ordinal))
            {
                return line.Substring("MYCODEX_CLONE_TARGET\t".Length).Trim();
            }

            if (progress == null)
            {
                return null;
            }

            progress.Report(new GitCloneProgress
            {
                Text = GitOutputSanitizer.RedactSensitiveText(line),
                IsError = isError
            });

            return null;
        }
    }

    internal sealed class WslGitHubCliCommandRunner : IGitHubCliCommandRunner
    {
        private const string GhScript =
            "set -u\n" +
            "if ! command -v gh >/dev/null 2>&1; then echo 'GitHub CLI is not installed in WSL.' >&2; exit 127; fi\n" +
            "gh \"$@\"";

        private const string GhCloneScript =
            "set -u\n" +
            "parent=${1:-}\n" +
            "folder=${2:-}\n" +
            "if [ -z \"$parent\" ]; then exit 64; fi\n" +
            "shift 2\n" +
            "case \"$parent\" in '~') parent=\"$HOME\" ;; '~/'*) parent=\"$HOME/${parent#~/}\" ;; esac\n" +
            "mkdir -p \"$parent\"\n" +
            "cd \"$parent\"\n" +
            "if ! command -v gh >/dev/null 2>&1; then echo 'GitHub CLI is not installed in WSL.' >&2; exit 127; fi\n" +
            "gh \"$@\"\n" +
            "exit_code=$?\n" +
            "if [ \"$exit_code\" -eq 0 ] && [ -n \"$folder\" ]; then printf '\\nMYCODEX_CLONE_TARGET\\t%s/%s\\n' \"$parent\" \"$folder\"; fi\n" +
            "exit \"$exit_code\"";

        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;

        public WslGitHubCliCommandRunner(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitHubCliCommandRunner(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<GitHubCliCommandResult> RunAsync(string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _executor.ExecuteAsync(
                _config,
                GhScript,
                "mycodex-wsl-gh",
                timeoutMilliseconds,
                cancellationToken,
                GitHubCliArgumentParser.Split(arguments)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertResult(result);
        }

        public GitCloneResult CloneRepository(
            GitHubRepositoryEntry repository,
            string targetParentDirectory,
            string targetFolderName,
            bool recurseSubmodules,
            bool useSshUrl,
            IProgress<GitCloneProgress> progress,
            CancellationToken cancellationToken)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            var repositoryArgument = useSshUrl && !string.IsNullOrWhiteSpace(repository.SshUrl)
                ? repository.SshUrl.Trim()
                : UnixGitCloneScript.NormalizeRequired(repository.NameWithOwner, "repository");
            var parentDirectory = UnixGitCloneScript.NormalizeRequired(targetParentDirectory, "targetParentDirectory");
            var folderName = UnixGitCloneScript.NormalizeRequired(targetFolderName, "targetFolderName");
            UnixGitCloneScript.ValidateFolderName(folderName);
            var arguments = GitHubCliArgumentParser.BuildRepoCloneArguments(repositoryArgument, folderName, recurseSubmodules);
            var commandArguments = new[] { parentDirectory, folderName }
                .Concat(GitHubCliArgumentParser.Split(arguments))
                .ToArray();
            var result = _executor.ExecuteAsync(
                _config,
                GhCloneScript,
                "mycodex-wsl-gh-clone",
                30 * 60 * 1000,
                cancellationToken,
                commandArguments).GetAwaiter().GetResult();

            string targetPath;
            var standardOutput = UnixGitCloneScript.ExtractCloneTargetPath(result.StandardOutput, out targetPath);
            UnixGitCloneScript.ReportBufferedProgress(progress, standardOutput, false);
            UnixGitCloneScript.ReportBufferedProgress(progress, result.StandardError, true);

            return new GitCloneResult
            {
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode == 127 ? GitHubCliCommandResult.NotFoundExitCode : result.ExitCode,
                TargetPath = string.IsNullOrWhiteSpace(targetPath) ? UnixGitCloneScript.CombineUnixPath(parentDirectory, folderName) : targetPath,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(standardOutput ?? string.Empty),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError ?? string.Empty)
            };
        }

        public void StartInteractiveLogin()
        {
            var arguments = "/d /c wsl.exe " + WslCommandLine.BuildExecArguments(
                _config.DistributionName,
                "gh",
                "auth",
                "login",
                "--hostname",
                "github.com",
                "--git-protocol",
                "https",
                "--web",
                "--scopes",
                "repo,workflow,gist,read:org") + " & pause";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }

        private static GitHubCliCommandResult ConvertResult(WslCommandExecutionResult result)
        {
            result = result ?? new WslCommandExecutionResult();
            return new GitHubCliCommandResult
            {
                ExitCode = result.ExitCode == 127 ? GitHubCliCommandResult.NotFoundExitCode : result.ExitCode,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(result.StandardOutput ?? string.Empty),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError ?? string.Empty)
            };
        }
    }
}
