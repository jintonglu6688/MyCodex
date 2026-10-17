namespace MyCodex.Application.RemoteRelay
{
    public static class RelayEnvelopeDirections
    {
        public const string WindowsToMobile = "windows_to_mobile";
        public const string MobileToWindows = "mobile_to_windows";
    }

    public static class RelayPayloadEncodings
    {
        public const string EncryptedJson = "encrypted-json";
    }

    public static class SecureRelayFrameTypes
    {
        public const string ClientHello = "session.client_hello";
        public const string ServerHello = "session.server_hello";
        public const string Confirmation = "session.confirmation";
        public const string Envelope = "session.envelope";
    }

    public static class SecureRelayKinds
    {
        public const string Request = "rpc.request";
        public const string Response = "rpc.response";
        public const string Event = "event";
    }

    public static class SecureRelayRoles
    {
        public const string Android = "android";
        public const string Windows = "windows";
    }
}
