using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteAttachmentCatalogOperator
    {
        Task<CodingRemoteAttachmentCatalogResult> ListPluginAttachmentsAsync(string sessionId, CancellationToken cancellationToken);

        Task<CodingRemoteAttachmentCatalogResult> ListSkillAttachmentsAsync(string sessionId, bool forceReload, CancellationToken cancellationToken);
    }
}
