using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RuntimeScopedConfigFileStoreTests
    {
        private const string AppStatePath = "~/.codex/mycodex/test-app-state.json";
        private const string ModeSettingsPath = "~/.codex/mycodex/mode-settings.json";

        [TestCase(null)]
        [TestCase("")]
        public void EmptyWriteStillUsesInputAndZeroByteCount(string content)
        {
            var executors = new CapturingCommandExecutors();
            var store = new RuntimeScopedConfigFileStore(executors, executors);
            store.WriteTextFile(new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl }, AppStatePath, content);
            Assert.That(executors.StandardInput, Is.EqualTo(string.Empty));
            AssertCommand(executors, "mycodex-write-runtime-config", 30000, AppStatePath, "0");
        }

        [TestCase(CodexRuntimeSources.Wsl, false)]
        [TestCase(CodexRuntimeSources.Wsl, true)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, false)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, true)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, false)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, true)]
        public void LargeStateWrite_UsesInputWithBoundedArguments(string source, bool asynchronous)
        {
            var content = "{\"text\":\"中文 'quoted' $HOME\r\n" + new string('x', 200000) + "\"}";
            var executors = new CapturingCommandExecutors();
            var config = CreateRemoteConfig(source, source == CodexRuntimeSources.RemoteUnixSsh, out var selectedSsh);
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            if (asynchronous) store.WriteTextFileAsync(config, AppStatePath, content).GetAwaiter().GetResult();
            else store.WriteTextFile(config, AppStatePath, content);

            Assert.That(executors.Arguments[1].Length, Is.LessThan(20), "State contents must not become a process argument.");
            AssertCommand(executors, "mycodex-write-runtime-config", 30000, AppStatePath,
                Encoding.UTF8.GetByteCount(content).ToString(CultureInfo.InvariantCulture));
            Assert.That(executors.StandardInput, Is.EqualTo(content));
            Assert.That(executors.Script, Does.Not.Contain(content));
            if (source == CodexRuntimeSources.Wsl)
                Assert.That(executors.WslConfig, Is.SameAs(config.WslAppServer));
            else
                Assert.That(executors.RemoteConfig, Is.SameAs(selectedSsh));
        }

        [TestCase(CodexRuntimeSources.Bundled, false)]
        [TestCase(CodexRuntimeSources.System, false)]
        [TestCase(CodexRuntimeSources.Wsl, true)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, true)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, true)]
        public void RuntimePaths_AreAvailableOnlyForEndpointTargets(string source, bool expected)
        {
            var executors = new CapturingCommandExecutors();
            var store = new RuntimeScopedConfigFileStore(executors, executors, AppStatePath);
            var config = new AppConfigSnapshot { CodexRuntimeSource = source };

            Assert.That(store.TryGetRuntimeAppStateFilePathForAsyncAccess(config, out var appStatePath), Is.EqualTo(expected));
            Assert.That(appStatePath, Is.EqualTo(expected ? AppStatePath : null));
            Assert.That(store.TryGetRuntimeModeSettingsFilePathForAsyncAccess(config, out var modeSettingsPath), Is.EqualTo(expected));
            Assert.That(modeSettingsPath, Is.EqualTo(expected ? ModeSettingsPath : null));
        }

        [Test]
        public void WindowsOperations_RejectWithoutInvokingEndpointExecutors()
        {
            var executors = new CapturingCommandExecutors();
            var store = new RuntimeScopedConfigFileStore(executors, executors);
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled };

            Assert.That(
                () => store.ReadTextFile(config, AppStatePath, out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => store.WriteTextFile(config, AppStatePath, "{}"),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(executors.WslCallCount, Is.Zero);
            Assert.That(executors.RemoteCallCount, Is.Zero);
        }

        [Test]
        public void ReadTextFile_WslUsesReadContractAndLegacyLiteralTildeMigration()
        {
            const string path = "~/.codex/mycodex/Office Docs/config 'test'.json";
            var executors = new CapturingCommandExecutors
            {
                WslResult = new WslCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "saved content"
                }
            };
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };
            config.WslAppServer.DistributionName = "Debian";
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            var found = store.ReadTextFile(config, path, out var content);

            Assert.That(found, Is.True);
            Assert.That(content, Is.EqualTo("saved content"));
            Assert.That(executors.WslConfig, Is.SameAs(config.WslAppServer));
            Assert.That(executors.WslCallCount, Is.EqualTo(1));
            Assert.That(executors.RemoteCallCount, Is.Zero);
            AssertCommand(executors, "mycodex-read-runtime-config", 20000, path);
            Assert.That(executors.Script, Does.Contain("target_file=$(resolve_path \"$original_path\")"));
            Assert.That(executors.Script, Does.Contain("if [ -e \"$target_file\" ] || [ -L \"$target_file\" ]; then exit 65; fi"));
            Assert.That(executors.Script, Does.Contain("if ! validate_searchable_parent \"$target_file\"; then exit 65; fi"));
            Assert.That(executors.Script, Does.Contain("printf '%s\\n' \"$HOME/~/$tail\""));
            Assert.That(executors.Script, Does.Contain("cp \"$legacy_file\" \"$target_file\""));
            Assert.That(executors.Script, Does.Contain("chmod 600 \"$target_file\""));
            Assert.That(executors.Script, Does.Not.Contain(path));
        }

        [Test]
        public void WriteTextFileAsync_WslPassesUtf8InputAndAtomicWriteScript()
        {
            const string path = "~/.codex/mycodex/mode settings.json";
            const string content = "{\"label\":\"中文 + spaces\"}";
            var executors = new CapturingCommandExecutors();
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            store.WriteTextFileAsync(config, path, content).GetAwaiter().GetResult();

            AssertCommand(executors, "mycodex-write-runtime-config", 30000, path,
                Encoding.UTF8.GetByteCount(content).ToString(CultureInfo.InvariantCulture));
            Assert.That(executors.StandardInput, Is.EqualTo(content));
            Assert.That(executors.Script, Does.Contain("cat > \"$tmp_file\""));
            Assert.That(executors.Script, Does.Contain("wc -c < \"$tmp_file\""));
            Assert.That(executors.Script, Does.Contain("trap 'rm -f \"$tmp_file\"' EXIT"));
            Assert.That(executors.Script, Does.Contain("chmod 600 \"$tmp_file\""));
            Assert.That(executors.Script, Does.Contain("mv -f \"$tmp_file\" \"$target_file\""));
            Assert.That(executors.Script, Does.Not.Contain(content));
        }

        [TestCase(CodexRuntimeSources.RemoteMacSsh, false)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, true)]
        public void ReadTextFileAsync_RemoteSourcesUseSelectedSshConfiguration(string source, bool useProfile)
        {
            var executors = new CapturingCommandExecutors
            {
                RemoteResult = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "remote content"
                }
            };
            var config = CreateRemoteConfig(source, useProfile, out var expectedSshConfig);
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            var result = store.ReadTextFileAsync(config, AppStatePath).GetAwaiter().GetResult();

            Assert.That(result.Item1, Is.True);
            Assert.That(result.Item2, Is.EqualTo("remote content"));
            Assert.That(executors.RemoteConfig, Is.SameAs(expectedSshConfig));
            Assert.That(executors.RemoteCallCount, Is.EqualTo(1));
            Assert.That(executors.WslCallCount, Is.Zero);
            AssertCommand(executors, "mycodex-read-runtime-config", 20000, AppStatePath);
        }

        [Test]
        public void ReadTextFile_MapsMissingAndFailedCommands()
        {
            var executors = new CapturingCommandExecutors
            {
                WslResult = new WslCommandExecutionResult { ExitCode = 66 }
            };
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            Assert.That(store.ReadTextFile(config, AppStatePath, out var missingContent), Is.False);
            Assert.That(missingContent, Is.Null);

            executors.WslResult = new WslCommandExecutionResult
            {
                ExitCode = 65,
                StandardError = " unsafe runtime path "
            };
            Assert.That(
                () => store.ReadTextFile(config, AppStatePath, out _),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("unsafe runtime path"));
        }

        [Test]
        public void ReadTextFile_RetriesRemoteSshConnectionRejectedBeforeAuthentication()
        {
            var executors = new CapturingCommandExecutors
            {
                RemoteResults = new Queue<RemoteSshCommandExecutionResult>(new[]
                {
                    new RemoteSshCommandExecutionResult
                    {
                        ExitCode = 255,
                        StandardError = "Connection closed by remote host port 22"
                    },
                    new RemoteSshCommandExecutionResult
                    {
                        ExitCode = 0,
                        StandardOutput = "saved content"
                    }
                })
            };
            var config = CreateRemoteConfig(CodexRuntimeSources.RemoteUnixSsh, true, out _);
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            Assert.That(store.ReadTextFile(config, AppStatePath, out var content), Is.True);
            Assert.That(content, Is.EqualTo("saved content"));
            Assert.That(executors.RemoteCallCount, Is.EqualTo(2));
        }

        [Test]
        public void RemoteWrite_DoesNotRetryWhenConnectionCloses()
        {
            var executors = new CapturingCommandExecutors
            {
                RemoteResult = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 255,
                    StandardError = "Connection closed by remote host port 22"
                }
            };
            var config = CreateRemoteConfig(CodexRuntimeSources.RemoteUnixSsh, true, out _);
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            Assert.That(
                () => store.WriteTextFile(config, AppStatePath, "{}"),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(executors.RemoteCallCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoteRead_DoesNotRetryAuthenticationFailure()
        {
            var executors = new CapturingCommandExecutors
            {
                RemoteResult = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 255,
                    StandardError = "Permission denied (publickey)."
                }
            };
            var config = CreateRemoteConfig(CodexRuntimeSources.RemoteUnixSsh, true, out _);
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            Assert.That(
                () => store.ReadTextFile(config, AppStatePath, out _),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(executors.RemoteCallCount, Is.EqualTo(1));
        }

        [Test]
        public void WriteAndDelete_FailedCommandsSurfaceEndpointError()
        {
            var executors = new CapturingCommandExecutors
            {
                WslResult = new WslCommandExecutionResult
                {
                    ExitCode = 1,
                    StandardError = " endpoint write failed "
                }
            };
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };
            var store = new RuntimeScopedConfigFileStore(executors, executors);

            Assert.That(
                () => store.WriteTextFile(config, AppStatePath, "{}"),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("endpoint write failed"));
        }

        private static AppConfigSnapshot CreateRemoteConfig(
            string source,
            bool useProfile,
            out RemoteMacSshConfigSnapshot expectedSshConfig)
        {
            var config = new AppConfigSnapshot { CodexRuntimeSource = source };
            if (!useProfile)
            {
                config.RemoteMacSsh.HostName = "legacy-mac.example";
                expectedSshConfig = config.RemoteMacSsh;
                return config;
            }

            var selectedProfile = new RemoteUnixSshProfileConfigSnapshot
            {
                Id = "selected",
                Ssh = new RemoteMacSshConfigSnapshot { HostName = "unix.example" }
            };
            config.RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
            {
                new RemoteUnixSshProfileConfigSnapshot { Id = "other" },
                selectedProfile
            };
            config.SelectedRemoteUnixSshProfileId = selectedProfile.Id;
            expectedSshConfig = selectedProfile.Ssh;
            return config;
        }

        private static void AssertCommand(
            CapturingCommandExecutors executors,
            string operationName,
            int timeoutMilliseconds,
            params string[] arguments)
        {
            Assert.That(executors.OperationName, Is.EqualTo(operationName));
            Assert.That(executors.TimeoutMilliseconds, Is.EqualTo(timeoutMilliseconds));
            Assert.That(executors.Arguments, Is.EqualTo(arguments));
            Assert.That(executors.Script, Does.StartWith("resolve_path() {\n"));
        }

        private sealed class CapturingCommandExecutors : IWslCommandExecutor, IRemoteSshCommandExecutor
        {
            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                StandardInput = standardInput;
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                StandardInput = standardInput;
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }

            public WslCommandExecutionResult WslResult { get; set; } = new WslCommandExecutionResult();

            public RemoteSshCommandExecutionResult RemoteResult { get; set; } = new RemoteSshCommandExecutionResult();

            public Queue<RemoteSshCommandExecutionResult> RemoteResults { get; set; }

            public int WslCallCount { get; private set; }

            public int RemoteCallCount { get; private set; }

            public WslAppServerConfigSnapshot WslConfig { get; private set; }

            public RemoteMacSshConfigSnapshot RemoteConfig { get; private set; }

            public string Script { get; private set; }

            public string OperationName { get; private set; }

            public int TimeoutMilliseconds { get; private set; }

            public string[] Arguments { get; private set; }

            public string StandardInput { get; private set; }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                WslCallCount++;
                WslConfig = config;
                Capture(script, operationName, timeoutMilliseconds, arguments);
                return Task.FromResult(WslResult);
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                RemoteCallCount++;
                RemoteConfig = config;
                Capture(script, operationName, timeoutMilliseconds, arguments);
                return Task.FromResult(RemoteResults != null && RemoteResults.Count > 0
                    ? RemoteResults.Dequeue()
                    : RemoteResult);
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }

            private void Capture(string script, string operationName, int timeoutMilliseconds, string[] arguments)
            {
                Script = script;
                OperationName = operationName;
                TimeoutMilliseconds = timeoutMilliseconds;
                Arguments = arguments;
            }
        }
    }
}
