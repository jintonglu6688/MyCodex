using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient
    {
        public async Task<AuthenticationConfigDocument> PreviewProviderConfigAsync(AuthenticationProviderEdit edit, string editedText, CancellationToken cancellationToken)
        {
            var parameters = WriteEdit(edit);
            if (editedText != null) parameters["editedText"] = editedText;
            // A full config editor draft is split against the text shown when it opened.
            if (editedText != null && edit.EditorBase != null) parameters["editorBase"] = edit.EditorBase;
            var value = await _request("config/provider/preview", parameters, cancellationToken).ConfigureAwait(false);
            if (Text(value, "text") == null || Text(value, "baseUrl") == null || Text(value, "model") == null || !value.ContainsKey("advanced"))
                throw new AuthenticationCenterException("invalid_response");
            return new AuthenticationConfigDocument
            {
                Text = Text(value, "text"), BaseUrl = Text(value, "baseUrl"), Model = Text(value, "model"), Advanced = ReadAdvanced(value),
                GlobalChanges = Strings(value, "globalChanges") ?? new List<string>()
            };
        }

        public async Task<AuthenticationConfigDocument> ViewProviderConfigAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken)
        {
            var value = await _request("config/provider/view", WriteEdit(edit), cancellationToken).ConfigureAwait(false);
            if (Text(value, "text") == null) throw new AuthenticationCenterException("invalid_response");
            return new AuthenticationConfigDocument
            {
                Text = Text(value, "text"),
                Inactive = Objects(value, "inactive").Select(item => new AuthenticationInactiveSetting
                {
                    Path = Text(item, "path"), Value = item.TryGetValue("value", out var setting) ? setting as string ?? new JavaScriptSerializer().Serialize(setting) : null
                }).Where(item => !string.IsNullOrEmpty(item.Path)).ToList()
            };
        }
    }
}
