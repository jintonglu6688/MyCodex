using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;

namespace MyCodex.WinForms.Forms
{
    internal sealed class WinFormsCodingRemotePlanExecutionResponder : ICodingRemotePlanExecutionResponder
    {
        private readonly Func<CodingRemotePlanExecutionResponseRequest, CancellationToken, Task<CodingRemotePlanExecutionResponseResult>> _respond;

        public WinFormsCodingRemotePlanExecutionResponder(
            Func<CodingRemotePlanExecutionResponseRequest, CancellationToken, Task<CodingRemotePlanExecutionResponseResult>> respond)
        {
            _respond = respond ?? throw new ArgumentNullException(nameof(respond));
        }

        public Task<CodingRemotePlanExecutionResponseResult> RespondPlanExecutionAsync(
            CodingRemotePlanExecutionResponseRequest request,
            CancellationToken cancellationToken)
        {
            return _respond(request, cancellationToken);
        }
    }
}
