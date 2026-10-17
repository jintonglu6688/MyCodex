using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task<string> GenerateProjectInstructionsContentAsync(WorkspaceInfo workspace, bool improveExisting, string existingContent, System.Threading.CancellationToken cancellationToken)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.ProjectInstructions.Action.Generate", "Generate project instructions")))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.ProjectInstructions.Error.AppServerUnavailable", "App Server is not connected."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var modelId = GetEffectiveModelId(_currentSession);
            var reasoningEffort = GetEffectiveReasoningEffort(_currentSession);
            var approvalMode = GetEffectiveApprovalMode(_currentSession);
            var threadId = await _appServerClient.CreateThreadAsync(
                workspace.RootPath,
                approvalMode,
                modelId,
                reasoningEffort).ConfigureAwait(true);
            _projectInstructionsHelperThreadIds.Add(threadId);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _appServerClient.SetThreadNameAsync(
                    threadId,
                    UiLocalization.Text("MainForm.ProjectInstructions.ThreadTitle", "Generate project instructions")).ConfigureAwait(true);
            }
            catch
            {
                // Naming the helper thread is best-effort only.
            }

            var prompt = improveExisting
                ? ProjectInstructionsService.BuildImprovementPrompt(existingContent)
                : ProjectInstructionsService.BuildGenerationPrompt();

            try
            {
                return await SendProjectInstructionsPromptAsync(
                    threadId,
                    workspace.RootPath,
                    prompt,
                    approvalMode,
                    modelId,
                    reasoningEffort,
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                await CleanupProjectInstructionsHelperThreadAsync(threadId).ConfigureAwait(true);
            }
        }

        private async Task CleanupProjectInstructionsHelperThreadAsync(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            string sessionPath = null;
            try
            {
                var details = await _appServerClient.ReadThreadAsync(threadId).ConfigureAwait(true);
                sessionPath = details != null ? details.SessionPath : null;
            }
            catch
            {
                // Best-effort cleanup only.
            }

            try
            {
                await _appServerClient.UnsubscribeThreadAsync(threadId).ConfigureAwait(true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                return;
            }

            try
            {
                await _appServerClient.DeleteThreadFileAsync(sessionPath).ConfigureAwait(true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            RemoveProjectInstructionsHelperSessionLocally(threadId);
        }

        private async Task<string> SendProjectInstructionsPromptAsync(
            string threadId,
            string workspacePath,
            string prompt,
            ApprovalMode approvalMode,
            string modelId,
            string reasoningEffort,
            System.Threading.CancellationToken cancellationToken)
        {
            var assistantText = new StringBuilder();
            var completion = new TaskCompletionSource<bool>();
            EventHandler<AppServerEventArgs> handler = (sender, e) =>
            {
                if (e == null ||
                    string.IsNullOrWhiteSpace(e.ThreadId) ||
                    !string.Equals(e.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var eventType = (e.EventType ?? string.Empty).Trim();
                if (string.Equals(eventType, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    lock (assistantText)
                    {
                        assistantText.AppendLine(e.Content ?? string.Empty);
                    }
                    return;
                }

                if (string.Equals(eventType, "turn_completed", StringComparison.OrdinalIgnoreCase))
                {
                    completion.TrySetResult(true);
                    return;
                }

                if (string.Equals(eventType, "turn_failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
                {
                    completion.TrySetException(new InvalidOperationException(e.Content));
                }
            };

            _appServerClient.EventReceived += handler;
            using (cancellationToken.Register(() =>
            {
                completion.TrySetCanceled();
                try
                {
                    _ = _appServerClient.InterruptAsync(threadId);
                }
                catch
                {
                }
            }))
            try
            {
                await _appServerClient.SendUserMessageAsync(
                    threadId,
                    workspacePath,
                    new[]
                    {
                        new AppServerUserInputItem
                        {
                            Type = "text",
                            Text = prompt
                        }
                    },
                    approvalMode,
                    modelId,
                    reasoningEffort).ConfigureAwait(true);

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10));
                var completedTask = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(true);
                if (completedTask == timeoutTask)
                {
                    // Stop the background turn so it does not keep running after the caller gives up.
                    _ = _appServerClient.InterruptAsync(threadId).ContinueWith(
                        task => Trace.TraceWarning("Failed to interrupt timed-out project instructions turn: {0}", task.Exception.GetBaseException().Message),
                        TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException(UiLocalization.Text("MainForm.ProjectInstructions.Error.GenerateTimeout", "Timed out while generating project instructions."));
                }

                await completion.Task.ConfigureAwait(true);
            }
            finally
            {
                _appServerClient.EventReceived -= handler;
            }

            var generated = assistantText.ToString().Trim();
            if (string.IsNullOrWhiteSpace(generated))
            {
                var details = await _appServerClient.ReadThreadAsync(threadId).ConfigureAwait(true);
                if (details != null && details.Messages != null)
                {
                    for (var i = details.Messages.Count - 1; i >= 0; i--)
                    {
                        var message = details.Messages[i];
                        if (message != null &&
                            string.Equals(message.Sender, "Codex", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(message.Content))
                        {
                            generated = message.Content.Trim();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(generated))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.ProjectInstructions.Error.EmptyGeneration", "The model did not return project instructions."));
            }

            return generated;
        }

        private static string BuildProjectInstructionsBasicTemplateFallback()
        {
            return "# AGENTS.md instructions\r\n\r\n" +
                   "## Project Overview\r\n\r\n" +
                   "Describe what this repository is for and the main application entry points.\r\n\r\n" +
                   "## Build And Test\r\n\r\n" +
                   "- Build:\r\n" +
                   "- Test:\r\n" +
                   "- Other verification:\r\n\r\n" +
                   "## Coding Conventions\r\n\r\n" +
                   "- Follow existing project structure and naming.\r\n" +
                   "- Keep changes scoped to the requested task.\r\n" +
                   "- Do not perform broad refactors unless the task requires it.\r\n\r\n" +
                   "## Accessibility\r\n\r\n" +
                   "- Keep control AccessibleName values semantic and concise.\r\n" +
                   "- Do not trigger expensive work on focus changes.\r\n" +
                   "- Actions should be triggered explicitly by the user.\r\n\r\n" +
                   "## Done Criteria\r\n\r\n" +
                   "Describe what should be checked before considering work complete.";
        }
    }
}
