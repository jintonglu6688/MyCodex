using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class RemoteMacSshConfigSnapshot
    {
        public const int DefaultPort = 22;
        public const string DefaultShellCommand = "zsh";
        public const string DefaultCodexCommand = "codex";

        private string _hostName;
        private string _userName;
        private string _privateKeyPath;
        private string _hostKeyFingerprint;
        private string _plinkExecutablePath;
        private string _shellCommand = DefaultShellCommand;
        private string _codexCommand = DefaultCodexCommand;

        [DataMember]
        public string HostName
        {
            get { return string.IsNullOrWhiteSpace(_hostName) ? null : _hostName; }
            set { _hostName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public int Port { get; set; } = DefaultPort;

        [DataMember]
        public string UserName
        {
            get { return string.IsNullOrWhiteSpace(_userName) ? null : _userName; }
            set { _userName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public RemoteSshAuthenticationMode AuthenticationMode { get; set; } = RemoteSshAuthenticationMode.PrivateKey;

        [DataMember]
        public string PrivateKeyPath
        {
            get { return string.IsNullOrWhiteSpace(_privateKeyPath) ? null : _privateKeyPath; }
            set { _privateKeyPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public string ProtectedPassword { get; set; }

        [DataMember]
        public string HostKeyFingerprint
        {
            get { return string.IsNullOrWhiteSpace(_hostKeyFingerprint) ? null : _hostKeyFingerprint; }
            set { _hostKeyFingerprint = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public string PlinkExecutablePath
        {
            get { return string.IsNullOrWhiteSpace(_plinkExecutablePath) ? null : _plinkExecutablePath; }
            set { _plinkExecutablePath = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public string ShellCommand
        {
            get { return string.IsNullOrWhiteSpace(_shellCommand) ? DefaultShellCommand : _shellCommand; }
            set { _shellCommand = string.IsNullOrWhiteSpace(value) ? DefaultShellCommand : value.Trim(); }
        }

        [DataMember]
        public string CodexCommand
        {
            get { return string.IsNullOrWhiteSpace(_codexCommand) ? DefaultCodexCommand : _codexCommand; }
            set { _codexCommand = string.IsNullOrWhiteSpace(value) ? DefaultCodexCommand : value.Trim(); }
        }
    }
}
