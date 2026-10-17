namespace MyCodex.Application.DTOs
{
    public sealed class GitSshKeyEntry
    {
        public string Name { get; set; }

        public string PublicKeyPath { get; set; }

        public string PrivateKeyPath { get; set; }

        public bool HasPrivateKey { get; set; }

        public string KeyType { get; set; }

        public string Fingerprint { get; set; }

        public string Comment { get; set; }

        public string PublicKeyText { get; set; }
    }
}
