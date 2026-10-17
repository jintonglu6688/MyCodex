using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal static class ComputerUsePolicyWriter
    {
        // A short-lived original App Server writes TOML through its public API, with no thread/model.
        public static async Task WriteAsync(string codexPath, string helperHome, IDictionary<string, object> policy, CancellationToken token)
        {
            Directory.CreateDirectory(helperHome);
            var start = AppServerProcessLifecycle.BuildStartInfo(codexPath, "app-server", helperHome,
                new Dictionary<string, string> { { "CODEX_HOME", helperHome } }, null);
            using (var process = new Process { StartInfo = start })
            using (var writeLock = new SemaphoreSlim(1, 1))
            {
                ComputerUseProcessJob job = null;
                AppServerProtocolTransport transport = null;
                try
                {
                    process.Start();
                    job = new ComputerUseProcessJob(process);
                    process.StandardInput.AutoFlush = true;
                    transport = new AppServerProtocolTransport(() => process.StandardInput,
                        AppServerJsonValue.CreateSerializer(), writeLock, new object(),
                        (method, milliseconds) => new TimeoutException("CUA_POLICY_WRITE_TIMEOUT"));
                    var reader = Task.Run(async () =>
                    {
                        try
                        {
                            string line;
                            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                var message = AppServerJsonValue.DeserializeDictionary(line);
                                if (message.ContainsKey("id") && !message.ContainsKey("method"))
                                {
                                    string error;
                                    transport.TryHandleResponse(message, out error);
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            foreach (var pending in transport.DrainPendingRequests())
                                pending.TrySetException(new InvalidOperationException("CUA_POLICY_WRITER_STOPPED"));
                        }
                    });
                    var errors = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { } } catch { } });
                    var timeout = TimeSpan.FromSeconds(15);
                    await transport.SendRequestAsync("initialize", new Dictionary<string, object>
                    {
                        { "clientInfo", new Dictionary<string, object> { { "name", "mycodex_cua_config" }, { "version", "1" } } }
                    }, timeout, token).ConfigureAwait(false);
                    await transport.SendNotificationAsync("initialized", new Dictionary<string, object>(), token).ConfigureAwait(false);
                    await transport.SendRequestAsync("config/batchWrite", new Dictionary<string, object>
                    {
                        { "edits", new[] { new Dictionary<string, object>
                        {
                            { "keyPath", "computer_use" }, { "value", policy }, { "mergeStrategy", "replace" }
                        } } }
                    }, timeout, token).ConfigureAwait(false);
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch { }
                    ProcessTreeTerminator.TryKill(process);
                    job?.TerminateAndWait();
                    job?.Dispose();
                }
            }
        }
    }
}
