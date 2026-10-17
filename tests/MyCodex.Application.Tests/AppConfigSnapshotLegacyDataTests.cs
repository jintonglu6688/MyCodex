using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppConfigSnapshotLegacyDataTests
    {
        [Test]
        public void LegacyModelDefaults_RoundTripWithoutBecomingPublicAuthority()
        {
            const string json = "{\"DefaultModelId\":\"retired-model\",\"DefaultReasoningEffort\":\"retired-effort\"}";
            var serializer = new DataContractJsonSerializer(typeof(AppConfigSnapshot));
            AppConfigSnapshot snapshot;
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                snapshot = (AppConfigSnapshot)serializer.ReadObject(input);
            }

            string outputJson;
            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, snapshot);
                outputJson = Encoding.UTF8.GetString(output.ToArray());
            }

            var outputValues = new JavaScriptSerializer()
                .Deserialize<IDictionary<string, object>>(outputJson);
            Assert.That(outputValues["DefaultModelId"], Is.EqualTo("retired-model"));
            Assert.That(outputValues["DefaultReasoningEffort"], Is.EqualTo("retired-effort"));
        }
    }
}
