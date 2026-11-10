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

            if (UsesInternalEditor(preference, request.FilePath, request.IsLocalWindowsFile))
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

            return Launch(TextFileExternalEditorCommandBuilder.Build(
                _externalEditorPreferenceResolver.Resolve(preference),
                request));
        }

        // The system default action runs scripts and programs; opening a file to read it must not run it.
        internal static bool UsesInternalEditor(TextFileOpenPreference preference, string path, bool isLocalWindowsFile)
        {
            var mode = TextFileOpenMode.Normalize(preference == null ? null : preference.Mode);
            return string.Equals(mode, TextFileOpenMode.Internal, StringComparison.OrdinalIgnoreCase) || !isLocalWindowsFile ||
                   (string.Equals(mode, TextFileOpenMode.System, StringComparison.OrdinalIgnoreCase) &&
                    TextEditableFilePolicy.RunsWhenOpenedByWindows(path));
        }

        // The file browser's explicit "open with the system app" for files MyCodex cannot show; like Explorer, it runs programs.
        public TextFileOpenResult OpenWithSystemDefault(string path)
        {
            return Launch(new TextFileExternalEditorCommand { ExecutablePath = path, Arguments = string.Empty, UseShellExecute = true });
        }

        private TextFileOpenResult Launch(TextFileExternalEditorCommand command)
        {
            try
            {
                _launchExternalEditor(command);
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
