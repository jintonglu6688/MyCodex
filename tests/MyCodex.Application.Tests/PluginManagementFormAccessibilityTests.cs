using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class PluginManagementFormAccessibilityTests
    {
        [Test]
        public void RegistrationAction_HasAccessibleNamePredictableTabOrderAndClassicMenu()
        {
            using (var screenReader = new RecordingScreenReaderService())
            using (var form = CreateForm(screenReader))
            {
                var registerButton = GetField<Button>(form, "_registerMarketplaceButton");
                var installButton = GetField<Button>(form, "_installButton");
                var contextMenu = GetField<ContextMenu>(form, "_pluginContextMenu");
                var statusLabel = GetField<Label>(form, "_statusLabel");

                Assert.That(registerButton.AccessibleName, Is.Not.Null.And.Not.Empty);
                Assert.That(registerButton.TabIndex, Is.EqualTo(2));
                Assert.That(installButton.TabIndex, Is.EqualTo(3));
                Assert.That(registerButton.Enabled, Is.False);
                Assert.That(contextMenu, Is.TypeOf<ContextMenu>());
                Assert.That(statusLabel.TabStop, Is.False);
            }
        }

        [Test]
        public void SupportedActions_HaveSequentialTabOrderAndPreviewIsVisible()
        {
            using (var screenReader = new RecordingScreenReaderService())
            using (var form = CreateForm(screenReader))
            {
                Assert.That(GetField<Button>(form, "_uninstallButton").TabIndex, Is.EqualTo(4));
                Assert.That(GetField<Button>(form, "_openAuthButton").TabIndex, Is.EqualTo(5));
                Assert.That(GetField<Button>(form, "_openFolderButton").TabIndex, Is.EqualTo(6));
                Assert.That(GetField<Button>(form, "_closeButton").TabIndex, Is.EqualTo(7));
                Assert.That(form.Text, Does.Contain("Preview").Or.Contain("预览"));
            }
        }

        [Test]
        public void AnnouncedStatus_IsVisibleAndSentToScreenReader()
        {
            using (var screenReader = new RecordingScreenReaderService())
            using (var form = CreateForm(screenReader))
            {
                var method = typeof(PluginManagementForm).GetMethod(
                    "SetStatus",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), typeof(bool) },
                    null);
                Assert.That(method, Is.Not.Null);

                method.Invoke(form, new object[] { "Marketplace registered.", true });

                Assert.That(GetField<Label>(form, "_statusLabel").Text, Is.EqualTo("Marketplace registered."));
                Assert.That(screenReader.LastSpokenText, Is.EqualTo("Marketplace registered."));
            }
        }

        private static PluginManagementForm CreateForm(IScreenReaderService screenReaderService)
        {
#pragma warning disable SYSLIB0050
            var appServerClient = (IAppServerClient)FormatterServices.GetUninitializedObject(typeof(MyCodex.Infrastructure.AppServer.AppServerClient));
#pragma warning restore SYSLIB0050
            return new PluginManagementForm(
                appServerClient,
                new StubPluginMarketCatalogClient(),
                screenReaderService);
        }

        private static T GetField<T>(object instance, string name)
            where T : class
        {
            var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Field not found: " + name);
            return field.GetValue(instance) as T;
        }

        private sealed class StubPluginMarketCatalogClient : IPluginMarketCatalogClient
        {
            public Task<PluginMarketCatalogSnapshot> LoadIndexAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<PluginMarketSnapshot> LoadMarketAsync(string manifestUrl, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RecordingScreenReaderService : IScreenReaderService
        {
            public string LastSpokenText { get; private set; }

            public bool IsAvailable { get { return true; } }

            public bool IsInitialized { get { return true; } }

            public string LastError { get { return null; } }

            public bool EnsureInitialized() { return true; }

            public bool IsReaderRunning() { return true; }

            public int GetReaderState() { return 1; }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                LastSpokenText = text;
                return true;
            }

            public bool Stop(bool useSlave) { return true; }

            public void Dispose() { }
        }
    }
}
