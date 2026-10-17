using System;

namespace MyCodex.Infrastructure.Office
{
    public sealed class OfficeCliEnvironmentStore : IOfficeCliEnvironmentStore
    {
        public string GetUserPath()
        {
            return Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        }

        public void SetUserPath(string path)
        {
            Environment.SetEnvironmentVariable("Path", path ?? string.Empty, EnvironmentVariableTarget.User);
        }

        public string GetProcessPath()
        {
            return Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        }

        public void SetProcessPath(string path)
        {
            Environment.SetEnvironmentVariable("PATH", path ?? string.Empty);
        }
    }
}
