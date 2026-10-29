using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.BrowserUse;

namespace MyCodex.Infrastructure.AppServer
{
    // Scoped to one facade and one verified marketplace; the gate still validates transition ownership.
    internal sealed class AppServerBrowserSetupScope : IDisposable
    {
        private static readonly AsyncLocal<AppServerBrowserSetupScope> Current = new AsyncLocal<AppServerBrowserSetupScope>();
        private readonly AppServerClient _client;
        private readonly string _root;
        private readonly AppServerBrowserSetupScope _previous;
        private IDisposable _operation;

        private AppServerBrowserSetupScope(AppServerClient client, string root)
        {
            _client = client;
            _root = Path.GetFullPath(root);
            _operation = RuntimeOperationGate.Shared.EnterOperation(allowTransitionOwner: true);
            _previous = Current.Value;
            Current.Value = this;
        }

        internal static IDisposable Enter(AppServerClient client, string root)
        {
            return new AppServerBrowserSetupScope(client, root);
        }

        internal static bool Allows(AppServerClient client, string method, object parameters)
        {
            var scope = Current.Value;
            if (scope == null || scope._operation == null || !ReferenceEquals(scope._client, client)) return false;
            var values = parameters as IDictionary<string, object>;
            if (values == null) return false;
            switch (method)
            {
                case "plugin/installed": return values.Count == 0;
                case "marketplace/add":
                    return values.Count == 1 && string.Equals(AppServerJsonValueReader.GetString(values, "source"), scope._root, StringComparison.OrdinalIgnoreCase);
                case "marketplace/remove":
                    return values.Count == 1 && AppServerJsonValueReader.GetString(values, "marketplaceName") == BrowserUsePluginSetup.Marketplace;
                case "plugin/install":
                    return values.Count == 2 && AppServerJsonValueReader.GetString(values, "pluginName") == "browser-use" &&
                        string.Equals(AppServerJsonValueReader.GetString(values, "marketplacePath"),
                            Path.Combine(scope._root, ".agents", "plugins", "marketplace.json"), StringComparison.OrdinalIgnoreCase);
                case "config/batchWrite":
                    var edits = AppServerJsonValueReader.GetArray(values, "edits").ToArray();
                    var edit = edits.Length == 1 ? edits[0] as IDictionary<string, object> : null;
                    return values.Count == 2 && Equals(AppServerJsonValueReader.GetValue(values, "reloadUserConfig"), true) &&
                        edit != null && edit.Count == 3 &&
                        AppServerJsonValueReader.GetString(edit, "keyPath") == BrowserUsePluginSetup.EnableKey &&
                        AppServerJsonValueReader.GetString(edit, "mergeStrategy") == "replace" &&
                        Equals(AppServerJsonValueReader.GetValue(edit, "value"), true);
                default: return false;
            }
        }

        public void Dispose()
        {
            var operation = Interlocked.Exchange(ref _operation, null);
            if (operation == null) return;
            if (ReferenceEquals(Current.Value, this)) Current.Value = _previous;
            operation.Dispose();
        }
    }
}
