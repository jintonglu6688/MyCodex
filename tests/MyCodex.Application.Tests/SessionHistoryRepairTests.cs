using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionHistoryRepairTests
    {
        private const string ThreadId = "01a1263c-44bd-7800-8000-000000000001";

        [Test]
        public async Task ClientSendsTheModeAndReadsWhatChanged()
        {
            var sent = new List<string>();
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/sessionHistory/repair"));
                Assert.That(parameters["threadId"], Is.EqualTo(ThreadId));
                sent.Add((string)parameters["mode"]);
                return Reply(new Dictionary<string, object>
                {
                    { "repaired", true }, { "skipped", null }, { "reasoning", 2 }, { "ids", 3 },
                    { "convertedCompactions", 1 }, { "encryptedParts", 0 }, { "replacedCompactions", 0 },
                    { "remainingCompactions", 4 }
                });
            });
            var result = await client.RepairSessionHistoryAsync(ThreadId, AuthenticationSessionRepairMode.Resume, CancellationToken.None);
            await client.RepairSessionHistoryAsync(ThreadId, AuthenticationSessionRepairMode.Rejected, CancellationToken.None);
            await client.RepairSessionHistoryAsync(ThreadId, AuthenticationSessionRepairMode.Compaction, CancellationToken.None);
            Assert.That(sent, Is.EqualTo(new[] { "resume", "rejected", "compaction" }));
            Assert.That(result.Repaired, Is.True);
            Assert.That(result.Changes, Is.EqualTo(6));
            Assert.That(result.RemainingCompactions, Is.EqualTo(4));
        }

        [Test]
        public async Task ClientReportsWhyNothingWasRepaired()
        {
            var skipped = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(new Dictionary<string, object> { { "repaired", false }, { "skipped", "not_official" } }));
            var result = await skipped.RepairSessionHistoryAsync(ThreadId, AuthenticationSessionRepairMode.Resume, CancellationToken.None);
            Assert.That(result.Repaired, Is.False);
            Assert.That(result.Skipped, Is.EqualTo("not_official"));

            var incomplete = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(new Dictionary<string, object> { { "repaired", true } }));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                incomplete.RepairSessionHistoryAsync(ThreadId, AuthenticationSessionRepairMode.Rejected, CancellationToken.None)).Code,
                Is.EqualTo("invalid_response"));
        }

        [Test]
        public async Task ClientReadsWhetherSessionsAreRepairedBeforeTheyLoad()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(new Dictionary<string, object>
                {
                    { "unified", true }, { "pending", false }, { "repairOnResume", true }
                }));
            Assert.That((await client.ReadSessionHistoryAsync(CancellationToken.None)).RepairOnResume, Is.True);
        }

        [TestCase("{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"[ArrayParam] [input[6].content] [array_above_max_length] Invalid 'input[6].content': array too long. Expected an array with maximum length 0, but got an array with length 1 instead.\"},\"status\":400}")]
        [TestCase("Invalid 'input[19].id': 'a1_msg_35'. Expected an ID that begins with 'msg'.")]
        [TestCase("Item with id 'rs_123' not found. Items are not persisted when `store` is set to false.")]
        [TestCase("reasoning `encrypted_content` was not issued to this caller")]
        [TestCase("Encrypted function output content could not be decrypted or decoded.")]
        [TestCase("{\"code\":\"invalid-argument\",\"error\":\"Could not decrypt the provided encrypted_content\"}")]
        [TestCase("{\"error\":{\"code\":\"invalid_encrypted_content\"}}")]
        [TestCase("The encrypted content for item rs_1 could not be verified.")]
        public void RefusedHistoryIsRecognized(string message)
        {
            Assert.That(ThreadHistoryRejectionClassifier.IsRepairableRejection(message), Is.True);
        }

        [TestCase("stream disconnected before completion: network timeout")]
        [TestCase("Invalid 'input[2].content': array too long. Expected an array with maximum length 10, but got 11.")]
        [TestCase("The model `gpt-x` does not exist or you do not have access to it.")]
        [TestCase("")]
        public void OtherFailuresAreNotRecognized(string message)
        {
            Assert.That(ThreadHistoryRejectionClassifier.IsRepairableRejection(message), Is.False);
        }

        [Test]
        public async Task RefusedHistoryIsRepairedByTheBackendBeforeTheLocalRepair()
        {
            var backend = new RecordingBackend(Repaired(5));
            var local = new LocalRepair();
            var service = new AuthenticationThreadHistoryRepairService(() => backend.Client, local, count => Task.FromResult(true));
            var result = await service.TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(result.Repaired, Is.True);
            Assert.That(result.RepairCount, Is.EqualTo(5));
            Assert.That(backend.Modes, Is.EqualTo(new[] { "rejected" }));
            Assert.That(local.Calls, Is.EqualTo(0));
        }

        [TestCase("method_not_supported")]
        [TestCase("ipc_disconnected")]
        public async Task WithoutTheBackendTheLocalRepairRuns(string code)
        {
            var backend = new RecordingBackend(code);
            var local = new LocalRepair();
            var service = new AuthenticationThreadHistoryRepairService(() => backend.Client, local, null);
            var result = await service.TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(result.Repaired, Is.True);
            Assert.That(local.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task AFileStillBeingWrittenWaitsForARestart()
        {
            var service = new AuthenticationThreadHistoryRepairService(() => new RecordingBackend("session_changed").Client, new LocalRepair(), null);
            var result = await service.TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(result.Repaired, Is.False);
            Assert.That(result.IsFinal, Is.False);
        }

        [Test]
        public async Task CompactionsAreReplacedOnlyAfterTheUserAgrees()
        {
            var nothing = new Dictionary<string, object>(Nothing()) { ["remainingCompactions"] = 2 };
            var asked = new List<int>();
            var declined = new RecordingBackend(nothing);
            var result = await new AuthenticationThreadHistoryRepairService(() => declined.Client, new LocalRepair(), count =>
            {
                asked.Add(count);
                return Task.FromResult(false);
            }).TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(asked, Is.EqualTo(new[] { 2 }));
            Assert.That(declined.Modes, Is.EqualTo(new[] { "rejected" }));
            Assert.That(result.Repaired, Is.False);
            Assert.That(result.IsFinal, Is.True);

            var agreed = new RecordingBackend(nothing, Repaired(2));
            result = await new AuthenticationThreadHistoryRepairService(() => agreed.Client, new LocalRepair(),
                count => Task.FromResult(true)).TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(agreed.Modes, Is.EqualTo(new[] { "rejected", "compaction" }));
            Assert.That(result.Repaired, Is.True);

            var none = new RecordingBackend(Nothing());
            result = await new AuthenticationThreadHistoryRepairService(() => none.Client, new LocalRepair(), count =>
                throw new AssertionException("Nothing to replace.")).TryRepairInvalidEncryptedContentAsync(Session());
            Assert.That(result.IsFinal, Is.True);
        }

        [Test]
        public async Task ARestartIsSkippedWhenNothingIsLeftToRepair()
        {
            var restarts = 0;
            var repairs = 0;
            var result = await InvalidEncryptedContentRepairWorkflow.RepairWithAppServerRestartFallbackAsync(
                () => Task.FromResult(0),
                () =>
                {
                    repairs++;
                    return Task.FromResult(ThreadHistoryRepairResult.Final("nothing to repair"));
                },
                () =>
                {
                    restarts++;
                    return Task.FromResult(0);
                });
            Assert.That(result.IsFinal, Is.True);
            Assert.That(repairs, Is.EqualTo(1));
            Assert.That(restarts, Is.EqualTo(0));
        }

        [Test]
        public async Task ResumeRepairRunsOnlyWhileTheOfficialProviderReadsUnifiedHistory()
        {
            var reads = 0;
            var modes = new List<string>();
            var repairOnResume = false;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                if (method == "gui/sessionHistory/get")
                {
                    reads++;
                    return Reply(new Dictionary<string, object>
                    {
                        { "unified", true }, { "pending", false }, { "repairOnResume", repairOnResume }
                    });
                }
                modes.Add((string)parameters["mode"]);
                return Reply(Repaired(1));
            });
            var repaired = 0;
            var gate = new SessionHistoryResumeRepairGate(() => client, result => repaired++);

            await gate.PrepareAsync(ThreadId);
            await gate.PrepareAsync("01a1263c-44bd-7800-8000-000000000002");
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(modes, Is.Empty);

            // A reconnect may bring the official provider into use.
            repairOnResume = true;
            gate.Reset();
            await gate.PrepareAsync(ThreadId);
            await gate.PrepareAsync("01a1263c-44bd-7800-8000-000000000002");
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(modes, Is.EqualTo(new[] { "resume", "resume" }));
            Assert.That(repaired, Is.EqualTo(2));
        }

        [Test]
        public async Task ResumeRepairNeverKeepsAThreadFromLoading()
        {
            var calls = 0;
            var failing = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                calls++;
                throw new AuthenticationCenterException("method_not_supported");
            });
            var gate = new SessionHistoryResumeRepairGate(() => failing);
            await gate.PrepareAsync(ThreadId);
            await gate.PrepareAsync(ThreadId);
            // An older backend is asked once per connection, not before every thread.
            Assert.That(calls, Is.EqualTo(1));

            var unreachable = 0;
            var absent = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                unreachable++;
                throw new AuthenticationCenterException("backend_unavailable");
            });
            var remote = new SessionHistoryResumeRepairGate(() => absent);
            await remote.PrepareAsync(ThreadId);
            await remote.PrepareAsync(ThreadId);
            Assert.That(unreachable, Is.EqualTo(1));

            var broken = new SessionHistoryResumeRepairGate(() => throw new InvalidOperationException("no backend"));
            await broken.PrepareAsync(ThreadId);
            await new SessionHistoryResumeRepairGate(() => failing).PrepareAsync(null);
        }

        private static ChatSession Session()
        {
            return new ChatSession { ThreadId = ThreadId, SessionPath = @"C:\codex\sessions\rollout.jsonl" };
        }

        private static Dictionary<string, object> Repaired(int reasoning)
        {
            return new Dictionary<string, object>
            {
                { "repaired", true }, { "skipped", null }, { "reasoning", reasoning }, { "ids", 0 },
                { "convertedCompactions", 0 }, { "encryptedParts", 0 }, { "replacedCompactions", 0 }, { "remainingCompactions", 0 }
            };
        }

        private static Dictionary<string, object> Nothing()
        {
            return new Dictionary<string, object>(Repaired(0)) { ["repaired"] = false, ["skipped"] = "nothing_to_repair" };
        }

        private static Task<IDictionary<string, object>> Reply(Dictionary<string, object> value)
        {
            return Task.FromResult<IDictionary<string, object>>(value);
        }

        private sealed class RecordingBackend
        {
            private readonly Queue<object> _replies;

            public RecordingBackend(params object[] replies)
            {
                _replies = new Queue<object>(replies);
                Client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                {
                    Modes.Add((string)parameters["mode"]);
                    var reply = _replies.Count > 1 ? _replies.Dequeue() : _replies.Peek();
                    if (reply is string code) throw new AuthenticationCenterException(code);
                    return Reply((Dictionary<string, object>)reply);
                });
            }

            public AuthenticationCenterClient Client { get; }

            public List<string> Modes { get; } = new List<string>();
        }

        private sealed class LocalRepair : IThreadHistoryRepairService
        {
            public int Calls { get; private set; }

            public Task<ThreadHistoryRepairResult> TryRepairInvalidEncryptedContentAsync(ChatSession session)
            {
                Calls++;
                return Task.FromResult(ThreadHistoryRepairResult.CreateRepaired(1));
            }
        }
    }
}
