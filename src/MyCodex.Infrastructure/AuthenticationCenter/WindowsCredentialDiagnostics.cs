using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class WindowsCredentialDiagnostics
    {
        internal static IReadOnlyList<string> Collect(AppServerConfigSnapshot config, string legacyRoot, string backendHome,
            Func<string, EnvironmentVariableTarget, bool?> presence = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var records = new List<LegacyRecord>();
            var found = 0;
            var unreadable = 0;
            if (!string.IsNullOrWhiteSpace(legacyRoot))
            {
                foreach (var directory in new[] { "config", Path.Combine("migration-backup", "config"), Path.Combine("migration-backup", "legacy-config") })
                {
                    foreach (var suffix in new[] { "", ".bak" })
                        ReadLegacy(Path.Combine(legacyRoot, directory, "auth-profiles.json" + suffix), legacyRoot, records, ref found, ref unreadable);
                }
            }
            records = records.GroupBy(p => p.Kind + "\0" + p.Id + "\0" + p.ModelProviderId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            var lines = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture,
                    "[AuthenticationCenter] Credential reference diagnostics. Scope={0}; ConfigMatchesBackendHome={1}; ProfileSelected={2}; ReferenceCount={3}; LegacyFilesFound={4}; LegacyFilesUnreadable={5}; LegacyRecords={6}; ReferencesTruncated={7}; BackendReportsUnavailable=True; BackendPerVariableAvailability=unknown",
                    config.CredentialReferenceScope == "user_layer" ? "user_layer" : "effective_config", MatchesHome(config, backendHome),
                    config.CredentialReferenceHasProfile, config.CredentialEnvironmentReferences.Count, found, unreadable, records.Count, config.CredentialReferencesTruncated)
            };
            presence = presence ?? ((name, target) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name, target)));
            foreach (var reference in config.CredentialEnvironmentReferences.Take(32))
            {
                var name = reference.VariableName;
                if (name == null || name.Length > 128 || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant) ||
                    !string.Equals(name, GitOutputSanitizer.RedactSensitiveText(name), StringComparison.Ordinal))
                {
                    lines.Add("[AuthenticationCenter] Credential environment reference. Name=invalid_or_redacted; Availability=unknown");
                    continue;
                }
                var matches = records.Where(p => string.Equals(LegacyVariableName(p), name, StringComparison.OrdinalIgnoreCase)).ToList();
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "[AuthenticationCenter] Credential environment reference. Field={0}; Name={1}; MyCodexProcessPresent={2}; UserPresent={3}; MachinePresent={4}; LegacyEnvironmentMatches={5}; LegacyProviderMatches={6}",
                    reference.Field == "env_key" ? "env_key" : "env_http_headers", name,
                    Presence(presence, name, EnvironmentVariableTarget.Process), Presence(presence, name, EnvironmentVariableTarget.User),
                    Presence(presence, name, EnvironmentVariableTarget.Machine), matches.Count,
                    matches.Count(p => string.Equals(p.Kind == "openai_api" ? "mycodex_openai_api" : p.ModelProviderId,
                        config.CredentialReferenceProviderId, StringComparison.OrdinalIgnoreCase))));
            }
            return lines;
        }

        private static string Presence(Func<string, EnvironmentVariableTarget, bool?> read, string name, EnvironmentVariableTarget target)
        {
            try { return read(name, target)?.ToString() ?? "unknown"; }
            catch { return "unknown"; }
        }

        private static string MatchesHome(AppServerConfigSnapshot config, string home)
        {
            if (config.CredentialReferenceScope != "user_layer" || string.IsNullOrEmpty(home) || string.IsNullOrEmpty(config.CredentialReferenceFilePath)) return "unknown";
            try
            {
                return string.Equals(
                    AppServerPathUtilities.NormalizeLocalPath(AppServerPathUtilities.NormalizeDisplayPath(config.CredentialReferenceFilePath)),
                    AppServerPathUtilities.NormalizeLocalPath(Path.Combine(home, "config.toml")), StringComparison.OrdinalIgnoreCase).ToString();
            }
            catch { return "unknown"; }
        }

        // Compatibility metadata only. The old encrypted key, suffix, URL and name have no DTO fields and are never decrypted.
        private static void ReadLegacy(string path, string root, List<LegacyRecord> records, ref int found, ref int unreadable)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                found++;
                for (var current = path; current != null; current = Path.GetDirectoryName(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
                    if (string.Equals(Path.GetFullPath(current), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) break;
                }
                if ((attributes & FileAttributes.Directory) != 0) throw new InvalidDataException();
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException();
                    var bytes = new byte[(int)file.Length];
                    var count = 0;
                    int read;
                    while (count < bytes.Length && (read = file.Read(bytes, count, bytes.Length - count)) > 0) count += read;
                    if (file.ReadByte() != -1) throw new InvalidDataException();
                    using (var input = new MemoryStream(bytes, 0, count, false))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(LegacyFile), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 4096 });
                        var legacy = serializer.ReadObject(input) as LegacyFile;
                        records.AddRange((legacy?.Profiles ?? new List<LegacyRecord>()).Where(p => p != null &&
                            (p.Kind == "openai_api" || p.Kind == "openai_compatible")).Take(256));
                    }
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch { unreadable++; }
        }

        private static string LegacyVariableName(LegacyRecord profile)
        {
            if (profile.Kind == "openai_api") return "OPENAI_API_KEY";
            var source = string.IsNullOrWhiteSpace(profile.ModelProviderId) ? profile.Id : profile.ModelProviderId;
            source = string.IsNullOrWhiteSpace(source) ? "PROFILE" : source.Trim();
            if (source.Length > 128) return null;
            var normalized = new StringBuilder();
            foreach (var c in source)
                normalized.Append((c >= 'a' && c <= 'z') ? char.ToUpperInvariant(c) :
                    (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ? c : '_');
            return "MYCODEX_" + normalized + "_API_KEY";
        }

        [DataContract]
        private sealed class LegacyFile
        {
            [DataMember] public List<LegacyRecord> Profiles { get; set; }
        }

        [DataContract]
        private sealed class LegacyRecord
        {
            [DataMember] public string Id { get; set; }
            [DataMember] public string Kind { get; set; }
            [DataMember] public string ModelProviderId { get; set; }
        }
    }
}
