using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteConversationProtocolContractTests
    {
        [Test]
        public void PaginationFixture_HasFrozenSha256()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "remote-conversation-pagination-v1.json");
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                Assert.That(System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(), Is.EqualTo("e7b596623d2e169ed33356ea3b9191944a6ef35b7b471851eeee5d20ad37255f"));
            }
        }
    }
}
