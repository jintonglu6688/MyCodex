namespace MyCodex.Application.DTOs
{
    public class AppServerThreadMessage
    {
        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public int TurnIndex { get; set; }

        public string SourceItemId { get; set; }

        public int TurnItemIndex { get; set; }

        public string Sender { get; set; }

        public string Content { get; set; }

        public bool IsVoiceDelegation { get; set; }
    }
}
