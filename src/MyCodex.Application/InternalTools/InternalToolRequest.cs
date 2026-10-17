namespace MyCodex.Application.InternalTools
{
    public sealed class InternalToolRequest
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public object Arguments { get; set; }

        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public string CallId { get; set; }
    }
}
