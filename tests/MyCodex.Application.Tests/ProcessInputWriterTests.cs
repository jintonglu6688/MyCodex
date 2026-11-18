using System.IO;
using System.Text;
using MyCodex.Infrastructure.Processes;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ProcessInputWriterTests
    {
        [Test]
        public void CreateUtf8_WritesUnicodeJsonLinesWithoutBomOrCarriageReturn()
        {
            var line = "{\"path\":\"C:\\\\Users\\\\用户陈\\\\电脑控制\"}";
            var stream = new MemoryStream();
            using (var writer = ProcessInputWriter.CreateUtf8(stream))
            {
                writer.WriteLine(line);
                // AutoFlush: the child must receive each message without waiting for dispose.
                Assert.That(stream.ToArray(), Is.EqualTo(new UTF8Encoding(false).GetBytes(line + "\n")));
            }
        }
    }
}
