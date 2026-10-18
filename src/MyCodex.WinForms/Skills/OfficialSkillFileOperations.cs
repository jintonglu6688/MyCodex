using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Skills
{
    internal sealed class OfficialSkillFileOperations
    {
        private const string SkillFileName = "SKILL.md";
        private readonly IAppServerFileSystemClient _fileSystemClient;
        private readonly string _workspacePath;
        private readonly List<SkillSaveLocation> _approvedLocations = new List<SkillSaveLocation>();

        public OfficialSkillFileOperations(IAppServerFileSystemClient fileSystemClient, string workspacePath)
        {
            _fileSystemClient = fileSystemClient ?? throw new ArgumentNullException(nameof(fileSystemClient));
            _workspacePath = PortableSkillPath.Normalize(workspacePath);
        }

        public void RefreshObservations(IEnumerable<AppServerSkillInfo> skills)
        {
            _approvedLocations.Clear();
            foreach (var skill in skills ?? Enumerable.Empty<AppServerSkillInfo>())
            {
                if (skill == null || string.IsNullOrWhiteSpace(skill.Path))
                {
                    continue;
                }

                var kind = (skill.Scope ?? string.Empty).Trim();
                if (!string.Equals(kind, "user", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(kind, "repo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var skillDirectory = ResolveSkillDirectory(skill.Path);
                var root = GetParentPath(skillDirectory);
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                var locationKind = string.Equals(kind, "user", StringComparison.OrdinalIgnoreCase)
                    ? SkillSaveLocationKind.User
                    : SkillSaveLocationKind.Workspace;
                if (locationKind == SkillSaveLocationKind.Workspace && !IsUnderRoot(skillDirectory, CombinePath(_workspacePath, ".codex", "skills")))
                {
                    continue;
                }

                if (_approvedLocations.Any(item => item.Kind == locationKind && PathsEqual(item.RootPath, root)))
                {
                    continue;
                }

                _approvedLocations.Add(new SkillSaveLocation(
                    locationKind,
                    locationKind == SkillSaveLocationKind.User
                        ? string.Format(UiLocalization.Text("SkillEditForm.Location.User.Template", "User: {0}"), root)
                        : string.Format(UiLocalization.Text("SkillEditForm.Location.Workspace.Template", "Workspace: {0}"), root),
                    root));
            }
        }

        public IList<SkillSaveLocation> GetSaveLocations()
        {
            return _approvedLocations
                .Select(item => new SkillSaveLocation(item.Kind, item.DisplayName, item.RootPath))
                .ToList();
        }

        public bool CanEdit(AppServerSkillInfo skill, out string reason)
        {
            reason = string.Empty;
            if (skill == null || string.IsNullOrWhiteSpace(skill.Path))
            {
                reason = "This skill does not provide a current App Server path observation.";
                return false;
            }

            var scope = (skill.Scope ?? string.Empty).Trim();
            if (!string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scope, "repo", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Plugin, system, and unsupported skills are read-only.";
                return false;
            }

            var directory = ResolveSkillDirectory(skill.Path);
            if (_approvedLocations.Any(location =>
                    (location.Kind == SkillSaveLocationKind.User && string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) ||
                     location.Kind == SkillSaveLocationKind.Workspace && string.Equals(scope, "repo", StringComparison.OrdinalIgnoreCase)) &&
                    IsDirectChildOf(directory, location.RootPath)))
            {
                return true;
            }

            reason = "This skill is outside the current App Server observation and cannot be changed.";
            return false;
        }

        public SkillValidationResult Validate(SkillFileModel model, bool isNew)
        {
            var result = SkillFileEditor.ValidateContent(model);
            if (model == null || model.Location == null ||
                !_approvedLocations.Any(item => item.Kind == model.Location.Kind && PathsEqual(item.RootPath, model.Location.RootPath)))
            {
                result.AddError("Choose a save location from the current App Server observation.");
                return result;
            }

            var targetPath = BuildTargetPath(model, isNew);
            if (string.IsNullOrWhiteSpace(targetPath) || !IsDirectChildOf(targetPath, model.Location.RootPath))
            {
                result.AddError("The target path is outside the approved Skill location.");
            }

            return result;
        }

        public string BuildTargetPath(SkillFileModel model, bool isNew)
        {
            if (model == null)
            {
                return string.Empty;
            }

            if (!isNew)
            {
                return ResolveSkillDirectory(model.SkillPath);
            }

            return model.Location == null || string.IsNullOrWhiteSpace(model.Name)
                ? string.Empty
                : CombinePath(model.Location.RootPath, model.Name.Trim());
        }

        public async Task<SkillFileModel> LoadForEditAsync(AppServerSkillInfo skill)
        {
            string reason;
            if (!CanEdit(skill, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            var directory = ResolveSkillDirectory(skill.Path);
            var skillFilePath = CombinePath(directory, SkillFileName);
            var result = await _fileSystemClient.ReadFileAsync(skillFilePath).ConfigureAwait(false);
            var content = DecodeUtf8(result == null ? null : result.DataBase64);
            var location = _approvedLocations.First(item => IsDirectChildOf(directory, item.RootPath));
            return SkillFileEditor.LoadForEdit(skill, directory, content, null, location);
        }

        public async Task<string> SaveAsync(SkillFileModel model, bool isNew)
        {
            var validation = Validate(model, isNew);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.BuildMessage());
            }

            var targetDirectory = BuildTargetPath(model, isNew);
            if (isNew)
            {
                var existingEntries = await _fileSystemClient.ReadDirectoryAsync(model.Location.RootPath).ConfigureAwait(false);
                if ((existingEntries ?? new List<AppServerFileSystemEntry>()).Any(item =>
                        item != null && string.Equals(item.FileName, model.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("A skill with this internal name already exists in the selected location.");
                }

                await _fileSystemClient.CreateDirectoryAsync(targetDirectory, true).ConfigureAwait(false);
            }

            await _fileSystemClient.WriteFileAsync(
                CombinePath(targetDirectory, SkillFileName),
                Encoding.UTF8.GetBytes(SkillFileEditor.BuildSkillMarkdownText(model))).ConfigureAwait(false);
            return targetDirectory;
        }

        public async Task DeleteAsync(AppServerSkillInfo skill)
        {
            string reason;
            if (!CanEdit(skill, out reason) || !string.Equals((skill.Scope ?? string.Empty).Trim(), "user", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? "Only observed user skills can be deleted." : reason);
            }

            await _fileSystemClient.RemoveAsync(ResolveSkillDirectory(skill.Path), true, false).ConfigureAwait(false);
        }

        public static bool PathsReferToSameSkill(string left, string right)
        {
            return PathsEqual(ResolveSkillDirectory(left), ResolveSkillDirectory(right));
        }

        private static string DecodeUtf8(string dataBase64)
        {
            if (string.IsNullOrWhiteSpace(dataBase64))
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(dataBase64));
        }

        private static string ResolveSkillDirectory(string path)
        {
            var normalized = PortableSkillPath.Normalize(path);
            return normalized.EndsWith("/" + SkillFileName, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(0, normalized.Length - SkillFileName.Length - 1)
                : normalized;
        }

        private static string CombinePath(params string[] parts)
        {
            return string.Join("/", (parts ?? new string[0])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select((item, index) => index == 0 ? PortableSkillPath.Normalize(item) : item.Trim().Trim('/', '\\')));
        }

        private static string GetParentPath(string path)
        {
            var normalized = PortableSkillPath.Normalize(path);
            var separator = normalized.LastIndexOf('/');
            return separator <= 0 ? string.Empty : normalized.Substring(0, separator);
        }

        private static bool IsDirectChildOf(string path, string root)
        {
            return PathsEqual(GetParentPath(path), root);
        }

        private static bool IsUnderRoot(string path, string root)
        {
            var normalizedPath = PortableSkillPath.Normalize(path);
            var normalizedRoot = PortableSkillPath.Normalize(root).TrimEnd('/');
            return normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(PortableSkillPath.Normalize(left), PortableSkillPath.Normalize(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
