using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AuthenticationConfigDocument
    {
        public string Text { get; set; }
        public string Version { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public Dictionary<string, object> Advanced { get; set; }
        // Full config editor: global settings a draft changes, and row settings that never apply.
        public List<string> GlobalChanges { get; set; } = new List<string>();
        public List<AuthenticationInactiveSetting> Inactive { get; set; } = new List<AuthenticationInactiveSetting>();
    }

    public sealed class AuthenticationInactiveSetting
    {
        public string Path { get; set; }
        public string Value { get; set; }
    }
}
