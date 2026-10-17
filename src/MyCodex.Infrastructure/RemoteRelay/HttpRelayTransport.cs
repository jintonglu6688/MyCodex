using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class HttpRelayTransport : IRelayHttpTransport
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
        private readonly HttpClient _httpClient;

        public HttpRelayTransport()
            : this(CreateHttpClient())
        {
        }

        internal HttpRelayTransport(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<RelayHttpResponse> SendAsync(RelayHttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url))
            {
                foreach (KeyValuePair<string, string> pair in request.Headers)
                {
                    message.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }

                if (request.Body != null)
                {
                    message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
                }

                using (var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new RelayHttpResponse((int)response.StatusCode, body);
                }
            }
        }

        internal static string BuildUrl(RelayHostConfig config, string path)
        {
            var connectionHost = string.IsNullOrWhiteSpace(config.ConnectionHost)
                ? config.RelayHost
                : config.ConnectionHost;
            var connectionPort = config.ConnectionPort > 0
                ? config.ConnectionPort
                : config.RelayPort;
            var connectionTlsRequired = string.IsNullOrWhiteSpace(config.ConnectionHost)
                ? config.TlsRequired
                : config.ConnectionTlsRequired;
            var scheme = connectionTlsRequired ? "https" : "http";
            return scheme + "://" + connectionHost.Trim().TrimEnd('/') + ":" +
                connectionPort.ToString(CultureInfo.InvariantCulture) + path;
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout = DefaultTimeout
            };
        }
    }
}

