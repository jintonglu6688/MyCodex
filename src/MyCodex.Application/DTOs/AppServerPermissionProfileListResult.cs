using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerPermissionProfileListResult
    {
        public AppServerPermissionProfileListResult()
        {
            Profiles = new List<AppServerPermissionProfileSummary>();
        }

        public List<AppServerPermissionProfileSummary> Profiles { get; private set; }

        public string NextCursor { get; set; }
    }
}
