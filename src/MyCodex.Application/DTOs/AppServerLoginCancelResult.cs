using System;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerLoginCancelResult
    {
        public string Status { get; set; }

        public bool Canceled
        {
            get
            {
                return string.Equals(Status, "canceled", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool NotFound
        {
            get
            {
                return string.Equals(Status, "notFound", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
