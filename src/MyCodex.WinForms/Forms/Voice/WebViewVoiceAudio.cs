using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    // Hidden WebView2 that owns the microphone and WebRTC media; it exists only while voice is on.
    internal sealed class WebViewVoiceAudio : IRealtimeVoiceAudio
    {
        internal const string Origin = "https://mycodex-voice.local";
        internal const string Address = Origin + "/voice.html";
        internal const string PageResourceName = "MyCodex.Voice.Realtime.html";
        private readonly Control _owner;
        private readonly string _profilePath;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Dictionary<long, TaskCompletionSource<BridgeMessage>> _requests = new Dictionary<long, TaskCompletionSource<BridgeMessage>>();
        private WebView2 _view;
        private TaskCompletionSource<bool> _ready = NewCompletion<bool>();
        private Task _initialization;
        private TaskCompletionSource<bool> _connected;
        private long _nextId;
        private string _sessionId;
        private bool _captureAllowed;
        private bool _disposed;

        internal sealed class BridgeMessage
        {
            public long id { get; set; }

            public bool ok { get; set; }

            public string kind { get; set; }

            public string sessionId { get; set; }

            public string sdp { get; set; }

            public string error { get; set; }

            public string errorName { get; set; }

            public string echoMode { get; set; }
        }

        private sealed class AudioOnlyWebView : WebView2
        {
            protected override void SetVisibleCore(bool value)
            {
                base.SetVisibleCore(false);
            }
        }

        public WebViewVoiceAudio(Control owner, string profilePath)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _profilePath = profilePath ?? throw new ArgumentNullException(nameof(profilePath));
        }

        public event EventHandler<VoiceAudioEvent> Failed;

        public bool? SystemEchoCancellationEnabled { get; private set; }

        public event EventHandler<VoiceAudioEvent> PlaybackFinished;

        public Task<string> PrepareAsync(string sessionId, int outputVolumePercent, bool listenOnly, CancellationToken cancellationToken)
        {
            return OnUiAsync(async () =>
            {
                EnsureView();
                _sessionId = sessionId;
                _connected = NewCompletion<bool>();
                if (_initialization == null)
                {
                    _initialization = InitializeAsync();
                }

                await _initialization;
                cancellationToken.ThrowIfCancellationRequested();
                if (_sessionId != sessionId)
                {
                    throw new OperationCanceledException();
                }

                _captureAllowed = !listenOnly;
                var response = await RequestAsync("prepare", sessionId, new Dictionary<string, object>
                {
                    { "gain", outputVolumePercent / 100.0 },
                    { "listenOnly", listenOnly }
                }, cancellationToken);
                SystemEchoCancellationEnabled = response.echoMode == "all";
                return response.sdp;
            });
        }

        public Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken)
        {
            return OnUiAsync(async () =>
            {
                if (_sessionId != sessionId)
                {
                    throw new OperationCanceledException();
                }

                await RequestAsync("answer", sessionId, new Dictionary<string, object> { { "sdp", answerSdp } }, cancellationToken);
                await WithTimeoutAsync(_connected.Task, cancellationToken, 30000);
                return true;
            });
        }

        public Task SetMicrophoneEnabledAsync(string sessionId, bool enabled)
        {
            return OnUiAsync(async () =>
            {
                if (_sessionId != sessionId)
                {
                    throw new OperationCanceledException();
                }

                await RequestAsync("microphone", sessionId, new Dictionary<string, object> { { "enabled", enabled } }, CancellationToken.None, 4000);
                return true;
            });
        }

        public Task StopAsync(string sessionId)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            return OnUiAsync(async () =>
            {
                if (_sessionId != sessionId)
                {
                    return true;
                }

                _captureAllowed = false;
                _sessionId = null;
                _connected?.TrySetCanceled();
                try
                {
                    if (_ready.Task.Status == TaskStatus.RanToCompletion)
                    {
                        await RequestAsync("stop", sessionId, null, CancellationToken.None, 4000);
                    }
                }
                finally
                {
                    // A new session may have started while the stop request was pending.
                    if (_sessionId == null)
                    {
                        ReleaseView();
                    }
                }

                return true;
            });
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            _view = new AudioOnlyWebView
            {
                Visible = false,
                Size = new System.Drawing.Size(1, 1),
                TabStop = false,
                AccessibleRole = AccessibleRole.None,
                AccessibleName = string.Empty
            };
            _owner.Controls.Add(_view);
            var handle = _view.Handle; // Create the native child without ever showing it.
        }

        private async Task InitializeAsync()
        {
            var view = _view;
            var ready = _ready;
            // A separate profile keeps the voice page's permissions apart from other MyCodex web views.
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var environment = await CoreWebView2Environment.CreateAsync(null, _profilePath, options);
            if (_disposed || !ReferenceEquals(view, _view))
            {
                throw new OperationCanceledException();
            }

            await view.EnsureCoreWebView2Async(environment);
            if (_disposed || !ReferenceEquals(view, _view))
            {
                throw new OperationCanceledException();
            }

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
            if (!string.Equals(e.Request.Uri, Address, StringComparison.Ordinal))
            {
                return;
            }

            var stream = typeof(WebViewVoiceAudio).Assembly.GetManifestResourceStream(PageResourceName);
            e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        }

        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            e.State = IsMicrophoneAllowed(_captureAllowed, e.Uri, e.PermissionKind)
                ? CoreWebView2PermissionState.Allow
                : CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            e.Handled = true;
        }

        internal static bool IsMicrophoneAllowed(bool captureAllowed, string requestUri, CoreWebView2PermissionKind kind)
        {
            Uri uri;
            return captureAllowed &&
                kind == CoreWebView2PermissionKind.Microphone &&
                Uri.TryCreate(requestUri, UriKind.Absolute, out uri) &&
                string.Equals(uri.GetLeftPart(UriPartial.Authority), Origin, StringComparison.OrdinalIgnoreCase);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!string.Equals(e.Uri, Address, StringComparison.Ordinal))
            {
                e.Cancel = true;
            }
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            if (!ReferenceEquals(sender, _view?.CoreWebView2))
            {
                return;
            }

            Failed?.Invoke(this, new VoiceAudioEvent
            {
                SessionId = _sessionId,
                Error = UiLocalization.Text("VoiceChat.Error.AudioStopped", "The voice audio engine stopped. Start voice chat again.")
            });
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!ReferenceEquals(sender, _view?.CoreWebView2) || !string.Equals(e.Source, Address, StringComparison.Ordinal))
            {
                return;
            }

            BridgeMessage message;
            try
            {
                message = _json.Deserialize<BridgeMessage>(e.WebMessageAsJson);
            }
            catch (ArgumentException)
            {
                return;
            }

            if (message == null)
            {
                return;
            }

            TaskCompletionSource<BridgeMessage> request;
            if (message.id != 0 && _requests.TryGetValue(message.id, out request))
            {
                request.TrySetResult(message);
                return;
            }

            if (message.kind == "ready")
            {
                _ready.TrySetResult(true);
            }

            if (message.sessionId != _sessionId)
            {
                return;
            }

            if (message.kind == "connected")
            {
                _connected?.TrySetResult(true);
            }
            else if (message.kind == "error")
            {
                Failed?.Invoke(this, new VoiceAudioEvent { SessionId = _sessionId, Error = message.error });
            }
            else if (message.kind == "finished")
            {
                PlaybackFinished?.Invoke(this, new VoiceAudioEvent { SessionId = _sessionId });
            }
        }

        private async Task<BridgeMessage> RequestAsync(
            string method,
            string sessionId,
            IDictionary<string, object> extra,
            CancellationToken cancellationToken,
            int timeoutMs = 30000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ++_nextId;
            var completion = NewCompletion<BridgeMessage>();
            var payload = new Dictionary<string, object> { { "id", id }, { "method", method }, { "sessionId", sessionId } };
            if (extra != null)
            {
                foreach (var item in extra)
                {
                    payload[item.Key] = item.Value;
                }
            }

            _requests.Add(id, completion);
            try
            {
                _view.CoreWebView2.PostWebMessageAsJson(_json.Serialize(payload));
                var response = await WithTimeoutAsync(completion.Task, cancellationToken, timeoutMs);
                if (!response.ok)
                {
                    throw new InvalidOperationException(DescribeFailure(response));
                }

                return response;
            }
            finally
            {
                _requests.Remove(id);
            }
        }

        internal static string DescribeFailure(BridgeMessage response)
        {
            if (response.errorName == "NotAllowedError" || response.errorName == "NotFoundError" || response.errorName == "NotReadableError")
            {
                return UiLocalization.Text("VoiceChat.Error.Microphone", "The microphone is unavailable. Check the device and the Windows microphone permission.");
            }

            return response.error;
        }

        private Task<T> OnUiAsync<T>(Func<Task<T>> action)
        {
            if (_disposed || _owner.IsDisposed)
            {
                return Task.FromException<T>(new ObjectDisposedException(nameof(WebViewVoiceAudio)));
            }

            if (!_owner.InvokeRequired)
            {
                return action();
            }

            var completion = NewCompletion<T>();
            try
            {
                _owner.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        completion.TrySetResult(await action());
                    }
                    catch (OperationCanceledException)
                    {
                        completion.TrySetCanceled();
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                }));
            }
            catch (InvalidOperationException ex)
            {
                completion.TrySetException(ex);
            }

            return completion.Task;
        }

        private static TaskCompletionSource<T> NewCompletion<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<T> WithTimeoutAsync<T>(Task<T> task, CancellationToken cancellationToken, int timeoutMs)
        {
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delay = Task.Delay(timeoutMs, lifetime.Token);
                var result = await Task.WhenAny(task, delay);
                lifetime.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                if (result != task)
                {
                    throw new TimeoutException(UiLocalization.Text("VoiceChat.Error.Timeout", "The voice connection timed out. Please try again."));
                }

                return await task;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _captureAllowed = false;
            _connected?.TrySetCanceled();
            if (!_owner.IsDisposed && _owner.InvokeRequired)
            {
                try
                {
                    _owner.BeginInvoke(new Action(ReleaseView));
                }
                catch (InvalidOperationException)
                {
                }
            }
            else
            {
                ReleaseView();
            }
        }

        private void ReleaseView()
        {
            _ready.TrySetCanceled();
            foreach (var request in new List<TaskCompletionSource<BridgeMessage>>(_requests.Values))
            {
                request.TrySetCanceled();
            }

            var view = _view;
            _view = null;
            _initialization = null;
            SystemEchoCancellationEnabled = null;
            _ready = NewCompletion<bool>();
            // Disposing removes the browser process and its accessibility subtree, not only the audio stream.
            // A view disposed with its owner window must not be disposed again from a cleanup thread.
            if (view != null && !view.IsDisposed)
            {
                view.Dispose();
            }
        }
    }
}
