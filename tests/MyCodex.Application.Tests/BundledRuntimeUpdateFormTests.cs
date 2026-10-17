using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Codex;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class BundledRuntimeUpdateFormTests
    {
        [Test]
        public void FailureRetainsTheModalWorkflowAndRetryOnlyCompletesOnTheRequiredVersion()
        {
            var attempts = 0;
            using (var form = new BundledRuntimeUpdateForm(Info(), (status, progress, token) =>
            {
                attempts++;
                if (attempts == 1) throw new InvalidOperationException("synthetic download failure");
                return Task.FromResult(new CodexRuntimeUpdateResult { Version = "0.162.1", TargetTriple = Info().TargetTriple });
            }, CancellationToken.None))
            {
                form.RunUpdateAsync().GetAwaiter().GetResult();
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(Field<Label>(form, "_statusLabel").Text, Does.Contain("synthetic download failure"));
                Assert.That(Field<Button>(form, "_retryButton").Enabled, Is.True);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(460));
                Assert.That(Field<Label>(form, "_statusLabel").TabStop, Is.False);
                Assert.That(Field<ProgressBar>(form, "_progressBar").TabStop, Is.False);
                Assert.That(Field<ProgressBar>(form, "_progressBar").AccessibleName, Is.Not.Empty);
                Assert.That(form.Controls.OfType<TextBox>(), Is.Empty);
                Assert.That(Field<Button>(form, "_retryButton").AccessibleName, Is.Not.Empty);
                Assert.That(Field<Button>(form, "_cancelButton").AccessibleName, Is.Not.Empty);
                Assert.That(Field<Button>(form, "_retryButton").TabIndex, Is.LessThan(Field<Button>(form, "_cancelButton").TabIndex));
                Assert.That(form.CancelButton, Is.SameAs(Field<Button>(form, "_cancelButton")));
                form.RunUpdateAsync().GetAwaiter().GetResult();
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.OK));
                Assert.That(attempts, Is.EqualTo(2));
            }
        }

        [TestCase("0.162.0", "x86_64-pc-windows-msvc")]
        [TestCase("0.162.1", "aarch64-apple-darwin")]
        public void IncompleteOrWrongPlatformResultNeverReportsSuccess(string version, string target)
        {
            using (var form = new BundledRuntimeUpdateForm(Info(), (status, progress, token) => Task.FromResult(
                new CodexRuntimeUpdateResult { Version = version, TargetTriple = target }), CancellationToken.None))
            {
                form.RunUpdateAsync().GetAwaiter().GetResult();
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(Field<Button>(form, "_retryButton").Enabled, Is.True);
            }
        }

        [TestCase("zh-CN")]
        [TestCase("en-US")]
        public void ActionsHaveUniqueKeyboardMnemonics(string culture)
        {
            var previous = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
                using (var form = new BundledRuntimeUpdateForm(Info(), (status, progress, token) => throw new AssertionException("Must not install while inspecting controls."), CancellationToken.None))
                {
                    var texts = new[] { Field<Button>(form, "_retryButton").Text, Field<Button>(form, "_cancelButton").Text };
                    var keys = texts.Select(text => char.ToUpperInvariant(text[text.IndexOf('&') + 1])).ToArray();
                    Assert.That(texts.All(text => text.Contains("&")), Is.True);
                    Assert.That(keys.Distinct().Count(), Is.EqualTo(2));
                }
            }
            finally { Thread.CurrentThread.CurrentUICulture = previous; }
        }

        [Test]
        public void ClosingAFailedUpdateCancelsWithoutReportingSuccess()
        {
            using (var form = new BundledRuntimeUpdateForm(Info(), (status, progress, token) =>
                throw new InvalidOperationException("synthetic failure"), CancellationToken.None))
            {
                form.RunUpdateAsync().GetAwaiter().GetResult();
                var args = new FormClosingEventArgs(CloseReason.UserClosing, false);
                typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { args });
                Assert.That(args.Cancel, Is.False);
                Assert.That(form.DialogResult, Is.EqualTo(DialogResult.Cancel));
                Assert.That(form.UpdateResult, Is.Null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeProgressDoesNotTakeFocusAndCancellationStopsTheUpdate(bool closeWindow)
        {
            var cancelled = false;
            var observedProgress = false;
            var focusStayedOnCancel = false;
            using (var form = new BundledRuntimeUpdateForm(Info(), async (status, progress, token) =>
            {
                progress.Report(new MyCodex.Infrastructure.Updates.UpdateDownloadProgress { Percentage = 37 });
                try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return null;
            }, CancellationToken.None))
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                timer.Tick += (sender, args) =>
                {
                    var progress = Field<ProgressBar>(form, "_progressBar");
                    if (progress.Value != 37 && elapsed.Elapsed < TimeSpan.FromSeconds(3)) return;
                    timer.Stop();
                    observedProgress = progress.Value == 37 && progress.Style == ProgressBarStyle.Continuous;
                    var cancel = Field<Button>(form, "_cancelButton");
                    focusStayedOnCancel = form.ActiveControl == cancel;
                    form.SelectNextControl(cancel, true, true, true, true);
                    focusStayedOnCancel &= form.ActiveControl == cancel;
                    if (closeWindow) form.Close();
                    else cancel.PerformClick();
                };
                timer.Start();
                var previousContext = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    Assert.That(form.ShowDialog(), Is.EqualTo(DialogResult.Cancel));
                }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
                Assert.That(observedProgress, Is.True);
                Assert.That(focusStayedOnCancel, Is.True);
                Assert.That(cancelled, Is.True);
                Assert.That(form.UpdateResult, Is.Null);
            }
        }

        private static T Field<T>(object form, string name) => (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        private static CodexRuntimeUpdateInfo Info() => new CodexRuntimeUpdateInfo
        { InstalledVersion = "0.162.0", LatestVersion = "0.162.1", TargetTriple = "x86_64-pc-windows-msvc" };
    }
}
