using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // Usage column of the authentication center's provider list. Results are kept while the
    // list is rebuilt after edits, and queried again only when the list is opened or refreshed.
    internal sealed class AuthenticationUsageColumn
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        private int _generation;

        internal AuthenticationUsageColumn(IAuthenticationCenterClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        internal string Text(string providerId)
        {
            return providerId != null && _texts.TryGetValue(providerId, out var text) ? text : string.Empty;
        }

        internal void Forget(string providerId)
        {
            _texts.Remove(providerId);
        }

        // One provider at a time: the backend serves one request at a time, so user actions wait
        // for one query at most. A restart clears old results and abandons an earlier pass.
        internal async Task QueryAsync(IEnumerable<string> providerIds, bool restart, Action<string> updated, CancellationToken token)
        {
            if (restart)
            {
                _generation++;
                _texts.Clear();
            }
            var generation = _generation;
            var ids = providerIds.ToList();
            foreach (var id in ids)
            {
                _texts[id] = UiLocalization.Text("ProviderUsage.Column.Querying", "Querying…");
                updated(id);
            }
            foreach (var id in ids)
            {
                string text;
                try
                {
                    text = AuthenticationUsagePresentation.Summary(await _client.QueryUsageAsync(id, token));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    text = UiLocalization.Text("ProviderUsage.Column.Failed", "Query failed");
                }
                if (generation != _generation) return;
                _texts[id] = text;
                updated(id);
            }
        }
    }
}
