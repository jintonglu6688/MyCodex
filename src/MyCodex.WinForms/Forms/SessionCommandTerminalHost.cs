using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    // ponytail: one host per detached session keeps process, buffer, and form ownership together.
    internal sealed class SessionCommandTerminalHost
    {
        private const int MaxBufferedCharacters = 131072;
        private readonly IAppServerClient _appServerClient;
        private readonly ChatSession _session;
        private readonly WorkspaceInfo _workspace;
        private readonly Func<ChatSession, ApprovalMode> _approvalModeProvider;
        private readonly Func<Task<bool>> _ensureAvailableAsync;
        private readonly CommandTerminalBuffer _buffer;
        private readonly CommandTerminalResizePlanner _resizePlanner;
        private CommandTerminalForm _form;
        private CommandTask _commandTask;

        internal SessionCommandTerminalHost(
            IAppServerClient appServerClient,
            ChatSession session,
            WorkspaceInfo workspace,
            Func<ChatSession, ApprovalMode> approvalModeProvider,
            Func<Task<bool>> ensureAvailableAsync)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _approvalModeProvider = approvalModeProvider ?? throw new ArgumentNullException(nameof(approvalModeProvider));
            _ensureAvailableAsync = ensureAvailableAsync ?? throw new ArgumentNullException(nameof(ensureAvailableAsync));
            _buffer = new CommandTerminalBuffer(MaxBufferedCharacters);
            _resizePlanner = new CommandTerminalResizePlanner();
        }

        internal bool HasActiveCommand
        {
            get
            {
                return _commandTask != null &&
                       string.Equals(_commandTask.Status, "running", StringComparison.OrdinalIgnoreCase);
            }
        }

        internal bool HasOpenWindow
        {
            get { return _form != null && !_form.IsDisposed && !_form.Disposing; }
        }

        internal Form Window
        {
            get { return HasOpenWindow ? _form : null; }
        }

        internal event EventHandler<CommandTerminalHotkeyEventArgs> ScreenReaderHotkeyPressed;

        internal event EventHandler TerminalWindowClosed;

        internal event EventHandler TerminalBufferReset;

        internal string[] GetReadableLines()
        {
            return ManualReadTargetState.GetReadableTerminalLines(_buffer);
        }

        internal void Show()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.Show();
                _form.Activate();
                return;
            }

            _form = new CommandTerminalForm(
                _commandTask,
                _buffer,
                !HasActiveCommand,
                CanSubmitInput(),
                CanTerminate());
            _form.CommandSubmitted += Form_CommandSubmitted;
            _form.InputSubmitted += Form_InputSubmitted;
            _form.StopRequested += Form_StopRequested;
            _form.ScreenReaderHotkeyPressed += Form_ScreenReaderHotkeyPressed;
            _form.TerminalResizeRequested += Form_TerminalResizeRequested;
            _form.FormClosed += Form_FormClosed;
            _form.Show();
        }

        internal void Close()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.Close();
            }
        }

        internal bool TryCloseWindow()
        {
            Close();
            return !HasOpenWindow;
        }

        internal async Task TerminateActiveCommandForRuntimeTransitionAsync()
        {
            if (_commandTask == null || string.IsNullOrWhiteSpace(_commandTask.ProcessId) || !HasActiveCommand)
            {
                return;
            }

            _commandTask.TerminationRequested = true;
            try
            {
                await _appServerClient.TerminateCommandAsync(_commandTask.ProcessId);
            }
            finally
            {
                _commandTask.Status = "canceled";
                RefreshForm();
            }
        }

        internal void HandleAppServerEvent(AppServerEventArgs e)
        {
            if (e == null || _commandTask == null ||
                !string.Equals(_commandTask.ProcessId, e.ProcessId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(e.EventType, "command_terminal_interaction", StringComparison.OrdinalIgnoreCase))
            {
                CommandTerminalInteractionState.TryMarkWaitingForInput(_commandTask, e, DateTime.Now);
                Append(CommandTerminalEntryKind.Interaction, e.Content);
                RefreshForm();
            }
        }

        private async void Form_CommandSubmitted(object sender, CommandTerminalCommandSubmittedEventArgs e)
        {
            if (e != null)
            {
                await RunAsync(e.CommandText);
            }
        }

        private async void Form_InputSubmitted(object sender, CommandTerminalInputSubmittedEventArgs e)
        {
            if (!CanSubmitInput() || !await _ensureAvailableAsync())
            {
                RefreshForm();
                return;
            }

            try
            {
                await _appServerClient.WriteCommandInputAsync(_commandTask.ProcessId, e != null ? e.InputText : string.Empty);
                CommandTerminalInteractionState.MarkInputSent(_commandTask, DateTime.Now);
                Append(CommandTerminalEntryKind.Input, e != null ? e.InputText : string.Empty);
            }
            catch (Exception ex)
            {
                Append(CommandTerminalEntryKind.StandardError, ex.Message);
            }

            RefreshForm();
        }

        private async void Form_StopRequested(object sender, EventArgs e)
        {
            if (!CanTerminate() || !await _ensureAvailableAsync())
            {
                return;
            }

            try
            {
                _commandTask.TerminationRequested = true;
                await _appServerClient.TerminateCommandAsync(_commandTask.ProcessId);
                RefreshForm();
            }
            catch (Exception ex)
            {
                Append(CommandTerminalEntryKind.StandardError, ex.Message);
            }
        }

        private void Form_ScreenReaderHotkeyPressed(object sender, CommandTerminalHotkeyEventArgs e)
        {
            ScreenReaderHotkeyPressed?.Invoke(this, e);
        }

        private async void Form_TerminalResizeRequested(object sender, CommandTerminalResizeRequestedEventArgs e)
        {
            if (_commandTask == null || e == null)
            {
                return;
            }

            var plan = _resizePlanner.Plan(_commandTask.ProcessId, e.Size, DateTime.UtcNow);
            if (!plan.ShouldSend)
            {
                return;
            }

            try
            {
                await _appServerClient.ResizeCommandAsync(plan.ProcessId, plan.Size);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to resize detached command terminal process {0}: {1}", plan.ProcessId, ex.Message);
                _resizePlanner.DisableForProcess(plan.ProcessId);
            }
        }

        private void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_form == null)
            {
                return;
            }

            _form.CommandSubmitted -= Form_CommandSubmitted;
            _form.InputSubmitted -= Form_InputSubmitted;
            _form.StopRequested -= Form_StopRequested;
            _form.ScreenReaderHotkeyPressed -= Form_ScreenReaderHotkeyPressed;
            _form.TerminalResizeRequested -= Form_TerminalResizeRequested;
            _form.FormClosed -= Form_FormClosed;
            _form = null;
            TerminalWindowClosed?.Invoke(this, EventArgs.Empty);
        }

        private async Task RunAsync(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText) || HasActiveCommand || !await _ensureAvailableAsync())
            {
                return;
            }

            _commandTask = new CommandTask
            {
                Id = Guid.NewGuid().ToString("N"),
                ProcessId = Guid.NewGuid().ToString("N"),
                SessionId = _session.Id,
                CommandText = commandText,
                WorkingDirectory = _workspace.RootPath,
                Status = "running",
                StartedAt = DateTime.Now,
                ApprovalMode = _approvalModeProvider(_session)
            };
            _buffer.Clear();
            TerminalBufferReset?.Invoke(this, EventArgs.Empty);
            Append(CommandTerminalEntryKind.System, commandText);
            Append(CommandTerminalEntryKind.System, _workspace.RootPath);
            RefreshForm();

            try
            {
                var result = await _appServerClient.ExecuteCommandAsync(
                    commandText,
                    _workspace.RootPath,
                    _commandTask.ApprovalMode,
                    _commandTask.ProcessId,
                    120000,
                    HandleOutput);
                Complete(result);
            }
            catch (Exception ex)
            {
                _commandTask.Status = "failed";
                _commandTask.StdErr = ex.Message;
                _commandTask.Duration = DateTime.Now - _commandTask.StartedAt;
                Append(CommandTerminalEntryKind.StandardError, ex.Message);
            }
            finally
            {
                RefreshForm();
            }
        }

        private void HandleOutput(string text, string stream)
        {
            if (_commandTask == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.Equals(stream, "stderr", StringComparison.OrdinalIgnoreCase))
            {
                _commandTask.StdErr = (_commandTask.StdErr ?? string.Empty) + text;
                Append(CommandTerminalEntryKind.StandardError, text);
            }
            else
            {
                _commandTask.StdOut = (_commandTask.StdOut ?? string.Empty) + text;
                Append(CommandTerminalEntryKind.StandardOutput, text);
            }
        }

        private void Complete(AppServerCommandExecResult result)
        {
            _commandTask.ExitCode = result != null ? result.ExitCode : -1;
            _commandTask.Status = _commandTask.ExitCode == 0
                ? "completed"
                : (_commandTask.TerminationRequested ? "terminated" : "failed");
            _commandTask.IsWaitingForInput = false;
            _commandTask.Duration = DateTime.Now - _commandTask.StartedAt;
            AppendMissingResultOutput(result != null ? result.StdOut : null, CommandTerminalEntryKind.StandardOutput, true);
            AppendMissingResultOutput(result != null ? result.StdErr : null, CommandTerminalEntryKind.StandardError, false);
        }

        private void AppendMissingResultOutput(string output, CommandTerminalEntryKind kind, bool standardOutput)
        {
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            var streamed = standardOutput ? _commandTask.StdOut : _commandTask.StdErr;
            if (string.Equals(streamed, output, StringComparison.Ordinal))
            {
                return;
            }

            Append(kind, output);
        }

        private void Append(CommandTerminalEntryKind kind, string text)
        {
            var form = _form;
            if (form != null && !form.IsDisposed)
            {
                if (form.InvokeRequired)
                {
                    try
                    {
                        form.BeginInvoke(new Action(() => AppendToForm(form, kind, text)));
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                AppendToForm(form, kind, text);
                return;
            }

            _buffer.Append(kind, text);
        }

        private void AppendToForm(CommandTerminalForm form, CommandTerminalEntryKind kind, string text)
        {
            if (ReferenceEquals(_form, form) && !form.IsDisposed)
            {
                form.AppendEntry(kind, text);
                return;
            }

            _buffer.Append(kind, text);
        }

        private bool CanSubmitInput()
        {
            return HasActiveCommand && _commandTask != null && !string.IsNullOrWhiteSpace(_commandTask.ProcessId);
        }

        private bool CanTerminate()
        {
            return CanSubmitInput() && (_commandTask.ApprovalMode == ApprovalMode.FullAccess ||
                                        _commandTask.ApprovalMode == ApprovalMode.FullAccessAutoApprove);
        }

        private void RefreshForm()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.UpdateCommand(_commandTask, !HasActiveCommand, CanSubmitInput(), CanTerminate());
            }
        }
    }
}
