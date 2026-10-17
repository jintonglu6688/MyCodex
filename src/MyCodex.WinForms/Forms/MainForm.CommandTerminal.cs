using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void OnCommandMenuButtonClick(object sender, EventArgs e)
        {
            ShowCommandTerminal();
        }

        private void CommandMenuButton_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)
            {
                return;
            }

            ShowCommandTerminal();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private bool CanSubmitCommandTerminalInput(CommandTask commandTask)
        {
            return CommandTerminalSelectionState.CanSubmitInput(_activeCommandTask, commandTask);
        }

        private CommandTask GetTerminalCommand()
        {
            return CommandTerminalSelectionState.GetTerminalCommand(_activeCommandTask, _commandTasks);
        }

        private void ShowCommandTerminal()
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                return;
            }

            var terminalCommand = GetTerminalCommand();
            EnsureCommandTerminalBuffer(terminalCommand);

            if (_commandTerminalForm != null && !_commandTerminalForm.IsDisposed)
            {
                RefreshCommandTerminalWindow(terminalCommand);
                _commandTerminalForm.Activate();
                _commandTerminalForm.Focus();
                return;
            }

            _commandTerminalForm = new CommandTerminalForm(
                terminalCommand,
                _commandTerminalBuffer,
                IsCommandAvailable(),
                CanSubmitCommandTerminalInput(terminalCommand),
                CanTerminateCommand(terminalCommand));
            _commandTerminalForm.CommandSubmitted += CommandTerminalForm_CommandSubmitted;
            _commandTerminalForm.InputSubmitted += CommandTerminalForm_InputSubmitted;
            _commandTerminalForm.StopRequested += CommandTerminalForm_StopRequested;
            _commandTerminalForm.PresetsRequested += CommandTerminalForm_PresetsRequested;
            _commandTerminalForm.ScreenReaderHotkeyPressed += CommandTerminalForm_ScreenReaderHotkeyPressed;
            _commandTerminalForm.TerminalResizeRequested += CommandTerminalForm_TerminalResizeRequested;
            _commandTerminalForm.FormClosed += CommandTerminalForm_FormClosed;
            _commandTerminalForm.Show();
        }

        private async void CommandTerminalForm_CommandSubmitted(object sender, CommandTerminalCommandSubmittedEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.CommandText))
            {
                return;
            }

            await RunWorkspaceCommandAsync(e.CommandText);
        }

        private async void CommandTerminalForm_InputSubmitted(object sender, CommandTerminalInputSubmittedEventArgs e)
        {
            await SendCommandTerminalInputAsync(e != null ? e.InputText : string.Empty);
        }

        private async void CommandTerminalForm_StopRequested(object sender, EventArgs e)
        {
            await StopActiveCommandAsync();
            RefreshCommandTerminalWindow(_activeCommandTask);
        }

        private async void CommandTerminalForm_PresetsRequested(object sender, EventArgs e)
        {
            await ShowCommandPresetsAsync();
        }

        private void CommandTerminalForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_commandTerminalForm == null)
            {
                return;
            }

            _commandTerminalForm.CommandSubmitted -= CommandTerminalForm_CommandSubmitted;
            _commandTerminalForm.InputSubmitted -= CommandTerminalForm_InputSubmitted;
            _commandTerminalForm.StopRequested -= CommandTerminalForm_StopRequested;
            _commandTerminalForm.PresetsRequested -= CommandTerminalForm_PresetsRequested;
            _commandTerminalForm.ScreenReaderHotkeyPressed -= CommandTerminalForm_ScreenReaderHotkeyPressed;
            _commandTerminalForm.TerminalResizeRequested -= CommandTerminalForm_TerminalResizeRequested;
            _commandTerminalForm.FormClosed -= CommandTerminalForm_FormClosed;
            _commandTerminalForm = null;
        }

        private void CommandTerminalForm_ScreenReaderHotkeyPressed(object sender, CommandTerminalHotkeyEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            e.Handled = _mainManualBrowseContext != null &&
                        _mainManualBrowseContext.HandleTerminalHotkey(e.KeyData);
        }

        private async void CommandTerminalForm_TerminalResizeRequested(object sender, CommandTerminalResizeRequestedEventArgs e)
        {
            if (_activeCommandTask == null || e == null)
            {
                return;
            }

            var plan = _commandTerminalResizePlanner.Plan(_activeCommandTask.ProcessId, e.Size, DateTime.UtcNow);
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
                Trace.TraceWarning("Failed to resize command terminal process {0}: {1}", plan.ProcessId, ex.Message);
                _commandTerminalResizePlanner.DisableForProcess(plan.ProcessId);
            }
        }

        private async Task StopActiveCommandAsync()
        {
            if (_activeCommandTask == null || string.IsNullOrWhiteSpace(_activeCommandTask.ProcessId))
            {
                return;
            }

            if (!CanTerminateCommand(_activeCommandTask))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.StopCommandNotSupportedByPermission", "Current permission mode does not support stopping commands"));
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.StopCommand", "Stop command")))
            {
                return;
            }

            try
            {
                _activeCommandTask.TerminationRequested = true;
                await _appServerClient.TerminateCommandAsync(_activeCommandTask.ProcessId);
                SetStatus(UiLocalization.Text("MainForm.Status.StoppingCommand", "Stopping command..."));
                RefreshCommandTerminalWindow(_activeCommandTask);
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.StopCommandFailed", "Failed to stop command"));
                RefreshCommandTerminalWindow(_activeCommandTask);
            }
        }

        private async Task TerminateAllActiveRuntimeCommandsAsync()
        {
            if (_activeCommandTask != null && !string.IsNullOrWhiteSpace(_activeCommandTask.ProcessId))
            {
                try
                {
                    _activeCommandTask.TerminationRequested = true;
                    await _appServerClient.TerminateCommandAsync(_activeCommandTask.ProcessId);
                }
                catch (Exception ex)
                {
                    AppendLog(ex.Message);
                }
                finally
                {
                    _activeCommandTask.Status = "canceled";
                }
            }

            foreach (var host in _sessionCommandTerminals.Values)
            {
                if (host == null)
                {
                    continue;
                }

                try
                {
                    await host.TerminateActiveCommandForRuntimeTransitionAsync();
                }
                catch (Exception ex)
                {
                    AppendLog(ex.Message);
                }
            }

            _isCommandRunning = false;
            _activeCommandTask = null;
        }

        private static bool CanTerminateCommand(CommandTask commandTask)
        {
            return commandTask != null &&
                   !string.IsNullOrWhiteSpace(commandTask.ProcessId) &&
                   (commandTask.ApprovalMode == ApprovalMode.FullAccess ||
                    commandTask.ApprovalMode == ApprovalMode.FullAccessAutoApprove);
        }

        private async Task SendCommandTerminalInputAsync(string inputText)
        {
            if (!CanSubmitCommandTerminalInput(_activeCommandTask))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.NoActiveCommandTerminal", "No active command terminal"));
                RefreshCommandTerminalWindow(GetTerminalCommand());
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SendCommandInput", "Send command input")))
            {
                RefreshCommandTerminalWindow(GetTerminalCommand());
                return;
            }

            try
            {
                await _appServerClient.WriteCommandInputAsync(_activeCommandTask.ProcessId, inputText ?? string.Empty);
                CommandTerminalInteractionState.MarkInputSent(_activeCommandTask, DateTime.Now);
                AppendCommandTerminalEntry(CommandTerminalEntryKind.Input, (inputText ?? string.Empty).TrimEnd('\r', '\n'));
                SetStatus(UiLocalization.Text("MainForm.Status.CommandInputSent", "Command input sent"));
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CommandInputFailed", "Command input failed"));
            }

            RefreshCommandTerminalWindow(GetTerminalCommand());
        }

        private async Task ShowCommandPresetsAsync()
        {
            var originalDefinitions = GetConfiguredCommandDefinitions();
            using (var form = new CommandPresetForm(
                originalDefinitions,
                GetCurrentCommandHostPlatform(),
                IsCommandAvailable()))
            {
                var owner = _commandTerminalForm != null && !_commandTerminalForm.IsDisposed
                    ? (IWin32Window)_commandTerminalForm
                    : this;
                var result = form.ShowDialog(owner);
                if (TryPersistCommandDefinitions(form.CommandDefinitions, originalDefinitions))
                {
                    SaveAppConfig();
                }

                if (result != DialogResult.OK || string.IsNullOrWhiteSpace(form.SelectedCommandText))
                {
                    return;
                }

                switch (form.SelectedAction)
                {
                    case CommandPresetAction.Insert:
                        if (_commandTerminalForm != null && !_commandTerminalForm.IsDisposed)
                        {
                            _commandTerminalForm.SetInputText(form.SelectedCommandText);
                        }

                        break;
                    case CommandPresetAction.Run:
                        await RunWorkspaceCommandAsync(form.SelectedCommandText);
                        break;
                }
            }
        }

        private async Task RunWorkspaceCommandAsync(string commandText)
        {
            if (_currentWorkspace == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeRunCommand", "Please open a workspace first."),
                    UiLocalization.Text("MainForm.DialogTitle.CannotRunCommand", "Unable to run command"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (_isCommandRunning)
            {
                SetStatus(UiLocalization.Text("MainForm.Status.CommandAlreadyRunning", "A command is already running"));
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.RunCommand", "Run command")))
            {
                return;
            }

            var commandTask = new CommandTask
            {
                Id = Guid.NewGuid().ToString("N"),
                ProcessId = Guid.NewGuid().ToString("N"),
                SessionId = _currentSession != null ? _currentSession.Id : string.Empty,
                CommandText = commandText,
                WorkingDirectory = _currentWorkspace.RootPath,
                Status = "running",
                StartedAt = DateTime.Now,
                ApprovalMode = GetEffectiveApprovalMode(_currentSession)
            };

            _activeCommandTask = commandTask;
            if (_commandTerminalBuffer == null)
            {
                _commandTerminalBuffer = new CommandTerminalBuffer(MaxBufferedCommandOutputCharacters);
            }
            else
            {
                _commandTerminalBuffer.Clear();
            }

            _mainManualBrowseContext?.ResetTerminalCursor();
            AppendCommandTerminalEntry(CommandTerminalEntryKind.System, commandText);
            AppendCommandTerminalEntry(CommandTerminalEntryKind.System, commandTask.WorkingDirectory);
            _isCommandRunning = true;
            _commandTasks.Insert(0, commandTask);
            RefreshCommandState();
            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.CommandStart.Template", "Started: {0}"), commandText));
            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.WorkingDirectory.Template", "Working directory: {0}"), commandTask.WorkingDirectory));
            AddChatHistoryItem(
                ChatHistoryItemFactory.GetStatusSenderText(),
                string.Format(UiLocalization.Text("MainForm.Message.CommandStarted.Template", "Started command: {0}"), commandText),
                ChatHistoryCategory.Command);
            SetStatus(UiLocalization.Text("MainForm.Status.RunningCommand", "Running command..."));

            try
            {
                var result = await _appServerClient.ExecuteCommandAsync(
                    commandText,
                    _currentWorkspace.RootPath,
                    commandTask.ApprovalMode,
                    commandTask.ProcessId,
                    120000,
                    (text, outputStream) => HandleStreamingWorkspaceCommandOutput(commandTask.ProcessId, text, outputStream));

                FlushBufferedCommandOutputEvents();
                CompleteStandaloneCommand(commandTask, result);
            }
            catch (Exception ex)
            {
                commandTask.Status = "failed";
                commandTask.StdErr = ex.Message;
                commandTask.IsWaitingForInput = false;
                AppendCommandTerminalEntry(CommandTerminalEntryKind.StandardError, ex.Message);
                AppendLog(string.Format(UiLocalization.Text("MainForm.Log.CommandFailed.Template", "Execution failed: {0}"), ex.Message));
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CommandExecutionFailed", "Command execution failed"));
            }
            finally
            {
                _isCommandRunning = false;
                _activeCommandTask = null;
                RefreshCommandState();
                RefreshCommandTerminalWindow(GetTerminalCommand());
            }
        }

        private bool IsCommandAvailable()
        {
            return IsCurrentWorkspaceAvailable() && !_isCommandRunning;
        }

        private List<CommandDefinition> GetConfiguredCommandDefinitions()
        {
            var configured = _appConfigSnapshot != null ? _appConfigSnapshot.CommandDefinitions : null;
            return configured == null
                ? BuildDefaultCommandDefinitions()
                : CloneCommandDefinitions(configured);
        }

        private static List<CommandDefinition> BuildDefaultCommandDefinitions()
        {
            return new List<CommandDefinition>
            {
                new CommandDefinition
                {
                    Name = UiLocalization.Text("MainForm.CommandDefinition.BuildRelease", "Build Release"),
                    CommandText = "build.bat",
                    HostPlatform = CommandHostPlatform.Windows
                },
                new CommandDefinition
                {
                    Name = UiLocalization.Text("MainForm.CommandDefinition.GitChanges", "Git Changes"),
                    CommandText = "git status --short",
                    HostPlatform = CommandHostPlatform.Any
                }
            };
        }

        private bool TryPersistCommandDefinitions(IReadOnlyList<CommandDefinition> updatedDefinitions, IReadOnlyList<CommandDefinition> originalDefinitions)
        {
            var sanitized = CloneCommandDefinitions(updatedDefinitions);
            var original = CloneCommandDefinitions(originalDefinitions);
            if (AreCommandDefinitionsEqual(sanitized, original))
            {
                return false;
            }

            _appConfigSnapshot.CommandDefinitions = sanitized;
            return true;
        }

        private static List<CommandDefinition> CloneCommandDefinitions(IReadOnlyList<CommandDefinition> definitions)
        {
            var result = new List<CommandDefinition>();
            if (definitions == null)
            {
                return result;
            }

            for (var i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                var name = (definition.Name ?? string.Empty).Trim();
                var commandText = (definition.CommandText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(commandText))
                {
                    continue;
                }

                result.Add(new CommandDefinition
                {
                    Name = name,
                    CommandText = commandText,
                    HostPlatform = NormalizeCommandHostPlatform(definition.HostPlatform)
                });
            }

            return result;
        }

        private static bool AreCommandDefinitionsEqual(IReadOnlyList<CommandDefinition> left, IReadOnlyList<CommandDefinition> right)
        {
            var leftList = CloneCommandDefinitions(left);
            var rightList = CloneCommandDefinitions(right);
            if (leftList.Count != rightList.Count)
            {
                return false;
            }

            for (var i = 0; i < leftList.Count; i++)
            {
                if (!string.Equals(leftList[i].Name, rightList[i].Name, StringComparison.Ordinal) ||
                    !string.Equals(leftList[i].CommandText, rightList[i].CommandText, StringComparison.Ordinal) ||
                    leftList[i].HostPlatform != rightList[i].HostPlatform)
                {
                    return false;
                }
            }

            return true;
        }

        private AppServerHostPlatform GetCurrentCommandHostPlatform()
        {
            if (_appConfigSnapshot != null &&
                string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                return AppServerHostPlatform.Wsl;
            }

            if (_appConfigSnapshot != null &&
                CodexRuntimeSources.IsRemoteUnixSsh(_appConfigSnapshot.CodexRuntimeSource))
            {
                return AppServerHostPlatform.RemoteMac;
            }

            return AppServerHostPlatform.Windows;
        }

        private static CommandHostPlatform NormalizeCommandHostPlatform(CommandHostPlatform hostPlatform)
        {
            return hostPlatform == CommandHostPlatform.Wsl ||
                   hostPlatform == CommandHostPlatform.RemoteMac ||
                   hostPlatform == CommandHostPlatform.Any
                ? hostPlatform
                : CommandHostPlatform.Windows;
        }
    }
}
