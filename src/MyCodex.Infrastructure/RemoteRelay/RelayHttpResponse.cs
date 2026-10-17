namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class RelayHttpResponse
    {
        public RelayHttpResponse(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }

        public int StatusCode { get; private set; }

        public string Body { get; private set; }
    }
}

