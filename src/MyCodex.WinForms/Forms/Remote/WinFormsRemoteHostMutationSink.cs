using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;

namespace MyCodex.WinForms.Forms
{
    internal sealed class WinFormsRemoteHostMutationSink : IRemoteHostMutationSink
    {
        private readonly Control _dispatcher;
        private readonly Action<RemoteHostMutation> _handler;

        public WinFormsRemoteHostMutationSink(Control dispatcher, Action<RemoteHostMutation> handler)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public Task PublishAsync(RemoteHostMutation mutation, CancellationToken cancellationToken)
        {
            if (mutation == null || cancellationToken.IsCancellationRequested || _dispatcher.IsDisposed)
            {
                return Task.CompletedTask;
            }

            if (!_dispatcher.IsHandleCreated)
            {
                return Task.CompletedTask;
            }

            try
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_dispatcher.IsDisposed)
                    {
                        _handler(mutation);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }

            return Task.CompletedTask;
        }
    }
}
