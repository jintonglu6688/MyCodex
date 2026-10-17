namespace MyCodex.Application.DTOs
{
    public sealed class AppServerExperimentalFeature
    {
        public string Name { get; set; }

        public string Stage { get; set; }

        public bool Enabled { get; set; }

        public bool DefaultEnabled { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string Announcement { get; set; }
    }
}
