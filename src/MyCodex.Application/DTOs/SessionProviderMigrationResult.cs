namespace MyCodex.Application.DTOs
{
    public sealed class SessionProviderMigrationResult
    {
        public bool NeedsMigration { get; set; }
        public bool Changed { get; set; }
        public bool Supported { get; set; } = true;
    }
}
