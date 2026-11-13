using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    /// <summary>
    /// Checks for hooks that need review at connect, thread start, and window activation, and prompts once per
    /// exact set of key and hash. In the background it only announces; the dialog opens when the window is active.
    /// </summary>
    internal sealed class HookReviewPromptCoordinator
    {
        // Activation fires often; a repeated discovery within this window adds nothing.
        private static readonly TimeSpan ActivationCheckInterval = TimeSpan.FromSeconds(15);

        private readonly Form _owner;
        private readonly Func<HookManagementService> _createService;
        private readonly Func<string> _getWorkspacePath;
        private readonly Func<string> _getThreadId;
        private readonly Action<string> _announce;
        private readonly HookReviewPromptState _state = new HookReviewPromptState();
        private DateTime _lastCheckUtc = DateTime.MinValue;
        private bool _isChecking;
        private bool _recheckRequested;

        public HookReviewPromptCoordinator(
            Form owner,
            Func<HookManagementService> createService,
            Func<string> getWorkspacePath,
            Func<string> getThreadId,
            Action<string> announce)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _createService = createService ?? throw new ArgumentNullException(nameof(createService));
            _getWorkspacePath = getWorkspacePath ?? throw new ArgumentNullException(nameof(getWorkspacePath));
            _getThreadId = getThreadId ?? throw new ArgumentNullException(nameof(getThreadId));
            _announce = announce ?? throw new ArgumentNullException(nameof(announce));
        }

        public Task CheckAfterConnectAsync()
        {
            _state.ResetThreadViews();
            return CheckAsync(true);
        }

        /// <summary>Called whenever the main window redraws its header; checks only when the workspace or session changed.</summary>
        public void NotifyContextChanged()
        {
            if (_state.EnterContext(_getWorkspacePath(), _getThreadId()))
            {
                _ = CheckAsync(true);
            }
        }

        public async void NotifyTurnStarted(string threadId)
        {
            if (_state.NeedsThreadViewCheck(threadId, _getThreadId()) && await CheckAsync(true))
            {
                _state.MarkThreadViewChecked(threadId);
            }
        }

        /// <summary>
        /// Activation checks are throttled unless a prompt is waiting for the window to come forward. A forced check
        /// that arrives while another runs is queued, so a context change is never lost. True when the list was read.
        /// </summary>
        public async Task<bool> CheckAsync(bool force)
        {
            if (_isChecking)
            {
                _recheckRequested |= force;
                return false;
            }

            if (!force && !_state.HasDeferredPrompt && DateTime.UtcNow - _lastCheckUtc < ActivationCheckInterval)
            {
                return false;
            }

            _isChecking = true;
            try
            {
                _lastCheckUtc = DateTime.UtcNow;
                var workspacePath = _getWorkspacePath();
                var threadId = _getThreadId();
                var service = _createService();
                var pending = HookReviewPromptState.GetPending(await service.ListAsync(workspacePath, threadId));
                var decision = _state.Decide(HookReviewPromptState.GetSignature(pending), CanShowDialog());
                if (decision != HookReviewPromptDecision.None)
                {
                    // Like other dialog requests: the sound always, speech only while this target is in the background.
                    _announce(string.Format(
                        CultureInfo.CurrentCulture,
                        UiLocalization.Text("HookReviewForm.Notice.Pending", "{0} hooks need review and will not run until trusted."),
                        pending.Count));
                }

                if (decision == HookReviewPromptDecision.Show)
                {
                    bool openManagement;
                    using (var form = new HookReviewForm(service, workspacePath, threadId, pending))
                    {
                        form.ShowDialog(_owner);
                        openManagement = form.OpenManagementRequested;
                    }

                    if (openManagement)
                    {
                        OpenManagement(service, workspacePath, threadId);
                    }
                }

                return true;
            }
            catch (Exception)
            {
                // Runtimes without hooks/list, or a dropped connection, simply mean no prompt; management still reports errors.
                return false;
            }
            finally
            {
                _isChecking = false;
                if (_recheckRequested)
                {
                    _recheckRequested = false;
                    _ = CheckAsync(true);
                }
            }
        }

        public void OpenManagement()
        {
            OpenManagement(_createService(), _getWorkspacePath(), _getThreadId());
        }

        private void OpenManagement(HookManagementService service, string workspacePath, string threadId)
        {
            using (var form = new HooksManagementForm(service, workspacePath, threadId))
            {
                form.ShowDialog(_owner);
            }
        }

        /// <summary>Only an active, enabled main window may open the dialog; otherwise another dialog or app has the user.</summary>
        private bool CanShowDialog()
        {
            return _owner.Visible && _owner.CanFocus && Form.ActiveForm == _owner &&
                ForegroundApplicationWindow.BelongsToCurrentProcess();
        }
    }
}
