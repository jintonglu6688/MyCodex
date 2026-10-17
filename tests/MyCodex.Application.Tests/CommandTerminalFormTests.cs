using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class CommandTerminalFormTests
    {
        [Test]
        public void CommandTerminalForm_UsesStableFocusOrderAndSubmitsInputWithNewline()
        {
            var buffer = new CommandTerminalBuffer(1024);
            var task = new CommandTask
            {
                ProcessId = "process-1",
                CommandText = "npm run dev",
                WorkingDirectory = "E:\\MyCodex",
                Status = "running",
                IsWaitingForInput = true
            };

            using (var form = new CommandTerminalForm(task, buffer, true, true))
            {
                string submitted = null;
                form.InputSubmitted += (sender, e) => submitted = e.InputText;
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var input = FindControl<TextBox>(form, "输入");
                var send = FindControl<Button>(form, "发送");
                var presets = FindControl<Button>(form, "常用");
                var stop = FindControl<Button>(form, "停止");
                var close = FindControl<Button>(form, "关闭");

                Assert.That(input.TabIndex, Is.EqualTo(0));
                Assert.That(send.TabIndex, Is.EqualTo(1));
                Assert.That(presets.TabIndex, Is.EqualTo(2));
                Assert.That(stop.TabIndex, Is.EqualTo(3));
                Assert.That(close.TabIndex, Is.EqualTo(4));

                input.Text = "yes";
                send.PerformClick();

                Assert.That(submitted, Is.EqualTo("yes" + "\n"));
                Assert.That(input.Text, Is.Empty);
            }
        }

        [Test]
        public void CommandTerminalForm_SubmitsCommandWhenNoCommandIsRunning()
        {
            var buffer = new CommandTerminalBuffer(1024);

            using (var form = new CommandTerminalForm(null, buffer, true, false, false))
            {
                string submitted = null;
                form.CommandSubmitted += (sender, e) => submitted = e.CommandText;
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var input = FindControl<TextBox>(form, "输入");
                var send = FindControl<Button>(form, "发送");

                Assert.That(input.Enabled, Is.True);
                Assert.That(send.Enabled, Is.True);

                input.Text = " ping 8.8.8.8 ";
                send.PerformClick();

                Assert.That(submitted, Is.EqualTo("ping 8.8.8.8"));
                Assert.That(input.Text, Is.Empty);
            }
        }

        [Test]
        public void CommandTerminalForm_PresetButtonRaisesPresetRequest()
        {
            using (var form = new CommandTerminalForm(null, new CommandTerminalBuffer(1024), true, false, false))
            {
                var requested = false;
                form.PresetsRequested += (sender, e) => requested = true;
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var presetButton = FindControl<Button>(form, "常用");
                presetButton.PerformClick();

                Assert.That(requested, Is.True);
            }
        }

        [Test]
        public void CommandPresetForm_InsertSelectedReturnsSelectedCommand()
        {
            var definitions = new[]
            {
                new CommandDefinition
                {
                    Name = "Ping",
                    CommandText = "ping 8.8.8.8",
                    HostPlatform = CommandHostPlatform.Any
                }
            };

            using (var form = new CommandPresetForm(definitions, AppServerHostPlatform.Windows))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var insertButton = FindControl<Button>(form, "插入");
                insertButton.PerformClick();

                Assert.That(form.SelectedAction, Is.EqualTo(CommandPresetAction.Insert));
                Assert.That(form.SelectedCommandText, Is.EqualTo("ping 8.8.8.8"));
            }
        }

        [Test]
        public void CommandTerminalForm_ForwardsScreenReaderHotkeys()
        {
            var buffer = new CommandTerminalBuffer(1024);
            var task = new CommandTask
            {
                ProcessId = "process-1",
                Status = "running"
            };

            using (var form = new CommandTerminalForm(task, buffer, true, true))
            {
                Keys forwarded = Keys.None;
                form.ScreenReaderHotkeyPressed += (sender, e) =>
                {
                    forwarded = e.KeyData;
                    e.Handled = true;
                };

                var handled = form.ProcessTerminalShortcutForTesting(Keys.Alt | Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(forwarded, Is.EqualTo(Keys.Alt | Keys.Down));
            }
        }

        [Test]
        public void CommandTerminalForm_CalculatesPositiveTerminalSize()
        {
            using (var form = new CommandTerminalForm(null, new CommandTerminalBuffer(1024), true, false, false))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var size = form.GetTerminalSizeForTests();

                Assert.That(size.Rows, Is.GreaterThanOrEqualTo(1));
                Assert.That(size.Cols, Is.GreaterThanOrEqualTo(1));
            }
        }

        private static T FindControl<T>(Control root, string accessibleName)
            where T : Control
        {
            var match = root
                .Controls
                .Cast<Control>()
                .SelectMany(Flatten)
                .OfType<T>()
                .FirstOrDefault(control => control.AccessibleName == accessibleName);

            Assert.That(match, Is.Not.Null, "Control not found: " + accessibleName);
            return match;
        }

        private static System.Collections.Generic.IEnumerable<Control> Flatten(Control control)
        {
            yield return control;

            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
