// Standalone process probe compiled by SharedRelayHost.Tests.ps1, not shipped with MyCodex.
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;

internal static class SharedRelayLifecycleProbe
{
    private static int Main(string[] args)
    {
        try
        {
            if (args[0] == "--shared-relay-host")
            {
                var assembly = typeof(SharedEmbeddedRelayHost).Assembly;
                var store = (IRemoteServiceConfigStore)Activator.CreateInstance(
                    assembly.GetType("MyCodex.Infrastructure.RemoteRelay.SharedRelayConfigStore"),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { args[1] }, null);
                typeof(SharedEmbeddedRelayHost).GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { args[1], store, new ProbeRelay(store, args[1]) });
                return 0;
            }

            var root = args[1];
            if (args[0] == "seed")
            {
                var store = new JsonRemoteServiceConfigStore(Path.Combine(root, "config", "relay-service.json"),
                    Path.Combine(root, "unused-target.json"));
                var shared = store.LoadShared();
                shared.RemoteServiceMode = "Embedded";
                shared.EncryptedAccessSecret = "unchanged-probe-secret";
                shared.EmbeddedRelayCertificateSha256 = "unchanged-probe-certificate";
                shared.RelaySigningPublicKey = "unchanged-probe-key";
                shared.RelaySigningKeyFingerprint = "unchanged-probe-pin";
                store.SaveShared(shared);
                return 0;
            }

            var marker = args[2];
            var config = new AppConfigService(Path.Combine(root, marker), AppDomain.CurrentDomain.BaseDirectory, "Probe", root);
            using (var service = new SharedEmbeddedRelayService(config))
            {
                var started = service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).GetAwaiter().GetResult();
                if (!started.Success) throw new InvalidOperationException(started.ErrorMessage);
                File.WriteAllText(Path.Combine(root, marker + ".ready"), "ready");
                while (!File.Exists(Path.Combine(root, marker + ".exit"))) Thread.Sleep(100);
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length > 1)
            {
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "probe-error-" + Guid.NewGuid().ToString("N") + ".txt"), ex.ToString());
            }

            return 1;
        }
    }

    private sealed class ProbeRelay : IEmbeddedRelayService
    {
        private readonly IRemoteServiceConfigStore _store;
        private readonly string _root;
        private readonly EmbeddedRelayProcessController _processes = new EmbeddedRelayProcessController();

        public ProbeRelay(IRemoteServiceConfigStore store, string root)
        {
            _store = store;
            _root = root;
        }

        public EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot)
        {
            return new EmbeddedRelayStatus { State = _processes.IsRunning(Identity(snapshot))
                ? EmbeddedRelayRunState.Running : EmbeddedRelayRunState.Stopped };
        }

        public Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken)
        {
            var layout = EmbeddedRelayDirectoryLayout.Create(_root);
            var identity = _processes.Start(new EmbeddedRelayBinaryLocator().Resolve(), layout.ConfigPath,
                layout.RootDirectory, layout.StdoutLogPath, layout.StderrLogPath);
            snapshot.EmbeddedRelayConfigPath = layout.ConfigPath;
            snapshot.EmbeddedRelayStatePath = layout.StatePath;
            snapshot.EmbeddedRelayProcessId = identity.ProcessId;
            snapshot.EmbeddedRelayProcessExecutablePath = identity.ExecutablePath;
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = identity.StartTimeUtcTicks;
            _store.Save(snapshot);
            return Task.FromResult(new EmbeddedRelayStartResult { Success = true, Status = GetStatus(snapshot) });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            var snapshot = _store.Load();
            _processes.Stop(Identity(snapshot));
            snapshot.EmbeddedRelayProcessId = 0;
            snapshot.EmbeddedRelayProcessExecutablePath = null;
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = 0;
            _store.Save(snapshot);
            return Task.CompletedTask;
        }

        public EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot)
        {
            return new EmbeddedRelayDiagnosticInfo { Status = GetStatus(snapshot) };
        }

        private static EmbeddedRelayProcessIdentity Identity(RemoteServiceConfigSnapshot snapshot)
        {
            return new EmbeddedRelayProcessIdentity(snapshot.EmbeddedRelayProcessId,
                snapshot.EmbeddedRelayProcessExecutablePath, snapshot.EmbeddedRelayProcessStartTimeUtcTicks);
        }
    }
}
