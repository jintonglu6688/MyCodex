using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.Services
{
    public static class RemoteServicePairingInfoFormatter
    {
        public static string FormatCopyText(PairingPackage package)
        {
            if (package == null)
            {
                return string.Empty;
            }

            var relay = package.Relay ?? throw new ArgumentException("The Relay descriptor is required.", nameof(package));
            var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "version", package.Version },
                {
                    "relay",
                    new Dictionary<string, object>
                    {
                        { "url", relay.Url },
                        { "signingPublicKey", relay.SigningPublicKey },
                        { "signingKeyFingerprint", relay.SigningKeyFingerprint },
                        { "embeddedTlsCertificateSha256", relay.EmbeddedTlsCertificateSha256 }
                    }
                },
                { "tenantId", package.TenantId },
                { "hostId", package.HostId },
                { "hostName", package.HostName },
                { "inviteId", package.InviteId },
                { "inviteSecret", package.InviteSecret },
                { "expiresAt", package.ExpiresAt },
                { "hostSigningPublicKey", package.HostSigningPublicKey },
                { "hostAgreementPublicKey", package.HostAgreementPublicKey },
                { "hostKeyVersion", package.HostKeyVersion },
                { "hostSignature", package.HostSignature }
            });
            var data = SecureRemoteBase64Url.Encode(Encoding.UTF8.GetBytes(json));
            return "mycodex://pair?data=" + Uri.EscapeDataString(data);
        }

    }
}
