using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormModelConfigurationTests
    {
        [Test]
        public void DefaultGettersUseOfficialConfigInsteadOfCatalogRecommendation()
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            Set(form, "_appConfigSnapshot", new AppConfigSnapshot());
            Set(form, "_sessionDefaultsService", new SessionDefaultsService());
            Set(form, "_availableModels", new List<AppServerModelOption>
            {
                new AppServerModelOption { ModelId = "recommended", IsDefault = true, DefaultReasoningEffort = "medium" }
            });
            var official = new AppServerConfigSnapshot { ModelId = "configured", ModelReasoningEffort = "high" };
            Set(form, "_globalServerConfigSnapshot", official);
            Assert.That(FormTestDriver.Invoke(form, "GetConfiguredDefaultModelId"), Is.EqualTo("configured"));
            Assert.That(FormTestDriver.Invoke(form, "GetConfiguredDefaultReasoningEffort"), Is.EqualTo("high"));
            official.ModelId = "switched";
            official.ModelReasoningEffort = "low";
            Assert.That(FormTestDriver.Invoke(form, "GetConfiguredDefaultModelId"), Is.EqualTo("switched"));
            Assert.That(FormTestDriver.Invoke(form, "GetConfiguredDefaultReasoningEffort"), Is.EqualTo("low"));
        }

        private static void Set(MainForm form, string name, object value)
        {
            typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value);
        }
    }
}
