using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class WebViewVoiceAudio : IRealtimeVoiceAudio
    {
        private const string Address = "https://mycodex-voice.local/voice.html";
        private const int MaximumPreviewCharacters = 512 * 1024;
        private WebView2 _view;
        private readonly Control _dispatcher;
        private readonly bool _ownsView;
        private readonly string _profilePath;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Dictionary<long, TaskCompletionSource<BridgeMessage>> _requests = new Dictionary<long, TaskCompletionSource<BridgeMessage>>();
        private TaskCompletionSource<bool> _ready = NewCompletion<bool>();
        private Task _initialization;
        private TaskCompletionSource<bool> _connected;
        private long _nextId;
        private string _sessionId;
        private bool _captureAllowed;
        private bool _disposed;
        private string _previewKey;
        private string _cachedPreviewKey;
        private string _cachedPreviewAudio;

        private sealed class BridgeMessage
        {
            public BridgeMessage() { }
            public long id { get; set; }
            public bool ok { get; set; }
            public string kind { get; set; }
            public string sessionId { get; set; }
            public string sdp { get; set; }
            public string error { get; set; }
            public string errorName { get; set; }
            public string echoMode { get; set; }
            public string clip { get; set; }
        }

        public WebViewVoiceAudio(WebView2 view, string profilePath)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _dispatcher = view;
            _profilePath = profilePath ?? throw new ArgumentNullException(nameof(profilePath));
        }

        public WebViewVoiceAudio(Form owner, string profilePath)
        {
            _dispatcher = owner ?? throw new ArgumentNullException(nameof(owner));
            _ownsView = true;
            _profilePath = profilePath ?? throw new ArgumentNullException(nameof(profilePath));
        }

        private sealed class AudioOnlyWebView : WebView2
        {
            protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
        }

        private void EnsureOwnedView()
        {
            if (_view != null) return;
            _view = new AudioOnlyWebView
            {
                Visible = false, Size = new System.Drawing.Size(1, 1), TabStop = false,
                AccessibleRole = AccessibleRole.None, AccessibleName = string.Empty
            };
            _dispatcher.Controls.Add(_view);
            var handle = _view.Handle; // Create the native child without ever showing it.
        }

        public event EventHandler<VoiceAudioEvent> Failed;
        public event EventHandler<VoiceAudioEvent> PreviewCompleted;
        public bool? SystemEchoCancellationEnabled { get; private set; }

        public Task<bool> TryReplayPreviewAsync(string sessionId, VoiceAudioOptions options, CancellationToken cancellationToken)
        {
            return OnUiAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = PreviewKey(options);
                if (key == null || key != _cachedPreviewKey || _cachedPreviewAudio == null) return false;
                EnsureOwnedView();
                _sessionId = sessionId;
                _previewKey = null;
                _captureAllowed = false;
                SystemEchoCancellationEnabled = null;
                if (_initialization == null) _initialization = InitializeAsync();
                await _initialization;
                cancellationToken.ThrowIfCancellationRequested();
                if (_sessionId != sessionId) throw new OperationCanceledException();
                await RequestAsync("replay", sessionId, new Dictionary<string, object>
                {
                    { "clip", _cachedPreviewAudio }, { "gain", (int)options.OutputLevel / 100.0 }
                }, cancellationToken);
                return true;
            });
        }

        private string PreviewKey(VoiceAudioOptions options)
        {
            return options?.Preview == true && !string.IsNullOrWhiteSpace(options.Voice) && !string.IsNullOrWhiteSpace(options.PreviewText)
                ? _json.Serialize(new[] { options.Voice, options.FasterSpeech ? "faster" : "default", options.PreviewText }) : null;
        }

        public Task<string> PrepareAsync(string sessionId, VoiceAudioOptions options, CancellationToken cancellationToken)
        {
            if (options == null || !Enum.IsDefined(typeof(VoiceOutputLevel), options.OutputLevel)) throw new ArgumentException(nameof(options));
            return OnUiAsync(async () =>
            {
                EnsureOwnedView();
                _sessionId = sessionId;
                _previewKey = PreviewKey(options);
                _connected = NewCompletion<bool>();
                if (_initialization == null) _initialization = InitializeAsync();
                await _initialization;
                cancellationToken.ThrowIfCancellationRequested();
                if (_sessionId != sessionId) throw new OperationCanceledException();
                _captureAllowed = !options.Preview;
                var response = await RequestAsync("prepare", sessionId, new Dictionary<string, object>
                {
                    { "muted", options.Muted }, { "preview", options.Preview }, { "gain", (int)options.OutputLevel / 100.0 },
                    { "previewCacheLimit", _previewKey == null ? 0 : MaximumPreviewCharacters }
                }, cancellationToken);
                SystemEchoCancellationEnabled = options.Preview ? (bool?)null : response.echoMode == "all";
                return response.sdp;
            });
        }

        public Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken)
        {
            return OnUiAsync(async () =>
            {
                if (_sessionId != sessionId) throw new OperationCanceledException();
                await RequestAsync("answer", sessionId, new Dictionary<string, object> { { "sdp", answerSdp } }, cancellationToken);
                await WithTimeoutAsync(_connected.Task, cancellationToken, 30000);
                return true;
            });
        }

        public Task SetMutedAsync(string sessionId, bool muted)
        {
            return OnUiAsync(async () =>
            {
                await RequestAsync("mute", sessionId, new Dictionary<string, object> { { "muted", muted } }, CancellationToken.None);
                return true;
            });
        }

        public Task SetOutputLevelAsync(string sessionId, VoiceOutputLevel level)
        {
            if (!Enum.IsDefined(typeof(VoiceOutputLevel), level)) throw new ArgumentOutOfRangeException(nameof(level));
            return OnUiAsync(async () =>
            {
                await RequestAsync("volume", sessionId, new Dictionary<string, object> { { "gain", (int)level / 100.0 } }, CancellationToken.None);
                return true;
            });
        }

        public Task SetPlaybackMutedAsync(string sessionId, bool muted)
        {
            return OnUiAsync(async () =>
            {
                await RequestAsync("playbackMute", sessionId, new Dictionary<string, object> { { "muted", muted } }, CancellationToken.None);
                return true;
            });
        }

        public Task StopAsync(string sessionId)
        {
            if (_disposed) return Task.CompletedTask;
            return OnUiAsync(async () =>
            {
                if (_sessionId != sessionId) return true;
                _captureAllowed = false;
                _sessionId = null;
                _connected?.TrySetCanceled();
                try
                {
                    if (_ready.Task.Status == TaskStatus.RanToCompletion)
                        await RequestAsync("stop", sessionId, null, CancellationToken.None, 4000);
                }
                finally { if (_ownsView && _sessionId == null) ReleaseView(); }
                return true;
            });
        }

        private async Task InitializeAsync()
        {
            var view = _view;
            var ready = _ready;
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var environment = await CoreWebView2Environment.CreateAsync(null, _profilePath, options);
            if (_disposed || !ReferenceEquals(view, _view)) throw new OperationCanceledException();
            await view.EnsureCoreWebView2Async(environment);
            if (_disposed || !ReferenceEquals(view, _view)) throw new OperationCanceledException();
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.AddWebResourceRequestedFilter(Address, CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResourceRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += OnProcessFailed;
            core.Navigate(Address);
            await WithTimeoutAsync(ready.Task, CancellationToken.None, 15000);
        }

        private void OnResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!string.Equals(e.Request.Uri, Address, StringComparison.Ordinal)) return;
            var stream = typeof(WebViewVoiceAudio).Assembly.GetManifestResourceStream("MyCodex.Voice.Realtime.html");
            e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        }

        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            Uri uri;
            var ownOrigin = Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) &&
                string.Equals(uri.GetLeftPart(UriPartial.Authority), "https://mycodex-voice.local", StringComparison.OrdinalIgnoreCase);
            e.State = _captureAllowed && ownOrigin && e.PermissionKind == CoreWebView2PermissionKind.Microphone
                ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            e.Handled = true;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!string.Equals(e.Uri, Address, StringComparison.Ordinal)) e.Cancel = true;
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!ReferenceEquals(sender, _view?.CoreWebView2)) return;
            Failed?.Invoke(this, new VoiceAudioEvent { SessionId = _sessionId, Error = UiLocalization.Text("VoiceChatForm.Error.AudioStopped", "The audio engine stopped. Close voice chat and open it again.") });
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!ReferenceEquals(sender, _view?.CoreWebView2)) return;
            if (!string.Equals(e.Source, Address, StringComparison.Ordinal)) return;
            BridgeMessage message;
            try { message = _json.Deserialize<BridgeMessage>(e.WebMessageAsJson); }
            catch (ArgumentException) { return; }
            if (message == null) return;
            TaskCompletionSource<BridgeMessage> request;
            if (message.id != 0 && _requests.TryGetValue(message.id, out request))
            {
                request.TrySetResult(message);
                return;
            }
            if (message.kind == "ready") _ready.TrySetResult(true);
            if (message.sessionId != _sessionId) return;
            if (message.kind == "connected") _connected?.TrySetResult(true);
            if (message.kind == "error") Failed?.Invoke(this, new VoiceAudioEvent
            {
                SessionId = _sessionId,
                Error = message.errorName == "PreviewNoAudio" ? UiLocalization.Text("VoiceChatForm.Error.PreviewSilent", "No preview audio was received. Please try again.") : message.error
            });
            if (message.kind == "previewComplete")
            {
                if (_previewKey != null && message.clip != null && message.clip.Length <= MaximumPreviewCharacters &&
                    message.clip.StartsWith("data:audio/webm;codecs=opus;base64,", StringComparison.Ordinal))
                {
                    _cachedPreviewKey = _previewKey;
                    _cachedPreviewAudio = message.clip;
                }
                PreviewCompleted?.Invoke(this, new VoiceAudioEvent { SessionId = _sessionId });
            }
        }

        private async Task<BridgeMessage> RequestAsync(string method, string sessionId, IDictionary<string, object> extra, CancellationToken token, int timeout = 30000)
        {
            token.ThrowIfCancellationRequested();
            var id = ++_nextId;
            var completion = NewCompletion<BridgeMessage>();
            var payload = new Dictionary<string, object> { { "id", id }, { "method", method }, { "sessionId", sessionId } };
            if (extra != null) foreach (var item in extra) payload[item.Key] = item.Value;
            _requests.Add(id, completion);
            try
            {
                _view.CoreWebView2.PostWebMessageAsJson(_json.Serialize(payload));
                var response = await WithTimeoutAsync(completion.Task, token, timeout);
                if (!response.ok)
                {
                    if (response.errorName == "NotAllowedError" || response.errorName == "NotFoundError" || response.errorName == "NotReadableError")
                        throw new InvalidOperationException(UiLocalization.Text("VoiceChatForm.Error.Microphone", "The microphone is unavailable. Check the device and Windows microphone permission."));
                    throw new InvalidOperationException(response.error);
                }
                return response;
            }
            finally { _requests.Remove(id); }
        }

        private Task<T> OnUiAsync<T>(Func<Task<T>> action)
        {
            if (_disposed || _dispatcher.IsDisposed) return Task.FromException<T>(new ObjectDisposedException(nameof(WebViewVoiceAudio)));
            if (!_dispatcher.InvokeRequired) return action();
            var completion = NewCompletion<T>();
            try
            {
                _dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { completion.TrySetResult(await action()); }
                    catch (OperationCanceledException) { completion.TrySetCanceled(); }
                    catch (Exception ex) { completion.TrySetException(ex); }
                }));
            }
            catch (InvalidOperationException ex) { completion.TrySetException(ex); }
            return completion.Task;
        }

        private static TaskCompletionSource<T> NewCompletion<T>() { return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously); }

        private static async Task<T> WithTimeoutAsync<T>(Task<T> task, CancellationToken token, int milliseconds)
        {
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(milliseconds, lifetime.Token);
                var result = await Task.WhenAny(task, delay);
                lifetime.Cancel();
                token.ThrowIfCancellationRequested();
                if (result != task) throw new TimeoutException(UiLocalization.Text("VoiceChatForm.Error.Timeout", "The voice connection timed out. Please try again."));
                return await task;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cachedPreviewKey = null;
            _cachedPreviewAudio = null;
            _captureAllowed = false;
            _connected?.TrySetCanceled();
            foreach (var request in _requests.Values) request.TrySetCanceled();
            if (!_dispatcher.IsDisposed && _dispatcher.InvokeRequired)
            {
                try { _dispatcher.BeginInvoke(new Action(ReleaseView)); } catch (InvalidOperationException) { }
            }
            else ReleaseView();
        }

        private void ReleaseView()
        {
            _ready.TrySetCanceled();
            foreach (var request in _requests.Values) request.TrySetCanceled();
            var view = _view;
            _view = null;
            _initialization = null;
            SystemEchoCancellationEnabled = null;
            _previewKey = null;
            _ready = NewCompletion<bool>();
            view?.Dispose(); // Dispose removes the browser and its accessibility subtree, not only the audio stream.
        }
    }
}
