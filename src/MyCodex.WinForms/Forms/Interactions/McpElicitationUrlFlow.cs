using System;
using System.Threading.Tasks;

namespace MyCodex.WinForms.Forms
{
    internal static class McpElicitationUrlFlow
    {
        // The URL comes from an MCP server, so it is checked before the prompt shows it. Every path that
        // does not end in resolve answers the request with cancel, or the turn would wait on it indefinitely.
        public static async Task RunAsync(
            string url,
            Func<string, bool> confirm,
            Func<Task<bool>> ensureAppServerAvailable,
            Action<string> open,
            Func<Task> resolve,
            Func<Task> cancel)
        {
            string allowedUrl;
            try
            {
                allowedUrl = ExternalUrlLauncher.RequireAllowedUrl(url);
            }
            catch (InvalidOperationException)
            {
                await cancel();
                throw;
            }

            var accepted = confirm(allowedUrl);
            if (!await ensureAppServerAvailable())
            {
                return;
            }

            if (!accepted)
            {
                await cancel();
                return;
            }

            try
            {
                open(allowedUrl);
            }
            catch (Exception)
            {
                await cancel();
                throw;
            }

            await resolve();
        }
    }
}
