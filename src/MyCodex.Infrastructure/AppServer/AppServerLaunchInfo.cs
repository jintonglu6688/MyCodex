using System;
using System.Collections.Generic;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerLaunchInfo
    {
        public AppServerLaunchInfo(
            string fileName,
            string arguments,
            IDictionary<string, string> environmentVariables = null,
            AppServerHostPlatform hostPlatform = AppServerHostPlatform.Windows,
            IEnumerable<string> sensitiveTempFilePaths = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppServerLaunchInfo.Error.ExecutablePathRequired", "Executable path cannot be empty."),
                    nameof(fileName));
            }

            FileName = fileName;
            Arguments = arguments ?? string.Empty;
            EnvironmentVariables = environmentVariables != null
                ? new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HostPlatform = hostPlatform;
            SensitiveTempFilePaths = sensitiveTempFilePaths != null
                ? new List<string>(sensitiveTempFilePaths)
                : new List<string>();
        }

        public string FileName { get; private set; }

        public string Arguments { get; private set; }

        public IDictionary<string, string> EnvironmentVariables { get; private set; }

        public AppServerHostPlatform HostPlatform { get; private set; }

        public IList<string> SensitiveTempFilePaths { get; private set; }
    }
}
