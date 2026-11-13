using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerConfigWriteResult
    {
        public string Status { get; set; }

        public string Version { get; set; }

        public bool IsOverridden
        {
            get { return string.Equals(Status, "okOverridden", StringComparison.Ordinal); }
        }
    }
}
