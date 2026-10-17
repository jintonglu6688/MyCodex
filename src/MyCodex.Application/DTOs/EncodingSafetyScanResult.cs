using System.Collections.Generic;
using System.Linq;

namespace MyCodex.Application.DTOs
{
    public sealed class EncodingSafetyScanResult
    {
        public EncodingSafetyScanResult()
        {
            Issues = new List<EncodingSafetyIssue>();
        }

        public List<EncodingSafetyIssue> Issues { get; }

        public bool HasIssues
        {
            get { return Issues.Count > 0; }
        }

        public int AffectedFileCount
        {
            get
            {
                return Issues
                    .Where(issue => issue != null && !string.IsNullOrWhiteSpace(issue.FilePath))
                    .Select(issue => issue.FilePath)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .Count();
            }
        }
    }
}
