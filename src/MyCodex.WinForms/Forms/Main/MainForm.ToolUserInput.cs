using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void ShowToolUserInputDialog(AppServerEventArgs request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return;
            }

            try
            {
                var isMcpElicitation = string.Equals(request.EventType, "mcp_elicitation_requested", StringComparison.OrdinalIgnoreCase);
                if (request.ToolQuestions == null || request.ToolQuestions.Count == 0)
                {
                    if (isMcpElicitation && request.IsMcpElicitationConfirmation)
                    {
                        await ShowMcpElicitationConfirmationDialogAsync(request);
                    }
                    else
                    {
                        await _appServerClient.CancelToolUserInputRequestAsync(request.RequestId);
                        if (isMcpElicitation)
                        {
                            SetHighPriorityStatus(UiLocalization.Text(
                                "MainForm.Status.UnsupportedMcpInputCanceled",
                                "Unsupported MCP input request canceled"));
                        }
                    }

                    return;
                }

                var title = isMcpElicitation
                    ? UiLocalization.Text("ToolUserInputForm.Title.McpElicitation", "MCP Request")
                    : null;
                var header = isMcpElicitation
                    ? string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        UiLocalization.Text("ToolUserInputForm.Header.McpElicitation.Template", "MCP server {0} needs additional input before it can continue."),
                        string.IsNullOrWhiteSpace(request.McpServerName)
                            ? UiLocalization.Text("MainForm.McpElicitation.UnknownServer", "MCP server")
                            : request.McpServerName)
                    : null;

                var isMcpApproval = isMcpElicitation && request.IsMcpElicitationConfirmation;
                if (isMcpApproval)
                {
                    title = UiLocalization.Text("MainForm.McpElicitation.ConfirmationPrompt.Title", "MCP Confirmation");
                    header = request.Content;
                }
                if (!await WaitToPresentTargetInteractionAsync(request.RequestId)) return;
                using (var form = new ToolUserInputForm(request.ToolQuestions, title, header, isMcpApproval))
                {
                    var registration = _remoteInteractionDialogs.Register(request.RequestId, delegate
                    {
                        if (!form.IsDisposed)
                        {
                            form.CloseAsExternallyResolved();
                        }
                    });
                    DialogResult result;
                    try
                    {
                        result = form.ShowDialog(this);
                    }
                    finally
                    {
                        registration.Dispose();
                    }

                    if (form.WasExternallyResolved)
                    {
                        return;
                    }

                    if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
                    {
                        return;
                    }

                    if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
                    {
                        return;
                    }

                    if (result == DialogResult.OK)
                    {
                        await _appServerClient.ResolveToolUserInputRequestAsync(request.RequestId, form.Answers);
                    }
                    else
                    {
                        await _appServerClient.CancelToolUserInputRequestAsync(request.RequestId);
                    }
                }
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ToolInputResponseFailed", "Tool input response failed"));
            }
        }

        private async Task ShowMcpElicitationConfirmationDialogAsync(AppServerEventArgs request)
        {
            if (!await WaitToPresentTargetInteractionAsync(request.RequestId)) return;
            var serverName = string.IsNullOrWhiteSpace(request.McpServerName)
                ? UiLocalization.Text("MainForm.McpElicitation.UnknownServer", "MCP server")
                : request.McpServerName;
            var prompt = string.IsNullOrWhiteSpace(request.Details)
                ? UiLocalization.Text(
                    "MainForm.McpElicitation.ConfirmationPrompt.NoMessage",
                    "This MCP server is requesting permission to continue.")
                : request.Details.Trim();
            var message = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                UiLocalization.Text(
                    "MainForm.McpElicitation.ConfirmationPrompt.Template",
                    "{0} requests confirmation:\r\n\r\n{1}\r\n\r\nAllow it to continue?"),
                serverName,
                prompt);
            var result = MessageBox.Show(
                this,
                message,
                UiLocalization.Text("MainForm.McpElicitation.ConfirmationPrompt.Title", "MCP Confirmation"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
            {
                return;
            }

            if (result == DialogResult.Yes)
            {
                await _appServerClient.ResolveToolUserInputRequestAsync(
                    request.RequestId,
                    new Dictionary<string, string[]>(StringComparer.Ordinal));
            }
            else
            {
                await _appServerClient.CancelToolUserInputRequestAsync(request.RequestId);
            }
        }

        private async void ShowMcpElicitationUrlDialog(AppServerEventArgs request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return;
            }

            try
            {
                if (!await WaitToPresentTargetInteractionAsync(request.RequestId)) return;
                var serverName = string.IsNullOrWhiteSpace(request.McpServerName)
                    ? UiLocalization.Text("MainForm.McpElicitation.UnknownServer", "MCP server")
                    : request.McpServerName;
                var message = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    UiLocalization.Text("MainForm.McpElicitation.UrlPrompt.Template", "{0} wants to open this URL:\r\n\r\n{1}\r\n\r\nOpen it and continue?"),
                    serverName,
                    request.Url ?? string.Empty);
                var result = MessageBox.Show(
                    this,
                    message,
                    UiLocalization.Text("MainForm.McpElicitation.UrlPrompt.Title", "MCP Request"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
                {
                    return;
                }

                if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SubmitToolInput", "Submit tool input")))
                {
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    if (!string.IsNullOrWhiteSpace(request.Url))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = request.Url,
                            UseShellExecute = true
                        });
                    }

                    await _appServerClient.ResolveToolUserInputRequestAsync(request.RequestId, new Dictionary<string, string[]>(StringComparer.Ordinal));
                }
                else
                {
                    await _appServerClient.CancelToolUserInputRequestAsync(request.RequestId);
                }
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ToolInputResponseFailed", "Tool input response failed"));
            }
        }
    }
}
