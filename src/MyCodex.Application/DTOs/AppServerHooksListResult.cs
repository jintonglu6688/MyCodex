using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerHooksListResult
    {
        public AppServerHooksListResult()
        {
            Hooks = new List<AppServerHookInfo>();
            Warnings = new List<string>();
            Errors = new List<string>();
        }

        public string Cwd { get; set; }

        public bool IsThreadView { get; set; }

        public List<AppServerHookInfo> Hooks { get; private set; }

        public List<string> Warnings { get; private set; }

        public List<string> Errors { get; private set; }
    }
}
