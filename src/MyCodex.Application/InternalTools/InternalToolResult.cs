using System.Collections.Generic;

namespace MyCodex.Application.InternalTools
{
    public sealed class InternalToolResult
    {
        public bool Success { get; set; }

        public List<InternalToolContentItem> ContentItems { get; private set; } = new List<InternalToolContentItem>();

        public string StatusText { get; set; }

        public static InternalToolResult FromText(bool success, string text)
        {
            var result = new InternalToolResult
            {
                Success = success,
                StatusText = text
            };
            result.ContentItems.Add(InternalToolContentItem.TextItem(text));
            return result;
        }
    }
}
