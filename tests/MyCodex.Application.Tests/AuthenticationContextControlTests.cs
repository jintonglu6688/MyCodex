using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationContextControlTests
    {
        [Test]
        public void UntouchedNon1MValuesSurviveAndToggleUsesCcDefaults()
        {
            var original = new Dictionary<string, object>
            {
                { "modelContextWindow", 400000L }, { "autoCompactTokenLimit", 300000L }, { "remoteCompaction", true }
            };
            using (var control = new AuthenticationContextControl(original, false, true))
            {
                var window = Field<CheckBox>(control, "_window");
                var limit = Field<TextBox>(control, "_limit");
                Assert.That(window.Checked, Is.False);
                Assert.That(limit.Enabled, Is.False);
                var result = new Dictionary<string, object>(original);
                control.Apply(result);
                Assert.That(result, Is.EquivalentTo(original));
                window.Checked = true;
                control.Apply(result);
                Assert.That(result["modelContextWindow"], Is.EqualTo(1000000L));
                Assert.That(result["autoCompactTokenLimit"], Is.EqualTo(300000L), "Existing thresholds are retained when first enabling 1M.");
                window.Checked = false;
                control.Apply(result);
                Assert.That(result["modelContextWindow"], Is.Null);
                Assert.That(result["autoCompactTokenLimit"], Is.Null);
                window.Checked = true;
                control.Apply(result);
                Assert.That(result["autoCompactTokenLimit"], Is.EqualTo(900000L), "Re-enabling after deletion uses CC's default.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BackendCapabilityControlsEditing(bool supported)
        {
            using (var control = new AuthenticationContextControl(null, false, supported))
            {
                Assert.That(Field<CheckBox>(control, "_window").Enabled, Is.EqualTo(supported));
                Assert.That(Field<CheckBox>(control, "_remote").Enabled, Is.EqualTo(supported));
                Field<CheckBox>(control, "_window").Checked = true;
                Field<CheckBox>(control, "_remote").Checked = true;
                var result = new Dictionary<string, object>();
                control.Apply(result);
                Assert.That(result.Count, Is.EqualTo(supported ? 3 : 0));
                if (supported) { Assert.That(result["autoCompactTokenLimit"], Is.EqualTo(900000L)); }
            }
        }

        [TestCase("chatgpt")]
        [TestCase("official_api")]
        [TestCase("responses")]
        [TestCase("chat_completions")]
        [TestCase("anthropic")]
        public async Task ProviderSaveIncludesContextAndPreservesUnrelatedSettings(string kind)
        {
            AuthenticationProviderEdit saved = null;
            var provider = new AuthenticationProvider
            {
                Id = "test", Name = "Test", Kind = kind, Model = "model", AccountId = "account",
                BaseUrl = "https://example.test/v1", Advanced = new Dictionary<string, object> { { "unrepresented", "keep" } }
            };
            var client = new AuthenticationCenterClient("Test", (method, args, token) => throw new InvalidOperationException(method));
            using (var form = new AuthenticationProviderEditForm(client, provider, supportsContextSettings: true, save: (edit, token) =>
            {
                saved = edit;
                return Task.FromResult(new AuthenticationSaveResult { Provider = provider, Reconnected = true });
            }))
            {
                var context = Field<AuthenticationContextControl>(form, "_context");
                Field<CheckBox>(context, "_window").Checked = true;
                Field<TextBox>(context, "_limit").Text = "850000";
                if (kind != "chatgpt") { Field<CheckBox>(context, "_remote").Checked = true; }
                await form.SaveAsync();
                Assert.That(saved.Advanced["modelContextWindow"], Is.EqualTo(1000000L));
                Assert.That(saved.Advanced["autoCompactTokenLimit"], Is.EqualTo(850000L));
                Assert.That(saved.Advanced["unrepresented"], Is.EqualTo("keep"));
                Assert.That(saved.Models, Is.Empty, "Context settings must not create a custom model catalog.");
            }
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("")]
        [TestCase("9223372036854775808")]
        public void InvalidThresholdIsRejected(string value)
        {
            using (var control = new AuthenticationContextControl(null, false, true))
            {
                Field<CheckBox>(control, "_window").Checked = true;
                Field<TextBox>(control, "_limit").Text = value;
                Assert.Throws<AuthenticationCenterException>(() => control.Apply(new Dictionary<string, object>()));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapabilityIsReadFromEachTarget(bool supported)
        {
            var client = new AuthenticationCenterClient("Test", (method, args, token) =>
                Task.FromResult<IDictionary<string, object>>(method == "status"
                    ? new Dictionary<string, object>
                    {
                        { "protocolVersion", 3 }, { "capabilities", supported ? new[] { "contextSettings" } : new string[0] },
                        { "route", new Dictionary<string, object>() }
                    }
                    : new Dictionary<string, object> { { "providers", new object[0] } }));
            Assert.That((await client.ReadAsync(CancellationToken.None)).SupportsContextSettings, Is.EqualTo(supported));
        }

        [TestCase("responses")]
        [TestCase("chatgpt")]
        public async Task KeyboardAndValidationReachContextControlsAtMinimumSize(string kind)
        {
            var client = new AuthenticationCenterClient("Test", (method, args, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "accounts", new object[] { new Dictionary<string, object> { { "id", "account" }, { "label", "Test account" } } } }
                }));
            using (var form = new AuthenticationProviderEditForm(client, new AuthenticationProvider
            {
                Id = "test", Name = "Test", Kind = kind, Model = "model", AccountId = "account", BaseUrl = "https://example.test/v1"
            }, supportsContextSettings: true))
            {
                form.ShowInTaskbar = false;
                form.Size = form.MinimumSize;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var context = Field<AuthenticationContextControl>(form, "_context");
                var remote = Field<CheckBox>(context, "_remote");
                var window = Field<CheckBox>(context, "_window");
                var limit = Field<TextBox>(context, "_limit");
                Assert.That(remote.Visible, Is.EqualTo(kind != "chatgpt"));
                Assert.That(window.AccessibleName, Is.Not.Empty);
                Assert.That(limit.AccessibleName, Is.Not.Empty);
                if (remote.Visible)
                {
                    remote.Focus();
                    form.SelectNextControl(remote, true, true, true, false);
                    Assert.That(window.Focused, Is.True);
                }
                window.Checked = true;
                window.Focus();
                form.SelectNextControl(window, true, true, true, false);
                Assert.That(limit.Focused, Is.True);
                var content = Field<Panel>(form, "_content");
                Assert.That(content.ClientRectangle.Contains(content.PointToClient(limit.PointToScreen(new System.Drawing.Point(1, limit.Height - 1)))), Is.True);
                Assert.That(content.HorizontalScroll.Visible, Is.False);
                form.SelectNextControl(limit, true, true, true, false);
                Assert.That(Field<Button>(form, "_saveButton").Focused, Is.True);
                limit.Text = "invalid";
                await form.SaveAsync();
                Assert.That(limit.Focused, Is.True, "Invalid input stays editable and receives focus before the form is disabled.");
            }
        }

        private static T Field<T>(object value, string name) where T : class
        {
            return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value) as T;
        }
    }
}
