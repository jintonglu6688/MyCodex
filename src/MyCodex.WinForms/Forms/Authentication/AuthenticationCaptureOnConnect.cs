using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;

namespace MyCodex.WinForms.Forms
{
    internal static class AuthenticationCaptureOnConnect
    {
        internal static async Task RunAsync(IAuthenticationCenterClient client,
            Func<bool> isCurrentTarget, Action<string> showStatus, CancellationToken cancellationToken)
        {
            // Never install or contact another target as a side effect of connecting this one.
            try
            {
                var snapshot = await Task.Run(() => client.ReadAsync(cancellationToken), cancellationToken);
                if (!cancellationToken.IsCancellationRequested && isCurrentTarget() &&
                    (snapshot.LiveState == "unavailable" || !string.IsNullOrEmpty(snapshot.SyncError)))
                {
                    showStatus(AuthenticationCenterUiText.LiveStatus(snapshot, string.Empty).Trim());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!cancellationToken.IsCancellationRequested && isCurrentTarget())
                    showStatus(AuthenticationCenterUiText.Error(error));
            }
        }
    }
}
