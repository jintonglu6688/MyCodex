using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionHistoryLoadCoalescingTests
    {
        [Test]
        public void ReloadingTheSameThread_JoinsTheReadInProgress()
        {
            var reads = new List<TaskCompletionSource<AppServerThreadDetails>>();
            var store = new SessionHistoryStateStore();
            var workflow = CreateWorkflow(store, reads);
            var session = Session("thread-1");

            var first = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            var second = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            Assert.That(reads.Count, Is.EqualTo(1), "A reload must not start a second full read.");

            reads[0].SetResult(Details("thread-1", "reply"));

            Assert.That(first.GetAwaiter().GetResult().Kind, Is.EqualTo(SessionHistoryLoadResultKind.Stale));
            var loaded = second.GetAwaiter().GetResult();
            Assert.That(loaded.Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
            Assert.That(loaded.HistoryItems.Select(item => item.FullText), Has.Member("reply"));
        }

        [Test]
        public void SupersededRead_IsStillCachedForTheNextVisit()
        {
            var reads = new List<TaskCompletionSource<AppServerThreadDetails>>();
            var store = new SessionHistoryStateStore();
            var workflow = CreateWorkflow(store, reads);
            var longSession = Session("long");
            var otherSession = Session("other");

            var longLoad = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(longSession, false), (s, id) => true, s => false);
            var otherLoad = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(otherSession, false), (s, id) => true, s => false);
            reads[1].SetResult(Details("other", "other reply"));
            otherLoad.GetAwaiter().GetResult();
            reads[0].SetResult(Details("long", "long reply"));

            Assert.That(longLoad.GetAwaiter().GetResult().Kind, Is.EqualTo(SessionHistoryLoadResultKind.Stale));
            var revisit = workflow.BuildRequest(longSession, false);
            Assert.That(revisit.RequiresRemoteRead, Is.False);
            Assert.That(revisit.ImmediateResult.Kind, Is.EqualTo(SessionHistoryLoadResultKind.CachedHistory));
            Assert.That(revisit.ImmediateResult.HistoryItems.Select(item => item.FullText), Has.Member("long reply"));
            Assert.That(reads.Count, Is.EqualTo(2));
        }

        [Test]
        public void InvalidationDuringRead_PreventsCachingAndStartsAFreshRead()
        {
            var reads = new List<TaskCompletionSource<AppServerThreadDetails>>();
            var store = new SessionHistoryStateStore();
            var workflow = CreateWorkflow(store, reads);
            var session = Session("thread-1");

            var outdated = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            store.Invalidate("thread-1"); // A new turn arrived while the read was in progress.
            var fresh = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            Assert.That(reads.Count, Is.EqualTo(2), "An invalidated read must not be joined.");

            reads[0].SetResult(Details("thread-1", "before new turn"));
            outdated.GetAwaiter().GetResult();
            ChatHistoryItem[] cached;
            Assert.That(store.TryGetCached("thread-1", out cached), Is.False, "Outdated history must not be cached.");

            reads[1].SetResult(Details("thread-1", "after new turn"));
            Assert.That(fresh.GetAwaiter().GetResult().Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
            Assert.That(store.TryGetCached("thread-1", out cached), Is.True);
            Assert.That(cached.Select(item => item.FullText), Has.Member("after new turn"));
        }

        [Test]
        public void FailedRead_IsRetriedByTheNextLoad()
        {
            var reads = new List<TaskCompletionSource<AppServerThreadDetails>>();
            var workflow = CreateWorkflow(new SessionHistoryStateStore(), reads);
            var session = Session("thread-1");

            var failed = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            reads[0].SetException(new InvalidOperationException("transient"));
            Assert.That(failed.GetAwaiter().GetResult().Kind, Is.EqualTo(SessionHistoryLoadResultKind.Error));

            var retry = workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
            Assert.That(reads.Count, Is.EqualTo(2), "A failed read must not be reused.");
            reads[1].SetResult(Details("thread-1", "recovered"));
            Assert.That(retry.GetAwaiter().GetResult().Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
        }

        private static SessionHistoryLoadWorkflow CreateWorkflow(
            SessionHistoryStateStore store,
            List<TaskCompletionSource<AppServerThreadDetails>> reads)
        {
            var builder = new SessionHistoryDisplayBuilder(
                "System",
                "Error",
                _ => "model",
                (sender, message, category) => new ChatHistoryItem { Sender = sender, FullText = message, DisplayText = message, Category = category },
                sender => ChatHistoryCategory.Conversation);
            return new SessionHistoryLoadWorkflow(
                store,
                builder,
                threadId =>
                {
                    var read = new TaskCompletionSource<AppServerThreadDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
                    reads.Add(read);
                    return read.Task;
                },
                _ => false,
                _ => false);
        }

        private static ChatSession Session(string threadId)
        {
            return new ChatSession { Id = "session-" + threadId, ThreadId = threadId, Title = threadId };
        }

        private static AppServerThreadDetails Details(string threadId, string reply)
        {
            var details = new AppServerThreadDetails { ThreadId = threadId, TurnCount = 1 };
            details.Messages.Add(new AppServerThreadMessage { ThreadId = threadId, TurnId = "turn-1", TurnIndex = 1, Sender = "Codex", Content = reply });
            return details;
        }
    }
}
