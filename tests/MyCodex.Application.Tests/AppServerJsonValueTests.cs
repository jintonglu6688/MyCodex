using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerJsonValueTests
    {
        [Test]
        public void DeserializeDictionary_ReturnsDictionaryForObjectRoot()
        {
            var result = AppServerJsonValue.DeserializeDictionary("{\"text\":\"hello\",\"count\":3}");

            Assert.That(AppServerJsonValue.GetString(result, "text"), Is.EqualTo("hello"));
            Assert.That(AppServerJsonValue.GetString(result, "count"), Is.EqualTo("3"));
        }

        [Test]
        public void DeserializeDictionary_ReturnsEmptyDictionaryForNonObjectRoot()
        {
            var result = AppServerJsonValue.DeserializeDictionary("[\"not\",\"an\",\"object\"]");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void GetDictionary_ReturnsNestedDictionaryWhenPresent()
        {
            var nested = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "value", "demo" }
            };
            var source = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "payload", nested }
            };

            var result = AppServerJsonValue.GetDictionary(source, "payload");

            Assert.That(result, Is.SameAs(nested));
        }

        [Test]
        public void GetDictionary_ReturnsEmptyDictionaryWhenMissingOrWrongType()
        {
            var source = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "payload", "not-object" }
            };

            Assert.That(AppServerJsonValue.GetDictionary(source, "missing"), Is.Empty);
            Assert.That(AppServerJsonValue.GetDictionary(source, "payload"), Is.Empty);
        }

        [Test]
        public void GetString_ConvertsPrimitiveValuesAndReturnsNullForMissingValues()
        {
            var source = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "number", 42 },
                { "flag", true },
                { "empty", null }
            };

            Assert.That(AppServerJsonValue.GetString(source, "number"), Is.EqualTo("42"));
            Assert.That(AppServerJsonValue.GetString(source, "flag"), Is.EqualTo("True"));
            Assert.That(AppServerJsonValue.GetString(source, "empty"), Is.Null);
            Assert.That(AppServerJsonValue.GetString(source, "missing"), Is.Null);
        }
    }
}
