using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.WinForms.Forms
{
    /// <summary>
    /// Answers or ignores a pending async question from the phone through the same queue and
    /// send path as the Windows pending questions window.
    /// </summary>
    internal sealed class WinFormsCodingRemoteAsyncInputResponder : ICodingRemoteAsyncInputResponder
    {
        private readonly Control _owner;
        private readonly AsyncInputRequestQueue _queue;
        private readonly Func<PendingAsyncInputRequest, string, Task> _send;
        private readonly Action<string> _complete;

        public WinFormsCodingRemoteAsyncInputResponder(
            Control owner,
            AsyncInputRequestQueue queue,
            Func<PendingAsyncInputRequest, string, Task> send,
            Action<string> complete)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        }

        public Task<CodingRemoteToolInputResponseResult> RespondAsyncInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken)
        {
            return RunOnUiThreadAsync(async delegate
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pending = Find(request == null ? null : request.RequestId);
                AsyncInputRequestQueue.SaveAnswers(pending, RemoteAsyncInputRequests.FlattenAnswers(request.Answers));
                var responseText = AsyncInputRequestQueue.BuildResponseText(pending);
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Async input answers are required.");
                }

                try
                {
                    await _send(pending, responseText).ConfigureAwait(true);
                }
                catch (InvalidOperationException ex)
                {
                    throw new CodingRemoteOperationException(RemoteErrorCodes.OperationFailed, ex.Message);
                }

                _complete(pending.Key);
                return new CodingRemoteToolInputResponseResult
                {
                    RequestId = request.RequestId,
                    Completed = true,
                    StatusText = string.Empty
                };
            });
        }

        public Task<CodingRemoteToolInputResponseResult> IgnoreAsyncInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken)
        {
            return RunOnUiThreadAsync(delegate
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pending = Find(request == null ? null : request.RequestId);
                _complete(pending.Key);
                return Task.FromResult(new CodingRemoteToolInputResponseResult
                {
                    RequestId = request.RequestId,
                    Completed = true,
                    Canceled = true,
                    StatusText = string.Empty
                });
            });
        }

        private PendingAsyncInputRequest Find(string requestId)
        {
            var pending = string.IsNullOrWhiteSpace(requestId)
                ? null
                : _queue.Items.FirstOrDefault(item =>
                    string.Equals(RemoteAsyncInputRequests.CreateRequestId(item.Source), requestId, StringComparison.Ordinal));
            if (pending == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, "Async input request was not found.");
            }

            return pending;
        }

        private Task<CodingRemoteToolInputResponseResult> RunOnUiThreadAsync(Func<Task<CodingRemoteToolInputResponseResult>> action)
        {
            if (!_owner.InvokeRequired)
            {
                return action();
            }

            var completion = new TaskCompletionSource<CodingRemoteToolInputResponseResult>();
            _owner.BeginInvoke(new Action(async delegate
            {
                try
                {
                    completion.SetResult(await action().ConfigureAwait(true));
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));
            return completion.Task;
        }
    }
}
