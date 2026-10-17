using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteInteractionResponder : ICodingRemoteInteractionResponder
    {
        private readonly IAppServerInteractionClient _interactionClient;

        public AppServerCodingRemoteInteractionResponder(IAppServerInteractionClient interactionClient)
        {
            _interactionClient = interactionClient ?? throw new ArgumentNullException(nameof(interactionClient));
        }

        public async Task<CodingRemoteApprovalResponseResult> RespondApprovalAsync(
            CodingRemoteApprovalResponseRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteApprovalResponseRequest();
            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Approval request ID is required.");
            }

            var decision = MapDecision(request.Decision);
            try
            {
                await _interactionClient.ResolveApprovalRequestAsync(request.RequestId, decision).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, ex.Message);
            }

            return new CodingRemoteApprovalResponseResult
            {
                RequestId = request.RequestId,
                Decision = FormatDecision(decision),
                Completed = true,
                StatusText = "Approval response submitted."
            };
        }

        public async Task<CodingRemoteToolInputResponseResult> RespondToolInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteToolInputRespondRequest();
            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Tool input request ID is required.");
            }

            var answers = request.Answers ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
            try
            {
                await _interactionClient.ResolveToolUserInputRequestAsync(request.RequestId, answers).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, ex.Message);
            }

            return new CodingRemoteToolInputResponseResult
            {
                RequestId = request.RequestId,
                Completed = true,
                Canceled = false,
                StatusText = "Tool input submitted."
            };
        }

        public async Task<CodingRemoteToolInputResponseResult> CancelToolInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteToolInputCancelRequest();
            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Tool input request ID is required.");
            }

            try
            {
                await _interactionClient.CancelToolUserInputRequestAsync(request.RequestId).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, ex.Message);
            }

            return new CodingRemoteToolInputResponseResult
            {
                RequestId = request.RequestId,
                Completed = true,
                Canceled = true,
                StatusText = "Tool input canceled."
            };
        }

        private static AppServerApprovalDecision MapDecision(string decision)
        {
            switch ((decision ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "allow_once":
                case "allowonce":
                    return AppServerApprovalDecision.AllowOnce;
                case "allow_for_session":
                case "allowforsession":
                    return AppServerApprovalDecision.AllowForSession;
                case "decline":
                    return AppServerApprovalDecision.Decline;
                default:
                    throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Unsupported approval decision.");
            }
        }

        private static string FormatDecision(AppServerApprovalDecision decision)
        {
            switch (decision)
            {
                case AppServerApprovalDecision.AllowForSession:
                    return "allow_for_session";
                case AppServerApprovalDecision.Decline:
                    return "decline";
                default:
                    return "allow_once";
            }
        }
    }
}
