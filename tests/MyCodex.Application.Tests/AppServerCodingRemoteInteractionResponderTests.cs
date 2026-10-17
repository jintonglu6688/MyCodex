using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteInteractionResponderTests
    {
        [Test]
        public async Task RespondApprovalAsync_MapsDecisionToAppServerClient()
        {
            var client = new FakeInteractionClient();
            var responder = new AppServerCodingRemoteInteractionResponder(client);

            var result = await responder.RespondApprovalAsync(
                new CodingRemoteApprovalResponseRequest
                {
                    RequestId = "approval-1",
                    Decision = "allow_for_session"
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(client.LastApprovalRequestId, Is.EqualTo("approval-1"));
            Assert.That(client.LastApprovalDecision, Is.EqualTo(AppServerApprovalDecision.AllowForSession));
            Assert.That(result.Completed, Is.True);
            Assert.That(result.Decision, Is.EqualTo("allow_for_session"));
        }

        [Test]
        public void RespondApprovalAsync_PendingRequestMissingThrowsRequestNotFound()
        {
            var client = new FakeInteractionClient
            {
                ResolveApprovalException = new InvalidOperationException("Pending approval request was not found.")
            };
            var responder = new AppServerCodingRemoteInteractionResponder(client);

            var ex = Assert.ThrowsAsync<CodingRemoteOperationException>(async () =>
                await responder.RespondApprovalAsync(
                    new CodingRemoteApprovalResponseRequest
                    {
                        RequestId = "approval-missing",
                        Decision = "decline"
                    },
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ErrorCode, Is.EqualTo(RemoteErrorCodes.RequestNotFound));
        }

        [Test]
        public async Task RespondToolInputAsync_ForwardsAnswersToAppServerClient()
        {
            var client = new FakeInteractionClient();
            var responder = new AppServerCodingRemoteInteractionResponder(client);

            var result = await responder.RespondToolInputAsync(
                new CodingRemoteToolInputRespondRequest
                {
                    RequestId = "input-1",
                    Answers = new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        { "choice", new[] { "continue" } }
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(client.LastToolInputRequestId, Is.EqualTo("input-1"));
            Assert.That(client.LastToolInputAnswers["choice"], Is.EqualTo(new[] { "continue" }));
            Assert.That(result.Completed, Is.True);
            Assert.That(result.Canceled, Is.False);
        }

        [Test]
        public async Task CancelToolInputAsync_ForwardsCancelToAppServerClient()
        {
            var client = new FakeInteractionClient();
            var responder = new AppServerCodingRemoteInteractionResponder(client);

            var result = await responder.CancelToolInputAsync(
                new CodingRemoteToolInputCancelRequest { RequestId = "input-1" },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(client.LastCanceledToolInputRequestId, Is.EqualTo("input-1"));
            Assert.That(result.Completed, Is.True);
            Assert.That(result.Canceled, Is.True);
        }

        [Test]
        public void RespondToolInputAsync_PendingRequestMissingThrowsRequestNotFound()
        {
            var client = new FakeInteractionClient
            {
                ResolveToolInputException = new InvalidOperationException("Pending tool input request was not found.")
            };
            var responder = new AppServerCodingRemoteInteractionResponder(client);

            var ex = Assert.ThrowsAsync<CodingRemoteOperationException>(async () =>
                await responder.RespondToolInputAsync(
                    new CodingRemoteToolInputRespondRequest
                    {
                        RequestId = "input-missing",
                        Answers = new Dictionary<string, string[]>(StringComparer.Ordinal)
                    },
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.ErrorCode, Is.EqualTo(RemoteErrorCodes.RequestNotFound));
        }

        private sealed class FakeInteractionClient : IAppServerInteractionClient
        {
            public string LastApprovalRequestId { get; private set; }

            public AppServerApprovalDecision LastApprovalDecision { get; private set; }

            public InvalidOperationException ResolveApprovalException { get; set; }

            public string LastToolInputRequestId { get; private set; }

            public IDictionary<string, string[]> LastToolInputAnswers { get; private set; }

            public string LastCanceledToolInputRequestId { get; private set; }

            public InvalidOperationException ResolveToolInputException { get; set; }

            public bool HasInterruptibleTurn(string threadId)
            {
                return false;
            }

            public Task InterruptAsync(string threadId, string turnId = null)
            {
                return Task.CompletedTask;
            }

            public Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision)
            {
                if (ResolveApprovalException != null)
                {
                    throw ResolveApprovalException;
                }

                LastApprovalRequestId = requestId;
                LastApprovalDecision = decision;
                return Task.CompletedTask;
            }

            public Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers)
            {
                if (ResolveToolInputException != null)
                {
                    throw ResolveToolInputException;
                }

                LastToolInputRequestId = requestId;
                LastToolInputAnswers = answers;
                return Task.CompletedTask;
            }

            public Task CancelToolUserInputRequestAsync(string requestId)
            {
                LastCanceledToolInputRequestId = requestId;
                return Task.CompletedTask;
            }
        }
    }
}
