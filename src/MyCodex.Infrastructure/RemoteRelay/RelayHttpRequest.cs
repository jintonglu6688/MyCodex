using System.Collections.Generic;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class RelayHttpRequest
    {
        public RelayHttpRequest(string method, string url, string body)
        {
            Method = method;
            Url = url;
            Body = body;
            Headers = new Dictionary<string, string>();
        }

        public string Method { get; private set; }

        public string Url { get; private set; }

        public string Body { get; private set; }

        public Dictionary<string, string> Headers { get; private set; }
    }
}

