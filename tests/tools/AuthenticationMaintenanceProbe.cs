using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.AuthenticationCenter;

internal static class AuthenticationMaintenanceProbe
{
    private static int Main(string[] args)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        if (args.Length > 0 && args[0] == "--authentication-maintenance")
        {
            if (args.Length == 4 && args[1] == "Finalize" && File.Exists(Path.Combine(args[2], "abort-finalize"))) return -1;
            int result;
            return AuthenticationInstallationMaintenance.TryRun(args, out result) ? result : 99;
        }
        int startupResult;
        if (AuthenticationHostStartup.TryRun(args, out startupResult)) return startupResult;
        var serializer = new JavaScriptSerializer();
        var identity = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "mycodex-auth-host.manifest.json")));
        if (args.Length == 1 && args[0] == "--version-json")
        {
            if (File.Exists(Path.Combine(root, "version-fail"))) return 1;
            Console.WriteLine(serializer.Serialize(identity));
            return 0;
        }
        if (args[0] == "serve")
        {
            if (File.Exists(Path.Combine(root, "startup-fail"))) { Console.Error.Write("backend_unavailable"); return 1; }
            if (Convert.ToInt32(identity["protocolVersion"]) == 2)
            {
                Console.WriteLine(serializer.Serialize(new { @event = "ready", protocolVersion = 2 }));
                return 0;
            }
            identity["event"] = "ready";
            Console.WriteLine(serializer.Serialize(identity));
            return 0;
        }
        if (args[0] == "rpc")
        {
            var request = serializer.Deserialize<Dictionary<string, object>>(Console.In.ReadToEnd());
            identity["executablePath"] = System.Reflection.Assembly.GetExecutingAssembly().Location;
            identity["dataDir"] = args[2];
            identity["codexHome"] = args[4];
            identity["route"] = new { takeover = false, running = false, accepting = false };
            object result = (string)request["method"] == "backend/shutdown" ? (object)new { status = "shutting_down" } : identity;
            Console.WriteLine(serializer.Serialize(new { jsonrpc = "2.0", id = request["id"], result }));
            return 0;
        }
        return 2;
    }
}
