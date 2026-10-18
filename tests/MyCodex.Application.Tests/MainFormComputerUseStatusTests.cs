using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormComputerUseStatusTests
    {
        [TestCase(false, false, "Ready")]
        [TestCase(true, false, "Other important error")]
        [TestCase(false, true, "CUA failed")]
        public void Recovery_ReplacesOnlyItsOwnVisibleWarning(bool otherError, bool otherThread, string expected)
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            GC.SuppressFinalize(form);
            using (var label = new ToolStripStatusLabel())
            using (var indicator = new ToolStripStatusLabel())
            {
                FormTestDriver.SetField(form, "_statusLabel", label);
                FormTestDriver.SetField(form, "_statusIndicator", indicator);
                try
                {
                    Show(form, new AppServerEventArgs("protocol_warning", "CUA failed", threadId: "thread", reason: "computer_use/PreparationFailed"), true);
                    if (otherError) Invoke(form, "SetHighPriorityStatus", new[] { typeof(string) }, "Other important error");
                    Show(form, new AppServerEventArgs("status", "Ready", threadId: otherThread ? "other" : "thread", reason: "computer_use/Ready"), false);
                    Assert.That(label.Text, Is.EqualTo(expected));
                }
                finally { indicator.Image?.Dispose(); }
            }
        }

        private static void Show(MainForm form, AppServerEventArgs e, bool high)
        {
            Invoke(form, "SetAppServerStatus", new[] { typeof(AppServerEventArgs), typeof(bool), typeof(string) }, e, high, null);
        }
        private static void Invoke(object value, string name, Type[] types, params object[] args)
        { typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, types, null).Invoke(value, args); }
    }
}
