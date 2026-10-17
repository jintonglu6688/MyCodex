using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class ApprovalRequestClassification
    {
        public ApprovalRequestClassification()
        {
            Targets = new List<string>();
        }

        public ApprovalRequestCategory Category { get; set; }

        public ApprovalRequestConfidence Confidence { get; set; }

        public string DisplayAction { get; set; }

        public string DisplayCommand { get; set; }

        public string RawCommand { get; set; }

        public string ShellKind { get; set; }

        public List<string> Targets { get; private set; }
    }
}
