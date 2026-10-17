using System;
using System.IO;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class EmbeddedRelayDirectoryLayout
    {
        private EmbeddedRelayDirectoryLayout(
            string rootDirectory,
            string configPath,
            string statePath,
            string stdoutLogPath,
            string stderrLogPath)
        {
            RootDirectory = rootDirectory;
            ConfigPath = configPath;
            StatePath = statePath;
            StdoutLogPath = stdoutLogPath;
            StderrLogPath = stderrLogPath;
        }

        public string RootDirectory { get; private set; }

        public string ConfigPath { get; private set; }

        public string StatePath { get; private set; }

        public string StdoutLogPath { get; private set; }

        public string StderrLogPath { get; private set; }

        public static EmbeddedRelayDirectoryLayout Create(string appDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                throw new ArgumentException("App data directory cannot be empty.", nameof(appDataDirectory));
            }

            var root = Path.Combine(appDataDirectory, "Relay");
            return new EmbeddedRelayDirectoryLayout(
                root,
                Path.Combine(root, "relay-config.json"),
                Path.Combine(root, "relay-state.db"),
                Path.Combine(root, "relay.out.log"),
                Path.Combine(root, "relay.err.log"));
        }
    }
}
