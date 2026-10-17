using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslDistributionService
    {
        public Task<IList<string>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.Run<IList<string>>(() =>
            {
                var result = ProcessExecutionRunner.Run(new ProcessExecutionRequest
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsl.exe", Arguments = "--list --quiet", UseShellExecute = false,
                        CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.Unicode, StandardErrorEncoding = Encoding.Unicode
                    },
                    CancellationToken = cancellationToken,
                    TimeoutMilliseconds = 15000
                });
                if (result.ExitCode != 0) throw new InvalidOperationException("Unable to list installed WSL distributions.");
                return Parse(result.StandardOutput);
            }, cancellationToken);
        }

        internal static IList<string> Parse(string output)
        {
            return (output ?? string.Empty).Replace("\0", string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim()).Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
