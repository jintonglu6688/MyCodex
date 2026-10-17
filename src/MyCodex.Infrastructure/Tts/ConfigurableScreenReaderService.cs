using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.ByCtrl;
using MyCodex.Infrastructure.Nvda;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Zdsr;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Tts
{
    public sealed partial class ConfigurableScreenReaderService : IConfigurableScreenReaderService
    {
        private readonly object _syncRoot = new object();
        private readonly object _operationQueueLock = new object();
        private readonly IScreenReaderService _byCtrlService;
        private readonly IScreenReaderService _baoYiWebApiService;
        private readonly IScreenReaderService _zdsrService;
        private readonly IScreenReaderService _nvdaService;
        private Task _operationQueueTail = Task.FromResult(0);
        private ScreenReaderProviderKind _preferredProvider;
        private ScreenReaderProviderKind _lastSuccessfulAutomaticProvider = ScreenReaderProviderKind.Auto;
        private bool _manualBrowseUseReaderMainChannel;
        private string _independentChannelName;
        private string _lastError;

        public ConfigurableScreenReaderService(AppConfigService appConfigService, IAppConfigStore appConfigStore)
        {
            if (appConfigService == null)
            {
                throw new ArgumentNullException(nameof(appConfigService));
            }

            if (appConfigStore == null)
            {
                throw new ArgumentNullException(nameof(appConfigStore));
            }

            _byCtrlService = new ByCtrlScreenReaderService(appConfigService);
            _baoYiWebApiService = new BaoYiWebApiScreenReaderService();
            _zdsrService = new ZdsrScreenReaderService();
            _nvdaService = new NvdaScreenReaderService();
            AppConfigSnapshot snapshot;
            try
            {
                snapshot = appConfigStore.Load() ?? new AppConfigSnapshot();
            }
            catch
            {
                snapshot = new AppConfigSnapshot();
            }

            ApplyConfig(snapshot);
        }

        internal ConfigurableScreenReaderService(
            IScreenReaderService byCtrlService,
            IScreenReaderService baoYiWebApiService,
            IScreenReaderService zdsrService,
            IScreenReaderService nvdaService,
            AppConfigSnapshot snapshot)
        {
            _byCtrlService = byCtrlService ?? throw new ArgumentNullException(nameof(byCtrlService));
            _baoYiWebApiService = baoYiWebApiService ?? throw new ArgumentNullException(nameof(baoYiWebApiService));
            _zdsrService = zdsrService ?? throw new ArgumentNullException(nameof(zdsrService));
            _nvdaService = nvdaService ?? throw new ArgumentNullException(nameof(nvdaService));
            ApplyConfig(snapshot ?? new AppConfigSnapshot());
        }

        public bool IsAvailable
        {
            get
            {
                return ResolveService() != null;
            }
        }

        public bool IsInitialized
        {
            get
            {
                var service = ResolveService();
                return service != null && service.IsInitialized;
            }
        }

        public string LastError
        {
            get
            {
                lock (_syncRoot)
                {
                    if (!string.IsNullOrWhiteSpace(_lastError))
                    {
                        return _lastError;
                    }
                }

                var service = ResolveService();
                return service != null ? service.LastError : null;
            }
        }

        public void ApplyConfig(AppConfigSnapshot snapshot)
        {
            lock (_syncRoot)
            {
                var preferredProvider = NormalizeProvider(snapshot != null ? snapshot.ScreenReaderProvider : ScreenReaderProviderKind.Auto);
                if (_preferredProvider != preferredProvider)
                {
                    _lastSuccessfulAutomaticProvider = ScreenReaderProviderKind.Auto;
                }

                _preferredProvider = preferredProvider;
                _manualBrowseUseReaderMainChannel = snapshot != null && snapshot.ManualBrowseUseReaderMainChannel;
                var channelName = snapshot?.ScreenReaderIndependentChannelName;
                _independentChannelName = string.IsNullOrWhiteSpace(channelName) ? "MyCodex" : channelName.Trim();
                _lastError = null;
            }
        }

        public bool EnsureInitialized()
        {
            if (_isDisposed)
            {
                return false;
            }

            ScreenReaderProviderKind provider;
            var service = ResolveService(out provider);
            if (service == null)
            {
                return false;
            }

            if (ShouldUseResponsiveOperations(provider))
            {
                return QueueScreenReaderOperation(
                    "initialize",
                    () => ExecuteScreenReaderOperation(candidate => candidate.EnsureInitialized()));
            }

            return service.EnsureInitialized();
        }

        public bool IsReaderRunning()
        {
            ScreenReaderProviderKind provider;
            var service = ResolveService(out provider);
            if (service == null)
            {
                return false;
            }

            return ShouldUseResponsiveOperations(provider) || service.IsReaderRunning();
        }

        public int GetReaderState()
        {
            ScreenReaderProviderKind provider;
            var service = ResolveService(out provider);
            if (service == null)
            {
                return 0;
            }

            return ShouldUseResponsiveOperations(provider) ? 4 : service.GetReaderState();
        }

        public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                SetLastError(SharedLocalization.Text("ScreenReader.Error.SpeakTextRequired", "Text to read cannot be empty."));
                return false;
            }

            ScreenReaderProviderKind provider;
            var service = ResolveService(out provider);
            if (service == null)
            {
                SetLastError(SharedLocalization.Text("ScreenReader.Error.NoAvailableService", "No available screen reader service."));
                return false;
            }

            if (ShouldUseResponsiveOperations(provider))
            {
                return QueueScreenReaderOperation(
                    "speak",
                    () => SpeakCore(text, useSlave, append, allowBreak));
            }

            return SpeakCore(text, useSlave, append, allowBreak);
        }

        public bool Stop(bool useSlave)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (ShouldUseResponsiveOperations(_preferredProvider))
            {
                return QueueScreenReaderOperation(
                    "stop",
                    () => StopCore(useSlave));
            }

            return StopCore(useSlave);
        }

        public void Dispose()
        {
            Task disposal;
            lock (_operationQueueLock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _operationQueueTail = _operationQueueTail.ContinueWith(delegate
                {
                    StopOwnedOutputCore();
                    _byCtrlService.Dispose();
                    _baoYiWebApiService.Dispose();
                    _zdsrService.Dispose();
                    _nvdaService.Dispose();
                }, TaskScheduler.Default);
                disposal = _operationQueueTail;
            }

            disposal.GetAwaiter().GetResult();
        }

        // Availability checks stay cheap; live probing runs on the operation queue.
        private IScreenReaderService ResolveService()
        {
            ScreenReaderProviderKind ignored;
            return ResolveService(out ignored);
        }

        private IScreenReaderService ResolveService(out ScreenReaderProviderKind resolvedProvider)
        {
            foreach (var provider in GetProviderOrder())
            {
                var service = ResolveProviderService(provider);
                if (service != null && service.IsAvailable)
                {
                    resolvedProvider = provider;
                    return service;
                }
            }

            resolvedProvider = ScreenReaderProviderKind.Auto;
            return null;
        }

        private ScreenReaderProviderKind[] GetProviderOrder()
        {
            lock (_syncRoot)
            {
                if (_preferredProvider != ScreenReaderProviderKind.Auto)
                {
                    return new[] { _preferredProvider };
                }

                var providers = BuildAutomaticProviderOrder();
                var index = Array.IndexOf(providers, _lastSuccessfulAutomaticProvider);
                if (index > 0)
                {
                    Array.Copy(providers, 0, providers, 1, index);
                    providers[0] = _lastSuccessfulAutomaticProvider;
                }

                return providers;
            }
        }

        private IScreenReaderService ResolveProviderService(ScreenReaderProviderKind provider)
        {
            switch (NormalizeProvider(provider))
            {
                case ScreenReaderProviderKind.ByCtrl:
                    return _byCtrlService;
                case ScreenReaderProviderKind.BaoYiWebApi:
                    return _baoYiWebApiService;
                case ScreenReaderProviderKind.Zdsr:
                    return _zdsrService;
                case ScreenReaderProviderKind.Nvda:
                    return _nvdaService;
                default:
                    return null;
            }
        }

        private bool MapUseSlave(IScreenReaderService service, bool useSlave)
        {
            if (!IsMultiChannelScreenReaderService(service != null ? service.GetType() : null))
            {
                return useSlave;
            }

            lock (_syncRoot)
            {
                if (!useSlave && _manualBrowseUseReaderMainChannel)
                {
                    return false;
                }
            }

            return true;
        }

        private bool SpeakCore(string text, bool useSlave, bool append, bool allowBreak)
        {
            return ExecuteScreenReaderOperation(service =>
            {
                string channelName;
                lock (_syncRoot)
                {
                    channelName = _independentChannelName;
                }

                ApplyIndependentChannelName(service, channelName);
                var mappedUseSlave = MapUseSlave(service, useSlave);
                if (!service.Speak(text, mappedUseSlave, append, allowBreak))
                {
                    return false;
                }

                RememberOutput(service, useSlave, mappedUseSlave, channelName);
                return true;
            });
        }

        private bool StopCore(bool useSlave)
        {
            return StopLastOutput(useSlave);
        }

        private bool ExecuteScreenReaderOperation(Func<IScreenReaderService, bool> operation)
        {
            bool automatic;
            string channelName;
            ScreenReaderProviderKind[] providers;
            lock (_syncRoot)
            {
                automatic = _preferredProvider == ScreenReaderProviderKind.Auto;
                channelName = _independentChannelName;
                providers = GetProviderOrder();
            }

            string error = null;
            foreach (var provider in providers)
            {
                var service = ResolveProviderService(provider);
                try
                {
                    if (service == null || !service.IsAvailable)
                    {
                        continue;
                    }

                    ApplyIndependentChannelName(service, channelName);
                    // A loaded API (or a successful no-op Stop) does not mean its reader is running.
                    if ((!automatic || service.EnsureInitialized() && service.IsReaderRunning()) && operation(service))
                    {
                        lock (_syncRoot)
                        {
                            if (automatic && _preferredProvider == ScreenReaderProviderKind.Auto)
                            {
                                _lastSuccessfulAutomaticProvider = provider;
                            }

                            _lastError = null;
                        }

                        return true;
                    }

                    error = service.LastError ?? error;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                lock (_syncRoot)
                {
                    if (automatic && _lastSuccessfulAutomaticProvider == provider)
                    {
                        _lastSuccessfulAutomaticProvider = ScreenReaderProviderKind.Auto;
                    }
                }
            }

            SetLastError(error ?? SharedLocalization.Text("ScreenReader.Error.NoAvailableService", "No available screen reader service."));
            return false;
        }

        private bool ShouldUseResponsiveOperations(ScreenReaderProviderKind provider)
        {
            ScreenReaderProviderKind preferredProvider;
            lock (_syncRoot)
            {
                preferredProvider = _preferredProvider;
            }

            return preferredProvider == ScreenReaderProviderKind.Auto ||
                   NormalizeProvider(provider) == ScreenReaderProviderKind.Zdsr;
        }

        private bool QueueScreenReaderOperation(string operationName, Func<bool> operation)
        {
            if (operation == null)
            {
                return false;
            }

            lock (_operationQueueLock)
            {
                if (_isDisposed)
                {
                    return false;
                }

                _operationQueueTail = _operationQueueTail.ContinueWith(
                    delegate
                    {
                        ExecuteQueuedScreenReaderOperation(operationName, operation);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }

            return true;
        }

        private void ExecuteQueuedScreenReaderOperation(string operationName, Func<bool> operation)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                operation();
            }
            catch (Exception ex)
            {
                SetLastError(ex.Message);
                Trace.TraceWarning(
                    "Screen reader operation failed. Operation={0}; Error={1}",
                    string.IsNullOrWhiteSpace(operationName) ? "unknown" : operationName,
                    ex.Message);
            }
        }

        private static void ApplyIndependentChannelName(IScreenReaderService service, string channelName)
        {
            var byCtrlService = service as ByCtrlScreenReaderService;
            if (byCtrlService != null)
            {
                byCtrlService.ApplyIndependentChannelName(channelName);
                return;
            }

            var baoYiWebApiService = service as BaoYiWebApiScreenReaderService;
            if (baoYiWebApiService != null)
            {
                baoYiWebApiService.ApplyIndependentChannelName(channelName);
                return;
            }

            var zdsrService = service as ZdsrScreenReaderService;
            if (zdsrService != null)
            {
                zdsrService.ApplyIndependentChannelName(channelName);
            }
        }

        private static bool IsMultiChannelScreenReaderService(Type serviceType)
        {
            return serviceType == typeof(ByCtrlScreenReaderService) ||
                   serviceType == typeof(BaoYiWebApiScreenReaderService) ||
                   serviceType == typeof(ZdsrScreenReaderService);
        }

        private static ScreenReaderProviderKind NormalizeProvider(ScreenReaderProviderKind provider)
        {
            if ((int)provider == 1)
            {
                return ScreenReaderProviderKind.ByCtrl;
            }

            switch (provider)
            {
                case ScreenReaderProviderKind.Auto:
                case ScreenReaderProviderKind.ByCtrl:
                case ScreenReaderProviderKind.BaoYiWebApi:
                case ScreenReaderProviderKind.Zdsr:
                case ScreenReaderProviderKind.Nvda:
                    return provider;
                default:
                    return ScreenReaderProviderKind.Auto;
            }
        }

        private static ScreenReaderProviderKind[] BuildAutomaticProviderOrder()
        {
            return new[]
            {
                ScreenReaderProviderKind.BaoYiWebApi,
                ScreenReaderProviderKind.ByCtrl,
                ScreenReaderProviderKind.Zdsr,
                ScreenReaderProviderKind.Nvda
            };
        }

        private void SetLastError(string error)
        {
            lock (_syncRoot)
            {
                _lastError = error;
            }
        }
    }
}
