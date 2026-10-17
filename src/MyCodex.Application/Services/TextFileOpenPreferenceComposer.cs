using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public static class TextFileOpenPreferenceComposer
    {
        public static TextFileOpenPreference FromSnapshot(AppConfigSnapshot snapshot)
        {
            snapshot = snapshot ?? new AppConfigSnapshot();
            return new TextFileOpenPreference
            {
                Mode = TextFileOpenMode.Normalize(snapshot.TextFileOpenMode),
                ExternalEditorPath = snapshot.TextFileExternalEditorPath,
                ExternalEditorArguments = snapshot.TextFileExternalEditorArguments
            };
        }
    }
}
