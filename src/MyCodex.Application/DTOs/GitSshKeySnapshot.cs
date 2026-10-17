using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitSshKeySnapshot
    {
        public string SshDirectory { get; set; }

        public IReadOnlyList<GitSshKeyEntry> Keys { get; set; }

        public string Details { get; set; }
    }
}
