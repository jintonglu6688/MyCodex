using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient
    {
        private async Task ReportCredentialFailureAsync(string trigger)
        {
            if (CredentialDiagnostics == null) return;
            try { await CredentialDiagnostics(trigger).ConfigureAwait(false); }
            catch (Exception error)
            {
                // Diagnostic reads can fail while the App Server is disconnected. Keep the original error and never log payloads.
                WriteDiagnostic(true, "[AuthenticationCenter] Credential diagnostics unavailable. Trigger={0}; ExceptionType={1}", trigger, error.GetType().Name);
            }
        }

        internal static async Task CollectWindowsCredentialDiagnosticsAsync(string trigger,
            Func<CancellationToken, Task<AppServerConfigSnapshot>> readConfig, string legacyRoot, string backendHome)
        {
            WriteDiagnostic(true, "[AuthenticationCenter] Collecting credential reference diagnostics. DiagnosticVersion=1; Trigger={0}", trigger);
            using (var timeout = new CancellationTokenSource())
            {
                var read = readConfig(timeout.Token);
                if (await Task.WhenAny(read, Task.Delay(2000)).ConfigureAwait(false) != read)
                {
                    timeout.Cancel();
                    // Observe a late failure without holding up the provider editor or changing the request outcome.
                    _ = read.ContinueWith(task => { var observed = task.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    WriteDiagnostic(true, "[AuthenticationCenter] Credential diagnostics unavailable. ConfigRead=timeout");
                    return;
                }
                var config = await read.ConfigureAwait(false);
                foreach (var line in WindowsCredentialDiagnostics.Collect(config, legacyRoot, backendHome))
                    WriteDiagnostic(true, "{0}", line);
            }
        }
    }
}
