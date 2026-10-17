using System;
using System.Net;

namespace MyCodex.Infrastructure.Markets
{
    internal sealed class TimeoutWebClient : WebClient
    {
        private readonly int _timeoutMilliseconds;

        public TimeoutWebClient(TimeSpan timeout)
        {
            _timeoutMilliseconds = timeout <= TimeSpan.Zero
                ? 30000
                : Convert.ToInt32(Math.Min(timeout.TotalMilliseconds, int.MaxValue));
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            if (request == null)
            {
                return null;
            }

            request.Timeout = _timeoutMilliseconds;

            var httpRequest = request as HttpWebRequest;
            if (httpRequest != null)
            {
                httpRequest.ReadWriteTimeout = _timeoutMilliseconds;
            }

            return request;
        }
    }
}
