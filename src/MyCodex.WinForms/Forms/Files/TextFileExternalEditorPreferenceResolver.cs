using System;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal sealed class TextFileExternalEditorPreferenceResolver
    {
        private readonly Func<string> _resolveNotepadPlusPlusExecutable;

        public TextFileExternalEditorPreferenceResolver()
            : this(() => new NotepadPlusPlusExecutableResolver().Resolve())
        {
        }

        internal TextFileExternalEditorPreferenceResolver(Func<string> resolveNotepadPlusPlusExecutable)
        {
            _resolveNotepadPlusPlusExecutable = resolveNotepadPlusPlusExecutable ?? (() => null);
        }

        public TextFileOpenPreference Resolve(TextFileOpenPreference preference)
        {
            preference = preference ?? new TextFileOpenPreference();
            if (!string.Equals(TextFileOpenMode.Normalize(preference.Mode), TextFileOpenMode.NotepadPlusPlus, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(preference.ExternalEditorPath))
            {
                return preference;
            }

            var resolvedPath = _resolveNotepadPlusPlusExecutable();
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return preference;
            }

            return new TextFileOpenPreference
            {
                Mode = preference.Mode,
                ExternalEditorPath = resolvedPath.Trim(),
                ExternalEditorArguments = preference.ExternalEditorArguments
            };
        }
    }
}
