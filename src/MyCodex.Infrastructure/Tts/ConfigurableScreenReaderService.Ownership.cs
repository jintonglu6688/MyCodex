using System.Threading.Tasks;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.Tts
{
    public sealed partial class ConfigurableScreenReaderService
    {
        private SpeechOutput _lastManualOutput;
        private SpeechOutput _lastAuxiliaryOutput;
        private volatile bool _isDisposed;

        public bool StopOwnedOutput()
        {
            Task pending;
            var success = false;
            lock (_operationQueueLock)
            {
                if (!QueueScreenReaderOperation("stop-owned-output", () =>
                {
                    success = StopOwnedOutputCore();
                    return success;
                }))
                {
                    return false;
                }

                pending = _operationQueueTail;
            }

            pending.GetAwaiter().GetResult();
            return success;
        }

        private void RememberOutput(IScreenReaderService service, bool useSlave, bool mappedUseSlave, string channelName)
        {
            var output = new SpeechOutput
            {
                Service = service,
                UseSlave = mappedUseSlave,
                ChannelName = channelName,
                Independent = mappedUseSlave &&
                    (ReferenceEquals(service, _byCtrlService) ||
                     ReferenceEquals(service, _baoYiWebApiService) ||
                     ReferenceEquals(service, _zdsrService))
            };
            lock (_syncRoot)
            {
                if (useSlave)
                {
                    _lastAuxiliaryOutput = output;
                }
                else
                {
                    _lastManualOutput = output;
                }
            }
        }

        private bool StopLastOutput(bool useSlave)
        {
            SpeechOutput output;
            lock (_syncRoot)
            {
                output = useSlave ? _lastAuxiliaryOutput : _lastManualOutput;
            }

            // NVDA and the reader main channel cannot identify a target's automatic output.
            return output == null || (useSlave && !output.Independent) || StopOutput(output);
        }

        private bool StopOwnedOutputCore()
        {
            lock (_syncRoot)
            {
                // Channel names may be shared across processes; releasing a target must not stop that channel.
                _lastAuxiliaryOutput = null;
                _lastManualOutput = null;
            }
            return true;
        }

        private bool StopOutput(SpeechOutput output)
        {
            ApplyIndependentChannelName(output.Service, output.ChannelName);
            return output.Service.Stop(output.UseSlave);
        }

        private sealed class SpeechOutput
        {
            internal IScreenReaderService Service;
            internal bool UseSlave;
            internal bool Independent;
            internal string ChannelName;
        }
    }
}
