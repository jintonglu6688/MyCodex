using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class WslAppServerConfigSnapshot
    {
        public const string DefaultDistributionName = "Ubuntu";
        public const string DefaultCodexCommand = "codex";
        public const string DefaultShellCommand = "/bin/sh";

        private string _distributionName = DefaultDistributionName;
        private string _codexCommand = DefaultCodexCommand;
        private string _shellCommand = DefaultShellCommand;

        [DataMember]
        public string DistributionName
        {
            get { return string.IsNullOrWhiteSpace(_distributionName) ? DefaultDistributionName : _distributionName; }
            set { _distributionName = string.IsNullOrWhiteSpace(value) ? DefaultDistributionName : value.Trim(); }
        }

        [DataMember]
        public string CodexCommand
        {
            get { return string.IsNullOrWhiteSpace(_codexCommand) ? DefaultCodexCommand : _codexCommand; }
            set { _codexCommand = string.IsNullOrWhiteSpace(value) ? DefaultCodexCommand : value.Trim(); }
        }

        [DataMember]
        public string ShellCommand
        {
            get { return string.IsNullOrWhiteSpace(_shellCommand) ? DefaultShellCommand : _shellCommand; }
            set { _shellCommand = string.IsNullOrWhiteSpace(value) ? DefaultShellCommand : value.Trim(); }
        }

        [IgnoreDataMember]
        public WslCodexHomeStrategy CodexHomeStrategy { get; set; } = WslCodexHomeStrategy.WslDefault;

    }
}
