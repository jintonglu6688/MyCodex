using System.Collections;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerCredentialReferenceMapper
    {
        internal static void Apply(AppServerConfigSnapshot snapshot, IDictionary<string, object> result)
        {
            var config = AppServerJsonValue.GetDictionary(result, "config");
            snapshot.CredentialReferenceScope = "effective_config";
            var layers = result != null && result.TryGetValue("layers", out var rawLayers) && rawLayers is IEnumerable sequence ? sequence : new object[0];
            foreach (var item in layers)
            {
                var layer = item as IDictionary<string, object>;
                if (layer == null) continue;
                var name = AppServerJsonValue.GetDictionary(layer, "name");
                if (AppServerJsonValue.GetString(name, "type") == "user" &&
                    string.IsNullOrEmpty(AppServerJsonValue.GetString(layer, "disabledReason")) &&
                    layer.TryGetValue("config", out var value) && value is IDictionary<string, object> user)
                {
                    config = user;
                    snapshot.CredentialReferenceScope = "user_layer";
                    snapshot.CredentialReferenceFilePath = AppServerJsonValue.GetString(name, "file");
                    break;
                }
            }
            var provider = AppServerJsonValue.GetString(config, "model_provider") ?? "openai";
            snapshot.CredentialReferenceProviderId = provider;
            snapshot.CredentialReferenceHasProfile = config.ContainsKey("profile");
            var table = AppServerJsonValue.GetDictionary(AppServerJsonValue.GetDictionary(config, "model_providers"), provider);
            if (table.ContainsKey("env_key"))
                Add(snapshot, "env_key", table["env_key"] as string);
            foreach (var header in AppServerJsonValue.GetDictionary(table, "env_http_headers"))
                Add(snapshot, "env_http_headers", header.Value as string);
        }

        private static void Add(AppServerConfigSnapshot snapshot, string field, string name)
        {
            // Bound diagnostics even for unusually large provider maps. No credential values are copied.
            if (snapshot.CredentialEnvironmentReferences.Count < 32)
                snapshot.CredentialEnvironmentReferences.Add(new AppServerCredentialEnvironmentReference { Field = field, VariableName = name });
            else snapshot.CredentialReferencesTruncated = true;
        }
    }
}
