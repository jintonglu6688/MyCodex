using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MyCodex.WinForms.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class WebViewVoiceAudioTests
    {
        [TestCase(true)]
        [TestCase(false)]
        [Category("NativeIntegration")]
        [Explicit("Requires WebView2. Uses only synthetic audio and a non-activating transparent test window.")]
        public void OwnedTransport_StaysHiddenAndIsReleasedAfterEveryStop(bool supportsSystemEchoCancellation)
        {
            var profile = Path.Combine(TestContext.CurrentContext.WorkDirectory, "voice-hidden-" + Guid.NewGuid().ToString("N"));
            using (var form = new BackgroundForm { Opacity = 0, ShowInTaskbar = false, Width = 320, Height = 200 })
            using (var audio = new WebViewVoiceAudio(form, profile))
            {
                WebView2 view = null;
                form.ControlAdded += (sender, args) =>
                {
                    view = args.Control as WebView2;
                    if (view == null) return;
                    var added = view;
                    added.CoreWebView2InitializationCompleted += async (s, ready) =>
                    {
                        if (!ready.IsSuccess) return;
                        var core = added.CoreWebView2;
                        var installed = false;
                        core.NavigationStarting += (s2, e2) => { if (!installed) e2.Cancel = true; };
                        await core.AddScriptToExecuteOnDocumentCreatedAsync(
                            "window.microphoneCalls = 0; navigator.mediaDevices.getUserMedia = async function(options) {" +
                            "microphoneCalls++; window.captureOptions = options;" +
                            (supportsSystemEchoCancellation ? "" :
                                "if (options.audio.echoCancellation.exact) { const error = new Error('Unsupported'); error.name = 'OverconstrainedError'; error.constraint = 'echoCancellation'; throw error; }") +
                            "window.input = new AudioContext(); window.destination = input.createMediaStreamDestination();" +
                            "destination.stream.getAudioTracks()[0].getSettings = () => ({ echoCancellation: " +
                            (supportsSystemEchoCancellation ? "'all'" : "true") + " }); return destination.stream; };");
                        installed = true;
                        core.Navigate("https://mycodex-voice.local/voice.html");
                    };
                };
                form.Show();
                var verification = VerifyHiddenAsync(form, audio, () => view, supportsSystemEchoCancellation);
                var elapsed = Stopwatch.StartNew();
                while (!verification.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(45))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
                Assert.That(verification.IsCompleted, Is.True, "Hidden WebView initialization must not wait for visibility.");
                verification.GetAwaiter().GetResult();
            }
        }

        private static async Task VerifyHiddenAsync(Form form, WebViewVoiceAudio audio, Func<WebView2> getView, bool supportsSystemEchoCancellation)
        {
            Assert.That(form.Controls.Count, Is.Zero);
            await audio.PrepareAsync("hidden", new VoiceAudioOptions { Muted = true }, CancellationToken.None);
            var first = getView();
            Assert.That(first.Visible, Is.False);
            Assert.That(await first.CoreWebView2.ExecuteScriptAsync("captureOptions.audio.echoCancellation"),
                Is.EqualTo(supportsSystemEchoCancellation ? "{\"exact\":\"all\"}" : "true"));
            Assert.That(await first.CoreWebView2.ExecuteScriptAsync("microphoneCalls"), Is.EqualTo(supportsSystemEchoCancellation ? "1" : "2"));
            Assert.That(audio.SystemEchoCancellationEnabled, Is.EqualTo(supportsSystemEchoCancellation));
            form.Hide();
            form.Show();
            Assert.That(first.Visible, Is.False);
            Assert.That(await first.CoreWebView2.ExecuteScriptAsync("document.visibilityState"), Is.EqualTo("\"hidden\""));
            await audio.StopAsync("hidden");
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(audio.SystemEchoCancellationEnabled, Is.Null);
            Assert.That(form.Controls.Count, Is.Zero);
            await audio.PrepareAsync("preview", new VoiceAudioOptions { Preview = true }, CancellationToken.None);
            var second = getView();
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Visible, Is.False);
            Assert.That(audio.SystemEchoCancellationEnabled, Is.Null);
            Assert.That(await second.CoreWebView2.ExecuteScriptAsync("microphoneCalls"), Is.EqualTo("0"));
            await audio.StopAsync("preview");
            Assert.That(second.IsDisposed, Is.True);
        }

        private sealed class BackgroundForm : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        [Test]
        [Category("NativeIntegration")]
        [Explicit("Requires the installed WebView2 runtime; uses a synthetic audio device and no microphone.")]
        public void NativeWebViewBridge_PreparesMutedAudioAndReleasesItWithoutUsingAMicrophone()
        {
            var profile = Path.Combine(TestContext.CurrentContext.WorkDirectory, "voice-webview-" + Guid.NewGuid().ToString("N"));
            using (var form = new BackgroundForm { Opacity = 0, ShowInTaskbar = false, Width = 320, Height = 200 })
            using (var view = new WebView2 { Dock = DockStyle.Fill })
            using (var audio = new WebViewVoiceAudio(view, profile))
            {
                form.Controls.Add(view);
                var injected = new TaskCompletionSource<bool>();
                view.CoreWebView2InitializationCompleted += async (sender, args) =>
                {
                    if (!args.IsSuccess) { injected.TrySetException(args.InitializationException); return; }
                    var core = view.CoreWebView2;
                    core.IsMuted = true;
                    var ready = false;
                    core.NavigationStarting += (s, e) => { if (!ready) e.Cancel = true; };
                    try
                    {
                        await core.AddScriptToExecuteOnDocumentCreatedAsync(
                            "const NativePeer = RTCPeerConnection; window.previewPeerCalls = 0; window.RTCPeerConnection = function(...args) { previewPeerCalls++; return window.previewPeer = new NativePeer(...args); }; RTCPeerConnection.prototype = NativePeer.prototype;" +
                            "navigator.mediaDevices.getUserMedia = async function() {" +
                            "window.voiceProbeContext = new AudioContext();" +
                            "window.voiceProbeDestination = voiceProbeContext.createMediaStreamDestination();" +
                            "window.voiceProbeTrack = voiceProbeDestination.stream.getAudioTracks()[0];" +
                            "return voiceProbeDestination.stream; };");
                        ready = true;
                        injected.TrySetResult(true);
                        core.Navigate("https://mycodex-voice.local/voice.html");
                    }
                    catch (Exception ex) { injected.TrySetException(ex); }
                };
                form.Show();
                var verification = VerifyAsync(audio, view, injected.Task);
                var elapsed = Stopwatch.StartNew();
                while (!verification.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(45))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
                Assert.That(verification.IsCompleted, Is.True, "Native voice bridge timed out.");
                verification.GetAwaiter().GetResult();
                form.Close();
            }
        }

        private static async Task VerifyAsync(WebViewVoiceAudio audio, WebView2 view, Task injected)
        {
            var offer = await audio.PrepareAsync("native-test", new VoiceAudioOptions { Muted = true }, CancellationToken.None);
            await injected;
            Assert.That(offer, Does.Contain("m=audio"));
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voiceProbeTrack.enabled"), Is.EqualTo("false"));
            await audio.SetMutedAsync("native-test", false);
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voiceProbeTrack.enabled"), Is.EqualTo("true"));
            await audio.StopAsync("native-test");
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voiceProbeTrack.readyState"), Is.EqualTo("\"ended\""));
            await view.CoreWebView2.ExecuteScriptAsync("voiceProbeContext.close()");
            await view.CoreWebView2.ExecuteScriptAsync(
                "window.voiceProbeCalls = 0; navigator.mediaDevices.getUserMedia = async () => { voiceProbeCalls++; throw new Error('Preview requested microphone'); };" +
                "window.voiceProbeAddTrack = RTCPeerConnection.prototype.addTrack;" +
                "RTCPeerConnection.prototype.addTrack = function(track, stream) { window.voicePreviewTrack = track; return voiceProbeAddTrack.call(this, track, stream); };");
            var preview = await audio.PrepareAsync("native-preview", new VoiceAudioOptions { Preview = true, OutputLevel = VoiceOutputLevel.Boost }, CancellationToken.None);
            Assert.That(preview, Does.Contain("m=audio"));
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voiceProbeCalls"), Is.EqualTo("0"));
            await audio.SetOutputLevelAsync("native-preview", VoiceOutputLevel.StrongBoost);
            await audio.SetOutputLevelAsync("native-preview", VoiceOutputLevel.Original);
            await audio.StopAsync("native-preview");
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voicePreviewTrack.readyState"), Is.EqualTo("\"ended\""));
            await VerifyPreviewCacheAsync(audio, view);
        }

        private static async Task VerifyPreviewCacheAsync(WebViewVoiceAudio audio, WebView2 view)
        {
            var options = new VoiceAudioOptions { Preview = true, Voice = "cove", PreviewText = "Synthetic preview" };
            var recorded = new TaskCompletionSource<bool>();
            audio.PreviewCompleted += (sender, args) => { if (args.SessionId == "cache-record") recorded.TrySetResult(true); };
            await audio.PrepareAsync("cache-record", options, CancellationToken.None);
            await view.CoreWebView2.ExecuteScriptAsync(
                "window.previewToneContext = new AudioContext(); window.previewTone = previewToneContext.createOscillator();" +
                "window.previewToneGain = previewToneContext.createGain(); previewToneGain.gain.value = 0.02;" +
                "window.previewToneDestination = previewToneContext.createMediaStreamDestination();" +
                "previewTone.connect(previewToneGain); previewToneGain.connect(previewToneDestination); previewTone.start();" +
                "previewPeer.ontrack({track:previewToneDestination.stream.getAudioTracks()[0]}); setTimeout(() => previewTone.stop(), 300);");
            Assert.That(await Task.WhenAny(recorded.Task, Task.Delay(6000)), Is.SameAs(recorded.Task), "The generated preview sample must complete.");
            await audio.StopAsync("cache-record");
            await view.CoreWebView2.ExecuteScriptAsync("previewToneDestination.stream.getTracks().forEach(track => track.stop()); previewToneContext.close();");
            var peerCount = await view.CoreWebView2.ExecuteScriptAsync("previewPeerCalls");
            var replayed = new TaskCompletionSource<bool>();
            audio.PreviewCompleted += (sender, args) => { if (args.SessionId == "cache-replay") replayed.TrySetResult(true); };
            options.OutputLevel = VoiceOutputLevel.StrongBoost;
            Assert.That(await audio.TryReplayPreviewAsync("cache-replay", options, CancellationToken.None), Is.True,
                "Changing volume must reuse the same unamplified sample.");
            Assert.That(await Task.WhenAny(replayed.Task, Task.Delay(6000)), Is.SameAs(replayed.Task));
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("previewPeerCalls"), Is.EqualTo(peerCount), "Replay must not open another connection.");
            Assert.That(await view.CoreWebView2.ExecuteScriptAsync("voiceProbeCalls"), Is.EqualTo("0"));
            await audio.StopAsync("cache-replay");
            options.FasterSpeech = true;
            Assert.That(await audio.TryReplayPreviewAsync("wrong-speed", options, CancellationToken.None), Is.False);
            options.FasterSpeech = false;
            options.Voice = "juniper";
            Assert.That(await audio.TryReplayPreviewAsync("wrong-voice", options, CancellationToken.None), Is.False);
        }
    }
}
