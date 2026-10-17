using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal interface IEmbeddedRelayHealthClient
    {
        Task<bool> WaitUntilHealthyAsync(string healthUrl, TimeSpan timeout, CancellationToken cancellationToken);
    }

    public sealed class EmbeddedRelayHealthClient : IEmbeddedRelayHealthClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<bool> WaitUntilHealthyAsync(string healthUrl, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(healthUrl))
            {
                return false;
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var requestTimeout = remaining < TimeSpan.FromSeconds(1)
                    ? remaining
                    : TimeSpan.FromSeconds(1);
                try
                {
                    using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        requestCancellation.CancelAfter(requestTimeout);
                        using (var response = await HttpClient.GetAsync(healthUrl, requestCancellation.Token).ConfigureAwait(false))
                        {
                            if ((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                catch
                {
                }

                remaining = deadline - DateTime.UtcNow;
                var delay = TimeSpan.FromMilliseconds(250);
                if (delay > remaining)
                {
                    delay = remaining;
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            return false;
        }
    }
}
