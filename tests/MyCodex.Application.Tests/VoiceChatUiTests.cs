using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.WinForms.Controls;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class VoiceChatUiTests
    {
        [Test]
        public void VoiceDisabled_DoesNotCreateABrowserControl()
        {
            using (var owner = new Form())
            using (var voice = new VoiceChatCoordinator(owner, new RealtimeVoiceTests.VoiceClient(), null,
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "unused-profile"),
                () => Task.FromResult(Binding()), () => new VoiceChatSettings(), settings => true, status => { }))
            {
                Assert.That(owner.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>(), Is.Empty);
            }
        }

        [Test]
        public async Task VoiceChoices_HaveReadableNumbersWithoutChangingOfficialIds()
        {
            using (var form = new VoiceSettingsForm(new RealtimeVoiceTests.VoiceClient(), new VoiceChatSettings { Voice = "cove" },
                settings => Task.FromResult(true), (settings, text) => Task.CompletedTask, () => Task.CompletedTask))
            {
                await form.LoadVoicesAsync();
                var choices = FormTestDriver.Field<ComboBox>(form, "_voices");
                Assert.That(choices.GetItemText(choices.Items[0]), Does.Contain("01").And.Contain("cove"));
                Assert.That(choices.GetItemText(choices.Items[1]), Does.Contain("02").And.Contain("juniper"));
                Assert.That(choices.SelectedItem, Is.EqualTo("cove"));
            }
        }

        [Test]
        public async Task MainComposer_ShowsAdditionalControlsOnlyWhileVoiceIsEnabled()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                Assert.That(voice.ToggleButton.Visible, Is.True);
                Assert.That(voice.MuteButton.Visible, Is.False);
                Assert.That(voice.SettingsButton.Visible, Is.False);
                Assert.That(client.Starts, Is.Zero);
                await voice.ToggleAsync();
                Assert.That(voice.ModeEnabled, Is.True);
                Assert.That(voice.MuteButton.Visible, Is.True);
                Assert.That(voice.SettingsButton.Visible, Is.True);
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("thread"));
                Assert.That(owner.OwnedForms, Is.Empty, "Enabling voice must not open another conversation window.");
                await voice.ToggleAsync();
                Assert.That(voice.ModeEnabled, Is.False);
                Assert.That(voice.MuteButton.Visible, Is.False);
                Assert.That(voice.SettingsButton.Visible, Is.False);
                await voice.ToggleAsync();
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("thread"));
            }
        }

        [Test]
        public async Task StopDuringPreparation_CannotStartAnUnwantedLateMicrophone()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var prepared = new TaskCompletionSource<VoiceConversationBinding>();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio(), prepare: () => prepared.Task))
            {
                var start = voice.ToggleAsync();
                await voice.StopAsync();
                prepared.SetResult(Binding());
                await start;
                Assert.That(client.Starts, Is.Zero);
                Assert.That(voice.ModeEnabled, Is.False);
            }
        }

        [Test]
        public async Task VolumeOnly_ChangesOutputWithoutReconnectingAndVoiceChangePreservesMute()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var audio = new RealtimeVoiceTests.Audio();
            using (var owner = new Form())
            using (var voice = Create(owner, client, audio))
            {
                await voice.ToggleAsync();
                await voice.ToggleMuteAsync();
                await voice.ApplySettingsAsync(new VoiceChatSettings { Voice = "cove", OutputLevel = VoiceOutputLevel.StrongBoost });
                Assert.That(client.Starts, Is.EqualTo(1));
                Assert.That(audio.OutputLevel, Is.EqualTo(VoiceOutputLevel.StrongBoost));
                await voice.ApplySettingsAsync(new VoiceChatSettings { Voice = "juniper", FasterSpeech = true });
                Assert.That(client.Starts, Is.EqualTo(2));
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("thread"));
                Assert.That(client.LastRequest.Voice, Is.EqualTo("juniper"));
                Assert.That(audio.PreparedMuted, Is.True);
                Assert.That(voice.ModeEnabled, Is.True);
            }
        }

        [Test]
        public async Task Preview_DoesNotSaveOrPolluteChatAndKeepsTheConversationConnected()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var audio = new RealtimeVoiceTests.Audio();
            var saves = 0;
            var previewAudio = new RealtimeVoiceTests.Audio();
            using (var owner = new Form())
            using (var voice = Create(owner, client, audio, save: settings => { saves++; return true; }, previewAudio: previewAudio))
            {
                await voice.ToggleAsync();
                await voice.ToggleMuteAsync();
                var conversationConnection = audio.SessionId;
                var transcripts = 0;
                voice.TranscriptReceived += (sender, message) => transcripts++;
                var preview = voice.PreviewAsync(new VoiceChatSettings { Voice = "juniper", FasterSpeech = true, OutputLevel = VoiceOutputLevel.Boost }, "试听");
                Assert.That(previewAudio.Preview, Is.True);
                Assert.That(audio.PlaybackMuted, Is.True);
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("preview-thread"));
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Transcript, ThreadId = "preview-thread", SessionId = client.LastRequest.SessionId, ItemId = "preview-text", Text = "试听", Role = "assistant" });
                previewAudio.FinishPreview();
                await preview;
                Assert.That(audio.SessionId, Is.EqualTo(conversationConnection));
                Assert.That(client.Starts, Is.EqualTo(2), "Only the call and the separate preview are started.");
                Assert.That(audio.Stops, Is.Zero);
                Assert.That(audio.Muted, Is.True);
                Assert.That(audio.PlaybackMuted, Is.False);
                Assert.That(client.ReleasedPreviewThreads, Is.EqualTo(1));
                Assert.That(transcripts, Is.Zero);
                Assert.That(saves, Is.Zero);
                Assert.That(voice.ModeEnabled, Is.True);
            }
        }

        [Test]
        public async Task TurningVoiceOffDuringPreview_DoesNotResumeTheMicrophone()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                await voice.ToggleAsync();
                var preview = voice.PreviewAsync(new VoiceChatSettings { Voice = "juniper" }, "试听");
                await voice.StopAsync();
                await preview;
                Assert.That(client.Starts, Is.EqualTo(2));
                Assert.That(voice.ModeEnabled, Is.False);
            }
        }

        [Test]
        public async Task SwitchingConversation_StopsVoiceAndDoesNotRebindTheMicrophone()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                await voice.ToggleAsync();
                voice.SelectConversation("another-session");
                Assert.That(voice.ModeEnabled, Is.False);
                Assert.That(client.Stops, Is.EqualTo(1));
                Assert.That(client.Starts, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task AcceptedText_IsQuietContextForOnlyTheActiveThreadAndDoesNotStartAnotherTask()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                client.EmitText("thread", "关闭时不转发");
                Assert.That(client.AppendedContext, Is.Null);
                await voice.ToggleAsync();
                client.EmitText("other-thread", "其他会话");
                Assert.That(client.AppendedContext, Is.Null);
                client.EmitText("thread", "37 加 29");
                Assert.That(client.AppendedContext, Is.EqualTo("37 加 29"));
                Assert.That(client.Starts, Is.EqualTo(1));
                await voice.StopAsync();
                client.EmitText("thread", "已关闭");
                Assert.That(client.AppendedContext, Is.EqualTo("37 加 29"));
            }
        }

        [Test]
        public async Task BackendModelChange_UpdatesTheCurrentThreadWithoutRestartingVoice()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var model = "selected-model-a";
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio(), prepare: () =>
            {
                var binding = Binding();
                binding.Request.BackendMode = new AppServerCollaborationMode { Mode = "default", ModelId = model, ReasoningEffort = "high" };
                return Task.FromResult(binding);
            }))
            {
                await voice.SetEnabledAsync(true);
                Assert.That(client.LastRequest.BackendMode.ModelId, Is.EqualTo(model));
                model = "selected-model-b";
                Assert.That(await voice.RefreshBackendSettingsAsync("session"), Is.True);
                Assert.That(client.UpdatedBackend.ThreadId, Is.EqualTo("thread"));
                Assert.That(client.UpdatedBackend.BackendMode.ModelId, Is.EqualTo(model));
                Assert.That(client.Starts, Is.EqualTo(1));
                Assert.That(client.Stops, Is.Zero);
            }
        }

        [Test]
        public async Task UpdatingBackendWhileChangingVoice_StartsWithTheLatestSelectedModel()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var model = "selected-model-a";
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio(), prepare: () =>
            {
                var binding = Binding();
                binding.Request.BackendMode = new AppServerCollaborationMode { Mode = "default", ModelId = model };
                return Task.FromResult(binding);
            }))
            {
                await voice.SetEnabledAsync(true);
                model = "selected-model-b";
                client.PendingBackendUpdate = new TaskCompletionSource<bool>();
                var update = voice.RefreshBackendSettingsAsync("session");
                var changeVoice = voice.ApplySettingsAsync(new VoiceChatSettings { Voice = "juniper" });
                Assert.That(client.Starts, Is.EqualTo(1));
                client.PendingBackendUpdate.SetResult(true);
                Assert.That(await update, Is.True);
                Assert.That(await changeVoice, Is.True);
                Assert.That(client.Starts, Is.EqualTo(2));
                Assert.That(client.LastRequest.BackendMode.ModelId, Is.EqualTo(model));
            }
        }

        [Test]
        public async Task LateMuteCompletion_CannotChangeTheNewCallState()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var audio = new RealtimeVoiceTests.Audio();
            using (var owner = new Form())
            using (var voice = Create(owner, client, audio))
            {
                await voice.SetEnabledAsync(true);
                var pending = audio.PendingMute = new TaskCompletionSource<bool>();
                var mute = voice.SetMutedAsync(true);
                await voice.StopAsync();
                audio.PendingMute = null;
                await voice.SetEnabledAsync(true);
                pending.SetResult(true);
                await mute;
                Assert.That(voice.ModeEnabled, Is.True);
                Assert.That(voice.Muted, Is.False);
                Assert.That(audio.PreparedMuted, Is.False);
            }
        }

        [Test]
        public async Task ModelUpdateFailureFromClosedCall_DoesNotStopTheNewCall()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                await voice.SetEnabledAsync(true);
                client.PendingBackendUpdate = new TaskCompletionSource<bool>();
                var update = voice.RefreshBackendSettingsAsync("session");
                await voice.StopAsync();
                var restart = voice.SetEnabledAsync(true);
                client.PendingBackendUpdate.SetException(new InvalidOperationException("Previous call disconnected."));
                await update;
                await restart;
                Assert.That(voice.ModeEnabled, Is.True);
                Assert.That(client.Starts, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task VoiceRelay_DoesNotAddASecondAnswerAndFreshUserInputStartsANewReply()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var owner = new Form())
            using (var voice = Create(owner, client, new RealtimeVoiceTests.Audio()))
            {
                var shown = new System.Collections.Generic.List<RealtimeVoiceEvent>();
                voice.TranscriptReceived += (sender, message) => shown.Add(message);
                await voice.SetEnabledAsync(true);
                Assert.That(voice.BackendReplyCategory("thread", "commentary"), Is.EqualTo(ChatHistoryCategory.Processing));
                Assert.That(voice.BackendReplyCategory("thread", "final_answer"), Is.EqualTo(ChatHistoryCategory.Conversation));
                Action<string, string> emit = (role, text) => client.Emit(new RealtimeVoiceEvent
                { Kind = RealtimeVoiceEventKind.Transcript, ThreadId = "thread", SessionId = client.LastRequest.SessionId, ItemId = text, Role = role, Text = text });
                emit("assistant", "这是后台完整答复的语音转述");
                Assert.That(shown.Last().DisplayAsProcessing, Is.True);
                emit("user", "再解释一下");
                emit("assistant", "可以");
                Assert.That(shown.Last().DisplayAsProcessing, Is.False);
                emit("assistant", "。");
                Assert.That(shown.Count, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task VoiceParameters_AreIdempotentAndRejectAChangedConversation()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var audio = new RealtimeVoiceTests.Audio();
            using (var owner = new Form())
            using (var voice = Create(owner, client, audio))
            {
                var read = await voice.ApplyControlAsync(new VoiceControlCommand(), "thread", () => true);
                Assert.That(read.Success, Is.True);
                Assert.That(client.Starts, Is.Zero);
                var rejected = await voice.ApplyControlAsync(new VoiceControlCommand { Enabled = true }, "thread", () => false);
                Assert.That(rejected.Success, Is.False);
                Assert.That(client.Starts, Is.Zero);
                var request = new VoiceControlCommand { Enabled = true, MicrophoneMuted = true };
                Assert.That((await voice.ApplyControlAsync(request, "thread", () => true)).Success, Is.True);
                Assert.That(audio.PreparedMuted, Is.True, "Opening voice in muted mode must never briefly open the microphone.");
                Assert.That((await voice.ApplyControlAsync(request, "thread", () => true)).Success, Is.True);
                Assert.That(client.Starts, Is.EqualTo(1));
                Assert.That(audio.Muted, Is.True);
                await voice.ApplyControlAsync(new VoiceControlCommand { Enabled = false }, "thread", () => true);
                Assert.That(voice.ModeEnabled, Is.False);
            }
        }

        [Test]
        public async Task EnablingVoiceInAnotherConversation_UsesTheNewBinding()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var currentId = "session";
            VoiceChatCoordinator voice = null;
            using (var owner = new Form())
            using (voice = Create(owner, client, new RealtimeVoiceTests.Audio(), prepare: () =>
            {
                voice.SelectConversation(currentId);
                return Task.FromResult(new VoiceConversationBinding { ConversationId = currentId, Request = new RealtimeVoiceStart { ThreadId = currentId } });
            }))
            {
                await voice.ToggleAsync();
                await voice.StopAsync();
                currentId = "another-session";
                await voice.ToggleAsync();
                Assert.That(client.Starts, Is.EqualTo(2));
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo(currentId));
                Assert.That(voice.ModeEnabled, Is.True);
            }
        }

        [Test]
        public async Task CancellingPreviewWhilePausingTheCall_DoesNotStartALatePreview()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            var audio = new RealtimeVoiceTests.Audio();
            using (var owner = new Form())
            using (var voice = Create(owner, client, audio))
            {
                await voice.ToggleAsync();
                audio.OnMuting = () => { audio.OnMuting = null; _ = voice.StopPreviewAsync(); };
                var preview = voice.PreviewAsync(new VoiceChatSettings { Voice = "juniper" }, "试听");
                if (audio.Preview) audio.FinishPreview();
                await preview;
                Assert.That(client.PreviewThreads, Is.Zero);
                Assert.That(voice.ModeEnabled, Is.True);
                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("thread"));
            }
        }

        [Test]
        public async Task SettingsPreview_KeepsTheExistingChoicesAvailableWithoutOpeningAnotherForm()
        {
            var finished = new TaskCompletionSource<bool>();
            using (var form = new VoiceSettingsForm(new RealtimeVoiceTests.VoiceClient(), new VoiceChatSettings { Voice = "cove" },
                settings => Task.FromResult(true), (settings, text) => finished.Task, () => { finished.TrySetResult(true); return Task.CompletedTask; }))
            {
                await form.LoadVoicesAsync();
                var ownedBefore = System.Windows.Forms.Application.OpenForms.Count;
                var preview = form.PreviewAsync();
                try
                {
                    foreach (var control in FormTestDriver.Descendants<ComboBox>(form))
                        Assert.That(control.Enabled && control.TabStop, Is.True, "Voice, speed and volume must remain reachable while preview plays.");
                    Assert.That(System.Windows.Forms.Application.OpenForms.Count, Is.EqualTo(ownedBefore));
                    FormTestDriver.Field<ComboBox>(form, "_volume").SelectedItem = VoiceOutputLevel.StrongBoost;
                }
                finally { finished.TrySetResult(true); await preview; }
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task SavingVoiceSettings_ClosesOnlyAfterSuccessfulSave(bool succeeds)
        {
            var saved = new TaskCompletionSource<bool>();
            using (var form = new VoiceSettingsForm(new RealtimeVoiceTests.VoiceClient(), new VoiceChatSettings { Voice = "cove" },
                settings => saved.Task, (settings, text) => Task.CompletedTask, () => Task.CompletedTask))
            {
                await form.LoadVoicesAsync();
                var apply = form.ApplyAsync();
                Assert.That(form.IsDisposed, Is.False);
                saved.SetResult(succeeds);
                await apply;
                Assert.That(form.IsDisposed, Is.EqualTo(succeeds));
            }
        }

        [Test]
        public async Task Settings_BrowsingDoesNothingAndPreviewUsesUnsavedChoices()
        {
            VoiceChatSettings applied = null;
            VoiceChatSettings previewed = null;
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var form = new VoiceSettingsForm(client, new VoiceChatSettings { Voice = "cove" },
                settings => { applied = settings; return Task.FromResult(true); },
                (settings, text) => { previewed = settings; return Task.CompletedTask; }, () => Task.CompletedTask))
            {
                await form.LoadVoicesAsync();
                FormTestDriver.Field<ComboBox>(form, "_voices").SelectedItem = "juniper";
                FormTestDriver.Field<ComboBox>(form, "_speed").SelectedIndex = 1;
                FormTestDriver.Field<ComboBox>(form, "_volume").SelectedItem = VoiceOutputLevel.Boost;
                Assert.That(applied, Is.Null);
                Assert.That(previewed, Is.Null);
                await form.PreviewAsync();
                Assert.That(previewed.Voice, Is.EqualTo("juniper"));
                Assert.That(previewed.FasterSpeech, Is.True);
                Assert.That(previewed.OutputLevel, Is.EqualTo(VoiceOutputLevel.Boost));
                Assert.That(applied, Is.Null);
                foreach (var control in FormTestDriver.Descendants<Control>(form).Where(control => control is Button || control is ComboBox))
                {
                    Assert.That(control.AccessibleName, Is.Not.Null.And.Not.Empty);
                    Assert.That(control.TabStop, Is.True);
                }
                await form.ApplyAsync();
                Assert.That(applied.Voice, Is.EqualTo("juniper"));
            }
        }

        [Test]
        public void ProductionAudioPage_IsEmbeddedInTheClientAssembly()
        {
            using (var stream = typeof(VoiceSettingsForm).Assembly.GetManifestResourceStream("MyCodex.Voice.Realtime.html")) Assert.That(stream, Is.Not.Null);
        }

        private static VoiceConversationBinding Binding()
        {
            return new VoiceConversationBinding { ConversationId = "session", Request = new RealtimeVoiceStart { ThreadId = "thread", WorkspacePath = @"D:\workspace" } };
        }

        private static VoiceChatCoordinator Create(Form owner, RealtimeVoiceTests.VoiceClient client, RealtimeVoiceTests.Audio audio,
            Func<Task<VoiceConversationBinding>> prepare = null, Func<VoiceChatSettings, bool> save = null,
            RealtimeVoiceTests.Audio previewAudio = null)
        {
            return new VoiceChatCoordinator(owner, client, null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "unused-profile"),
                prepare ?? (() => Task.FromResult(Binding())), () => new VoiceChatSettings(), save ?? (settings => true), status => { }, audio,
                previewAudio ?? new RealtimeVoiceTests.Audio());
        }
    }
}
