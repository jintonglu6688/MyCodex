using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteConversationDiagnosticsTests
    {
        [TestCase("BODY_SENTINEL_DO_NOT_LOG")]
        [TestCase("CURSOR_SENTINEL_DO_NOT_LOG")]
        [TestCase("SECRET_SENTINEL_DO_NOT_LOG")]
        public void PaginationImplementation_DoesNotLogSensitiveValues(string sentinel)
        {
            var sourceRoot = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "src"));
            var text = File.ReadAllText(Path.Combine(sourceRoot, "MyCodex.Infrastructure", "RemoteRelay", "AppServerCodingRemoteThreadMessageReader.cs")) +
                File.ReadAllText(Path.Combine(sourceRoot, "MyCodex.Application", "CodingRemote", "RemoteCommandDispatcher.cs"));
            Assert.That(text, Does.Not.Contain(sentinel));
        }
    }
}
