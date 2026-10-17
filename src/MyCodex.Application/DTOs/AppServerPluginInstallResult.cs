using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerPluginInstallResult
    {
        public string AuthPolicy { get; set; }

        public List<AppServerPluginAppInfo> AppsNeedingAuth { get; set; }
    }
}
