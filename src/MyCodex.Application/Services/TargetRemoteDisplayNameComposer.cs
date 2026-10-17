using System;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class TargetRemoteDisplayNameComposer
    {
        public static string Compose(string machineName, TargetProfileSnapshot target)
        {
            var computer = string.IsNullOrWhiteSpace(machineName) ? "MyCodex" : machineName.Trim();
            if (target == null)
            {
                return computer;
            }

            var platform = string.Equals(target.Kind, "Wsl", StringComparison.OrdinalIgnoreCase) ? "WSL" :
                string.Equals(target.Kind, "Ssh", StringComparison.OrdinalIgnoreCase) ? "SSH" : "Windows";
            var name = (target.Name ?? string.Empty).Trim();
            return computer + " · " + platform +
                (name.Length == 0 || string.Equals(name, platform, StringComparison.OrdinalIgnoreCase) ? string.Empty : " · " + name);
        }
    }
}
