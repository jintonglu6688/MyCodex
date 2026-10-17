using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerThreadDetails : AppServerThreadSummary
    {
        public AppServerThreadDetails()
        {
            Messages = new List<AppServerThreadMessage>();
            TurnStatuses = new Dictionary<string, string>(System.StringComparer.Ordinal);
        }

        public List<AppServerEventArgs> Activities { get; } = new List<AppServerEventArgs>();

        public int TurnCount { get; set; }

        public List<AppServerThreadMessage> Messages { get; }

        public Dictionary<string, string> TurnStatuses { get; }
    }
}
