namespace MyCodex.Application.DTOs
{
    public sealed class GitRemoteEntry
    {
        public string Name { get; set; }

        public string FetchUrl { get; set; }

        public string PushUrl { get; set; }
    }
}
