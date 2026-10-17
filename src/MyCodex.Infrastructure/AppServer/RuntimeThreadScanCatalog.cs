using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class RuntimeThreadScanCatalog
    {
        private const int ScanTimeoutMilliseconds = 30000;
        private const string ScanScript =
            "set -eu\n" +
            "target_home=${1:-}\n" +
            "max_count=${2:-512}\n" +
            "case \"$max_count\" in \"\"|*[!0-9]*) max_count=512 ;; esac\n" +
            "if [ -z \"$target_home\" ]; then target_home=${CODEX_HOME:-$HOME/.codex}; fi\n" +
            "case \"$target_home\" in \"~\") target_home=$HOME ;; \"~/\"*) target_home=\"$HOME/${target_home#~/}\" ;; esac\n" +
            "case \"$target_home\" in /*) ;; *) target_home=\"$HOME/$target_home\" ;; esac\n" +
            "sessions_root=\"$target_home/sessions\"\n" +
            "if [ ! -d \"$sessions_root\" ]; then exit 0; fi\n" +
            "MYCODEX_SESSIONS_ROOT=\"$sessions_root\" MYCODEX_MAX_COUNT=\"$max_count\" perl -MFile::Find -MMIME::Base64=encode_base64 -e '\n" +
            "use strict;\n" +
            "use warnings;\n" +
            "my $root = $ENV{MYCODEX_SESSIONS_ROOT} || q{};\n" +
            "my $max = int($ENV{MYCODEX_MAX_COUNT} || 512);\n" +
            "my @files;\n" +
            "find(sub {\n" +
            "  return unless -f $_;\n" +
            "  return unless /\\.jsonl\\z/;\n" +
            "  my $path = $File::Find::name;\n" +
            "  my @st = stat($path);\n" +
            "  return unless @st;\n" +
            "  push @files, [$path, $st[9] || 0];\n" +
            "}, $root);\n" +
            "@files = sort { $b->[1] <=> $a->[1] || $a->[0] cmp $b->[0] } @files;\n" +
            "my $count = 0;\n" +
            "for my $item (@files) {\n" +
            "  last if $max > 0 && $count >= $max;\n" +
            "  my ($path, $mtime) = @$item;\n" +
            "  open(my $fh, q{<}, $path) or next;\n" +
            "  my $line = <$fh>;\n" +
            "  close($fh);\n" +
            "  next unless defined $line;\n" +
            "  chomp $line;\n" +
            "  print join(qq{\\t}, encode_base64($path, q{}), $mtime, encode_base64($line, q{})), qq{\\n};\n" +
            "  $count++;\n" +
            "}\n" +
            "'";

        private readonly IAppConfigStore _appConfigStore;
        private readonly IWslCommandExecutor _wslExecutor;
        private readonly IRemoteSshCommandExecutor _remoteExecutor;

        public RuntimeThreadScanCatalog(IAppConfigStore appConfigStore)
            : this(appConfigStore, new WslCommandExecutor(), new RemoteSshCommandExecutor())
        {
        }

        internal RuntimeThreadScanCatalog(
            IAppConfigStore appConfigStore,
            IWslCommandExecutor wslExecutor,
            IRemoteSshCommandExecutor remoteExecutor)
        {
            _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
            _wslExecutor = wslExecutor ?? throw new ArgumentNullException(nameof(wslExecutor));
            _remoteExecutor = remoteExecutor ?? throw new ArgumentNullException(nameof(remoteExecutor));
        }

        public async Task<List<AppServerThreadSummary>> ListThreadSummariesAsync(
            AppServerHostPlatform hostPlatform,
            int maxSummaries,
            CancellationToken cancellationToken)
        {
            var config = _appConfigStore.Load() ?? new AppConfigSnapshot();
            var arguments = new[]
            {
                ResolveTargetCodexHome(hostPlatform, config),
                maxSummaries > 0 ? maxSummaries.ToString(CultureInfo.InvariantCulture) : "512"
            };

            if (hostPlatform == AppServerHostPlatform.Wsl)
            {
                var result = await _wslExecutor.ExecuteAsync(
                    config.WslAppServer,
                    ScanScript,
                    "mycodex-scan-runtime-thread-sessions",
                    ScanTimeoutMilliseconds,
                    cancellationToken,
                    arguments).ConfigureAwait(false);
                return BuildSummariesOrThrow(result.ExitCode, result.StandardOutput, result.StandardError);
            }

            if (hostPlatform == AppServerHostPlatform.RemoteMac)
            {
                var result = await _remoteExecutor.ExecuteAsync(
                    config.GetSelectedRemoteUnixSshConfig(),
                    ScanScript,
                    "mycodex-scan-runtime-thread-sessions",
                    ScanTimeoutMilliseconds,
                    cancellationToken,
                    arguments).ConfigureAwait(false);
                return BuildSummariesOrThrow(result.ExitCode, result.StandardOutput, result.StandardError);
            }

            return new List<AppServerThreadSummary>();
        }

        private static string ResolveTargetCodexHome(AppServerHostPlatform hostPlatform, AppConfigSnapshot config)
        {
            if (hostPlatform != AppServerHostPlatform.Wsl)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static List<AppServerThreadSummary> BuildSummariesOrThrow(
            int exitCode,
            string standardOutput,
            string standardError)
        {
            if (exitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError)
                    ? "Runtime thread session scan failed."
                    : standardError.Trim());
            }

            var summaries = new List<AppServerThreadSummary>();
            if (string.IsNullOrWhiteSpace(standardOutput))
            {
                return summaries;
            }

            foreach (var rawLine in standardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }

                var sessionPath = DecodeBase64(parts[0]);
                var firstLine = DecodeBase64(parts[2]);
                var updatedAt = FromUnixSeconds(parts[1]);
                var summary = LocalSessionCatalog.TryBuildThreadSummaryFromSessionMetaLine(firstLine, sessionPath, updatedAt);
                if (summary != null)
                {
                    summaries.Add(summary);
                }
            }

            return summaries;
        }

        private static string DecodeBase64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static DateTime FromUnixSeconds(string value)
        {
            long seconds;
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds <= 0)
            {
                return DateTime.Now;
            }

            return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        }
    }
}
