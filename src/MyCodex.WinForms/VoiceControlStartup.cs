using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Instances;

namespace MyCodex.WinForms
{
    // Reuse the authenticated per-target pipe. This works for existing conversations
    // without adding a server or requiring new dynamic tools in their runtime history.
    internal static class VoiceControlStartup
    {
        internal static bool TryRun(string[] args, string channelId, string sharedRoot, out int exitCode,
            Func<string, TargetInstanceRequest, TargetInstanceReply> send = null, Action<string> write = null)
        {
            exitCode = 0;
            if (args == null || !args.Any(arg => string.Equals(arg, "--voice-control", StringComparison.OrdinalIgnoreCase) ||
                (arg ?? string.Empty).StartsWith("--voice-control=", StringComparison.OrdinalIgnoreCase))) return false;
            var json = new JavaScriptSerializer { MaxJsonLength = 4096, RecursionLimit = 8 };
            TargetInstanceReply reply;
            try
            {
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < args.Length; i++)
                {
                    var option = args[i] ?? string.Empty;
                    var separator = option.IndexOf('=');
                    var key = (separator < 0 ? option : option.Substring(0, separator)).ToLowerInvariant();
                    if (key != "--voice-control" && key != "--thread" && key != "--target" && key != "--channel")
                        throw new ArgumentException("Unsupported voice control option.");
                    var value = separator < 0 ? (++i < args.Length ? args[i] : null) : option.Substring(separator + 1);
                    if (string.IsNullOrWhiteSpace(value) || options.ContainsKey(key)) throw new ArgumentException("A unique value is required for each voice control option.");
                    options.Add(key, value);
                }
                var arguments = json.DeserializeObject(options["--voice-control"]) as IDictionary<string, object>;
                if (arguments == null) throw new ArgumentException("Voice control requires a JSON object.");
                string threadId;
                options.TryGetValue("--thread", out threadId);
                string target;
                if (!options.TryGetValue("--target", out target)) target = "windows";
                if (!TargetMigrationPlanner.IsSafeTargetId(target)) throw new ArgumentException("Invalid target ID.");
                var request = new TargetInstanceRequest { Operation = TargetInstanceOperation.VoiceControl,
                    VoiceArguments = arguments, VoiceThreadId = threadId };
                VoiceControlCommand.Parse(arguments).ValidateConversation(threadId);
                reply = send != null ? send(target, request) : TargetInstanceCoordinator.Send(channelId, target, sharedRoot, request, 30000);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is KeyNotFoundException)
            {
                reply = TargetInstanceReply.Failure(ex.Message);
            }
            exitCode = reply.Success ? 0 : 1;
            (write ?? Console.WriteLine)(json.Serialize(new
            {
                success = reply.Success, error = reply.Error, thread_id = reply.VoiceThreadId, voice = reply.VoiceResult
            }));
            return true;
        }
    }
}
