using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [Test]
        public void RepeatedCloseWaitsForCommittedTaskCleanup()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                Assert.That(((Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", "commit-cleanup", false)).GetAwaiter().GetResult(), Is.True);
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "commit-cleanup"), Is.True);
                var closing = typeof(MainForm).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                var waiting = new FormClosingEventArgs(CloseReason.UserClosing, false);
                closing.Invoke(fixture.Form, new object[] { fixture.Form, waiting });
                Assert.That(waiting.Cancel, Is.True);
                FormTestDriver.SetField(fixture.Form, "_targetExitCleanupComplete", true);
                var completed = new FormClosingEventArgs(CloseReason.UserClosing, false);
                closing.Invoke(fixture.Form, new object[] { fixture.Form, completed });
                Assert.That(completed.Cancel, Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActiveDetachedResponseCanPrepareButDraftSaveFailureCancelsExit(bool recoveryPending)
        {
            using (var fixture = new TargetFormFixture())
            using (var workbench = new SessionWorkbenchForm(new SessionWorkbenchContext(
                new ChatSession { Id = "active", ThreadId = "working" }, new WorkspaceInfo(), null, "draft"),
                new SessionWorkbenchOperations { IsWorking = session => true, SaveDraft = draft => { throw new System.IO.IOException("save failed"); } }))
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                FormTestDriver.SetField(fixture.Form, "_isPersistenceRecoveryPending", recoveryPending);
                FormTestDriver.Field<Dictionary<string, SessionWorkbenchForm>>(fixture.Form, "_sessionWorkbenches").Add("active", workbench);
                Assert.That(workbench.IsBusy, Is.True);
                Assert.That(workbench.HasPendingTargetOperation, Is.False);
                Assert.That(((Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", "draft-failure", false)).GetAwaiter().GetResult(), Is.False);
                Assert.That(fixture.Form.Enabled, Is.True);
                Assert.That(workbench.Enabled, Is.True);
                Assert.That(workbench.IsBusy, Is.True);
                Assert.That(fixture.State.SaveCalls, Is.Zero);
            }
        }

        [Test]
        public void ExitPreparationSaveFailureLeavesThisAndOtherTargetsRunning()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                fixture.State.FailSave = true;
                var busy = FormTestDriver.Field<HashSet<string>>(fixture.Form, "_busyThreadIds");
                busy.Add("still-working");
                var ready = ((Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", "save-failure", false)).GetAwaiter().GetResult();

                Assert.That(ready, Is.False);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                Assert.That(fixture.Form.Enabled, Is.True);
                Assert.That(busy, Does.Contain("still-working"));
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
                Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void ExitPreparationCanBeCancelledAfterSavingWithoutEndingExistingWork()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var busy = FormTestDriver.Field<HashSet<string>>(fixture.Form, "_busyThreadIds");
                busy.Add("still-working");
                Assert.That(((Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", "cancel-ready", false)).GetAwaiter().GetResult(), Is.True);
                Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                Assert.That(fixture.Form.Enabled, Is.False);
                Assert.That(busy, Does.Contain("still-working"));

                FormTestDriver.Invoke(fixture.Form, "CancelTargetExitPreparation", "cancel-ready");

                Assert.That(fixture.Form.Enabled, Is.True);
                Assert.That(busy, Does.Contain("still-working"));
                Assert.That((bool)FormTestDriver.Invoke(fixture.Form, "CommitTargetExit", "cancel-ready"), Is.False);
                Assert.That(fixture.Client.RestartCalls, Is.Zero);
            }
        }

        [Test]
        public void ExitPromptIncludesTheActualTargetName()
        {
            using (var fixture = new TargetFormFixture())
                Assert.That(FormTestDriver.Invoke(fixture.Form, "BuildApplicationExitPrompt"), Does.Contain((string)FormTestDriver.Invoke(fixture.Form, "GetCurrentRuntimeDisplayName")));
        }

        [Test]
        public void ExitAllEntryUsesAUniqueFileAndTrayMnemonic()
        {
            using (var fixture = new TargetFormFixture())
            {
                foreach (var menu in new[] { FormTestDriver.Field<MenuItem>(fixture.Form, "_fileMenuItem").MenuItems,
                    FormTestDriver.Field<ContextMenu>(fixture.Form, "_trayContextMenu").MenuItems })
                {
                    var mnemonics = menu.Cast<MenuItem>().Select(item => item.Text.Replace("&&", string.Empty))
                        .Where(text => text.IndexOf('&') >= 0 && text.IndexOf('&') + 1 < text.Length)
                        .Select(text => char.ToUpperInvariant(text[text.IndexOf('&') + 1])).ToArray();
                    Assert.That(mnemonics.Count(value => value == 'Q'), Is.EqualTo(1));
                    Assert.That(mnemonics.Distinct().Count(), Is.EqualTo(mnemonics.Length));
                }
            }
        }

        [Test]
        public void ActualAllExitDialogAllowsTheOwnerToPrepareAndCancelWithoutClosing()
        {
            using (var fixture = new TargetFormFixture())
            {
                FormTestDriver.Invoke(fixture.Form, "CompleteTargetStartup");
                var instance = FormTestDriver.Field<TargetInstanceCoordinator>(fixture.Form, "_targetInstance").InstanceRecord;
                var coordinator = new TargetExitAllCoordinator(instance,
                    () => new[] { new TargetInstanceReply { Success = true, Instance = instance,
                        Status = new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Running } } },
                    async (owner, request) =>
                    {
                        if (request.Operation == TargetInstanceOperation.AbortExit)
                        {
                            FormTestDriver.Invoke(fixture.Form, "CancelTargetExitPreparation", request.OperationId);
                            return new TargetInstanceReply { Success = true, Instance = instance };
                        }
                        var ready = await (Task<bool>)FormTestDriver.Invoke(fixture.Form, "PrepareTargetExitAsync", request.OperationId, false);
                        return new TargetInstanceReply { Success = ready, Instance = instance,
                            ExitPreparation = ready ? MyCodex.Infrastructure.Instances.TargetExitPreparation.Ready
                                : MyCodex.Infrastructure.Instances.TargetExitPreparation.Rejected };
                    }, owner => true);
                using (var form = new TargetExitAllForm(coordinator, owner => "Test target", "Test MyCodex"))
                {
                    Exception error = null;
                    var ready = false;
                    FormTestDriver.SetField(fixture.Form, "_allTargetExitForm", form);
                    form.Shown += async (sender, args) =>
                    {
                        try
                        {
                            for (var attempt = 0; attempt < 300 && !FormTestDriver.Field<Button>(form, "_exit").Enabled; attempt++) await Task.Delay(10);
                            Assert.That(FormTestDriver.Field<Button>(form, "_exit").Enabled, Is.True, "The real dialog must finish loading its scope.");
                            await coordinator.PrepareAsync(System.Threading.CancellationToken.None);
                            ready = true;
                            await coordinator.AbortAsync();
                        }
                        catch (Exception ex) { error = ex; }
                        finally { form.DialogResult = DialogResult.Cancel; }
                    };
                    form.ShowDialog(fixture.Form);
                    Assert.That(error, Is.Null);
                    Assert.That(ready, Is.True);
                    Assert.That(FormTestDriver.Field<bool>(fixture.Form, "_isExitRequested"), Is.False);
                    Assert.That(fixture.Form.Enabled, Is.True);
                    Assert.That(fixture.Client.RestartCalls, Is.Zero);
                }
            }
        }
    }
}
