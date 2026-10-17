using System;
using System.Collections.Generic;
using MyCodex.Application.InternalTools;

namespace MyCodex.Infrastructure.AppServer
{
    public static class AppServerDynamicToolResponseMapper
    {
        public static IDictionary<string, object> ToWire(InternalToolResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "success", result.Success },
                { "contentItems", MapContentItems(result.ContentItems) }
            };
        }

        private static object[] MapContentItems(IEnumerable<InternalToolContentItem> contentItems)
        {
            var items = new List<object>();
            if (contentItems == null)
            {
                return items.ToArray();
            }

            foreach (var contentItem in contentItems)
            {
                if (contentItem == null)
                {
                    continue;
                }

                if (string.Equals(contentItem.Type, "inputImage", StringComparison.Ordinal))
                {
                    items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "inputImage" },
                        { "imageUrl", contentItem.ImageUrl ?? string.Empty }
                    });
                    continue;
                }

                items.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "type", "inputText" },
                    { "text", contentItem.Text ?? string.Empty }
                });
            }

            return items.ToArray();
        }
    }
}
