using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.Architecture)]
    [TestFixture]
    public sealed class RemoteConversationDiagnosticsTests
    {
        [TestCase("BODY_SENTINEL_DO_NOT_LOG")]
        [TestCase("CURSOR_SENTINEL_DO_NOT_LOG")]
        [TestCase("SECRET_SENTINEL_DO_NOT_LOG")]
        public void PaginationImplementation_DoesNotLogSensitiveValues(string sentinel)
        {
            var text = RepositorySource.ReadSourceFile("AppServerCodingRemoteThreadMessageReader.cs") +
                RepositorySource.ReadSourceFile("RemoteCommandDispatcher.cs");
            Assert.That(text, Does.Not.Contain(sentinel));
        }
    }
}
