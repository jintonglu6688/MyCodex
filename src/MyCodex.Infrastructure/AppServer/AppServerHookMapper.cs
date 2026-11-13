using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    /// <summary>Maps hooks/list responses and config write responses.</summary>
    internal static class AppServerHookMapper
    {
        public static AppServerHooksListResult BuildList(IDictionary<string, object> result, Func<string, string> mapServerPathToClient)
        {
            var list = new AppServerHooksListResult();
            // Both list modes return one entry for the single cwd or thread this client asks about.
            var entry = AppServerJsonValueReader.GetArray(result, "data").OfType<IDictionary<string, object>>().FirstOrDefault();
            if (entry == null)
            {
                return list;
            }

            list.Cwd = MapPath(AppServerJsonValueReader.GetString(entry, "cwd"), mapServerPathToClient);
            list.Hooks.AddRange(AppServerJsonValueReader.GetArray(entry, "hooks")
                .OfType<IDictionary<string, object>>()
                .Select(hook => BuildHook(hook, mapServerPathToClient)));
            list.Warnings.AddRange(AppServerJsonValueReader.GetArray(entry, "warnings").OfType<string>());
            list.Errors.AddRange(AppServerJsonValueReader.GetArray(entry, "errors")
                .OfType<IDictionary<string, object>>()
                .Select(error => FormatError(error, mapServerPathToClient)));
            return list;
        }

        public static AppServerConfigWriteResult BuildWriteResult(IDictionary<string, object> result)
        {
            return new AppServerConfigWriteResult
            {
                Status = AppServerJsonValueReader.GetString(result, "status"),
                Version = AppServerJsonValueReader.GetString(result, "version")
            };
        }

        private static AppServerHookInfo BuildHook(IDictionary<string, object> hook, Func<string, string> mapServerPathToClient)
        {
            return new AppServerHookInfo
            {
                Key = AppServerJsonValueReader.GetString(hook, "key"),
                EventName = AppServerJsonValueReader.GetString(hook, "eventName"),
                HandlerType = AppServerJsonValueReader.GetString(hook, "handlerType"),
                Matcher = AppServerJsonValueReader.GetString(hook, "matcher"),
                Source = AppServerJsonValueReader.GetString(hook, "source"),
                SourcePath = MapPath(AppServerJsonValueReader.GetString(hook, "sourcePath"), mapServerPathToClient),
                PluginId = AppServerJsonValueReader.GetString(hook, "pluginId"),
                Command = AppServerJsonValueReader.GetString(hook, "command"),
                McpServer = AppServerJsonValueReader.GetString(hook, "server"),
                McpTool = AppServerJsonValueReader.GetString(hook, "tool"),
                TimeoutSec = AppServerJsonValueReader.GetNullableLong(hook, "timeoutSec"),
                Enabled = AppServerJsonValueReader.GetNullableBool(hook, "enabled") ?? false,
                IsManaged = AppServerJsonValueReader.GetNullableBool(hook, "isManaged") ?? false,
                CurrentHash = AppServerJsonValueReader.GetString(hook, "currentHash"),
                TrustStatus = AppServerJsonValueReader.GetString(hook, "trustStatus")
            };
        }

        private static string FormatError(IDictionary<string, object> error, Func<string, string> mapServerPathToClient)
        {
            var path = MapPath(AppServerJsonValueReader.GetString(error, "path"), mapServerPathToClient);
            var message = AppServerJsonValueReader.GetString(error, "message");
            return string.IsNullOrWhiteSpace(path) ? message : path + ": " + message;
        }

        private static string MapPath(string path, Func<string, string> mapServerPathToClient)
        {
            return string.IsNullOrWhiteSpace(path) ? path : mapServerPathToClient(path);
        }
    }
}
