namespace MyCodex.Application.DTOs
{
    public class AppServerPluginSkillInfo
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string ShortDescription { get; set; }

        public string Description { get; set; }

        public string Path { get; set; }

        public bool Enabled { get; set; }

        public string DefaultPrompt { get; set; }
    }
}
