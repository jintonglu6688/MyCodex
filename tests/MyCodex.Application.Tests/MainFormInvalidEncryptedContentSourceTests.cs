using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormInvalidEncryptedContentSourceTests
    {
        [Test]
        public void AppServerEvents_HandlesInvalidEncryptedContentBeforeDisplayingRawError()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.AppServerEvents.cs");
            var errorBlockStart = source.IndexOf("if (AppServerEventRouting.IsErrorDisplayEvent(route))", StringComparison.Ordinal);
            var repairCall = source.IndexOf("TryHandleInvalidEncryptedContentFailure(route, e)", StringComparison.Ordinal);
            var rawErrorDisplay = source.IndexOf("AddThreadEventHistoryItem(e, GetErrorSenderText(), e.Content, ChatHistoryCategory.Error);", StringComparison.Ordinal);

            Assert.That(errorBlockStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(repairCall, Is.GreaterThan(errorBlockStart));
            Assert.That(rawErrorDisplay, Is.GreaterThan(repairCall));
        }

        [Test]
        public void MessageSending_RegistersInvalidEncryptedContentRetryBeforeTurnStart()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.MessageSending.cs");
            var registerCall = source.IndexOf("RegisterPendingInvalidEncryptedContentRetry(", StringComparison.Ordinal);
            var sendCall = source.IndexOf("var sendResult = await _sessionThreadService.SendUserMessageAsync(", StringComparison.Ordinal);

            Assert.That(registerCall, Is.GreaterThanOrEqualTo(0));
            Assert.That(sendCall, Is.GreaterThan(registerCall));
        }
    }
}
