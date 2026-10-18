using System;
using System.ComponentModel;
using System.Diagnostics;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class TextFileOpenService
    {
        private readonly Action<TextFileExternalEditorCommand> _launchExternalEditor;
        private readonly Action<TextFileOpenRequest> _openInternalEditor;
        private readonly TextFileExternalEditorPreferenceResolver _externalEditorPreferenceResolver;

        public TextFileOpenService(
            Action<TextFileExternalEditorCommand> launchExternalEditor,
            Action<TextFileOpenRequest> openInternalEditor,
            TextFileExternalEditorPreferenceResolver externalEditorPreferenceResolver = null)
        {
            _launchExternalEditor = launchExternalEditor ?? LaunchExternalEditor;
            _openInternalEditor = openInternalEditor ?? throw new ArgumentNullException(nameof(openInternalEditor));
            _externalEditorPreferenceResolver = externalEditorPreferenceResolver ?? new TextFileExternalEditorPreferenceResolver();
        }

        public TextFileOpenResult Open(TextFileOpenPreference preference, TextFileOpenRequest request)
        {
            preference = preference ?? new TextFileOpenPreference();
            request = request ?? new TextFileOpenRequest();
            var mode = TextFileOpenMode.Normalize(preference.Mode);

            if (string.Equals(mode, TextFileOpenMode.Internal, StringComparison.OrdinalIgnoreCase) || !request.IsLocalWindowsFile)
            {
                _openInternalEditor(request);
                return TextFileOpenResult.Success(true);
            }

            if (string.Equals(mode, TextFileOpenMode.Custom, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(preference.ExternalEditorPath))
            {
                return TextFileOpenResult.Failure(
                    UiLocalization.Text(
                        "TextFileOpenService.Error.CustomEditorPathMissing",
                        "The custom text editor executable is not configured."));
            }

            try
            {
                _launchExternalEditor(TextFileExternalEditorCommandBuilder.Build(
                    _externalEditorPreferenceResolver.Resolve(preference),
                    request));
                return TextFileOpenResult.Success(false);
            }
            catch (Win32Exception ex)
            {
                return TextFileOpenResult.Failure(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return TextFileOpenResult.Failure(ex.Message);
            }
        }

        private static void LaunchExternalEditor(TextFileExternalEditorCommand command)
        {
            command = command ?? new TextFileExternalEditorCommand();
            Process.Start(new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                Arguments = command.Arguments,
                UseShellExecute = command.UseShellExecute
            });
        }
    }
}
