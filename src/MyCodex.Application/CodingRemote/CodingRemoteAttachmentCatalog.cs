using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteAttachmentCatalogResult
    {
        public CodingRemoteAttachmentCatalogResult()
        {
            Items = new List<CodingRemoteAttachmentCatalogItem>();
        }

        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public List<CodingRemoteAttachmentCatalogItem> Items { get; set; }
    }

    public sealed class CodingRemoteAttachmentCatalogItem
    {
        public string Kind { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Path { get; set; }

        public bool Enabled { get; set; }

        public bool Installed { get; set; }

        public string SourceText { get; set; }

        public string Scope { get; set; }

        public string Summary { get; set; }
    }
}
