using System.Collections.Generic;
using System.Linq;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerJsonValueReaderTests
    {
        [Test]
        public void GetString_ReturnsExpectedValues()
        {
            var source = new Dictionary<string, object>
            {
                { "name", "demo" },
                { "count", 3 }
            };

            Assert.That(AppServerJsonValueReader.GetString(source, "name"), Is.EqualTo("demo"));
            Assert.That(AppServerJsonValueReader.GetString(source, "count"), Is.EqualTo("3"));
            Assert.That(AppServerJsonValueReader.GetString(source, "missing"), Is.Null);
        }

        [Test]
        public void GetNullableInt_ParsesOrReturnsNull()
        {
            var source = new Dictionary<string, object>
            {
                { "a", "10" },
                { "b", "bad" }
            };

            Assert.That(AppServerJsonValueReader.GetNullableInt(source, "a"), Is.EqualTo(10));
            Assert.That(AppServerJsonValueReader.GetNullableInt(source, "b"), Is.Null);
            Assert.That(AppServerJsonValueReader.GetNullableInt(source, "c"), Is.Null);
        }

        [Test]
        public void GetArray_ReadsObjectArrayAndEnumerable()
        {
            var source = new Dictionary<string, object>
            {
                { "array", new object[] { "a", "b" } },
                { "list", new List<object> { 1, 2 } }
            };

            Assert.That(AppServerJsonValueReader.GetArray(source, "array").ToArray(), Is.EqualTo(new object[] { "a", "b" }));
            Assert.That(AppServerJsonValueReader.GetArray(source, "list").ToArray(), Is.EqualTo(new object[] { 1, 2 }));
            Assert.That(AppServerJsonValueReader.GetArray(source, "missing").Any(), Is.False);
        }
    }
}
