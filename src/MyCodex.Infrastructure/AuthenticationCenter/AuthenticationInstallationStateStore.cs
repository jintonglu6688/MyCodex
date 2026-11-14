using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal sealed class AuthenticationInstallationStateStore
    {
        internal static readonly string[] PackageFiles = { "mycodex-auth-host.exe", AuthenticationHostArtifact.ManifestFileName, "LICENSE" };
        internal readonly string Root;
        internal readonly string Path;
        internal string PackageDirectory => System.IO.Path.Combine(Root, "runtime", "auth-core");
        internal string Executable => System.IO.Path.Combine(PackageDirectory, PackageFiles[0]);
        internal string ExpectedPath => Path + ".installed";
        internal string BackupDirectory => Path + ".backup";

        internal AuthenticationInstallationStateStore(string root, string path)
        {
            Root = Normalize(root);
            Path = Normalize(path);
        }

        internal sealed class Target
        {
            public string home { get; set; }
            public string store { get; set; }
            public int processId { get; set; }
        }

        internal sealed class State
        {
            public string installRoot { get; set; }
            public List<Target> targets { get; set; } = new List<Target>();
            public bool hadPackage { get; set; }
            public bool completed { get; set; }
            public Dictionary<string, object> expectedPackage { get; set; }
        }

        internal State Read()
        {
            if (!File.Exists(Path)) return null;
            var state = new JavaScriptSerializer().Deserialize<State>(ReadBounded(Path));
            if (state == null || !SamePath(state.installRoot, Root) || state.targets == null || state.targets.Count > 256)
                throw new AuthenticationCenterException("authentication_update_target_mismatch");
            foreach (var target in state.targets)
            {
                if (target == null) throw new AuthenticationCenterException("authentication_update_invalid_target");
                Normalize(target.home);
                Normalize(target.store);
            }
            return state;
        }

        internal void Write(State state)
        {
            foreach (var path in new[] { Path, ExpectedPath, ExpectedPath + ".tmp" })
                AssertRegularPath(path, System.IO.Path.GetDirectoryName(Path));
            AtomicFileWriter.WriteText(Path, new JavaScriptSerializer().Serialize(state));
        }

        internal void Log(string action, string code)
        {
            try
            {
                AssertRegularPath(Path + ".log", System.IO.Path.GetDirectoryName(Path));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.AppendAllText(Path + ".log", DateTime.UtcNow.ToString("o") + " " + action + " " + code + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { /* Diagnostic failures must not change maintenance outcomes. */ }
        }

        internal Dictionary<string, object> Expected(State state)
        {
            return File.Exists(ExpectedPath) ? ReadManifest(ExpectedPath) : state.expectedPackage;
        }

        internal void VerifyExpected(IDictionary<string, object> expected)
        {
            var artifact = AuthenticationHostArtifact.Load(Executable);
            artifact.ValidateIdentity(expected);
            if (!File.Exists(System.IO.Path.Combine(PackageDirectory, "LICENSE")))
                throw new AuthenticationCenterException("authentication_installed_package_invalid");
        }

        internal static bool IsPackageValid(string directory)
        {
            try
            {
                AuthenticationHostArtifact.LoadForUpdate(System.IO.Path.Combine(directory, PackageFiles[0]));
                return File.Exists(System.IO.Path.Combine(directory, "LICENSE"));
            }
            catch { return false; }
        }

        internal void CopyPackage(string source, string destination)
        {
            var allowed = SamePath(destination, PackageDirectory) ? Root :
                SamePath(destination, BackupDirectory) ? System.IO.Path.GetDirectoryName(Path) : null;
            if (allowed == null) throw new AuthenticationCenterException("authentication_update_unsafe_path");
            AssertRegularPath(destination, allowed);
            foreach (var name in PackageFiles) AssertRegularPath(System.IO.Path.Combine(destination, name), allowed);
            Directory.CreateDirectory(destination);
            foreach (var name in PackageFiles) File.Copy(System.IO.Path.Combine(source, name), System.IO.Path.Combine(destination, name), true);
        }

        internal void Complete(State state)
        {
            state.completed = true;
            Write(state);
            if (File.Exists(ExpectedPath)) File.Delete(ExpectedPath);
        }

        internal static Dictionary<string, object> ReadManifest(string path)
        {
            var value = new JavaScriptSerializer().DeserializeObject(ReadBounded(path)) as Dictionary<string, object>;
            if (value == null) throw new AuthenticationCenterException("authentication_update_invalid_package");
            return value;
        }

        private static string ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 1048576) throw new AuthenticationCenterException("authentication_update_invalid_state");
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        internal static bool SameTarget(Target first, Target second)
        {
            return SamePath(first.home, second.home) && SamePath(first.store, second.store);
        }

        internal static bool SamePath(string first, string second)
        {
            return string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
        }

        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new AuthenticationCenterException("authentication_update_invalid_target");
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
            if (!WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(path) && !WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(path))
                throw new AuthenticationCenterException("authentication_update_invalid_target");
            var full = System.IO.Path.GetFullPath(path);
            return full.Length > System.IO.Path.GetPathRoot(full).Length ? full.TrimEnd('\\') : full;
        }

        private static void AssertRegularPath(string path, string allowedRoot)
        {
            var root = Normalize(allowedRoot);
            var current = Normalize(path);
            if (!SamePath(current, root) && !current.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new AuthenticationCenterException("authentication_update_unsafe_path");
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new AuthenticationCenterException("authentication_update_unsafe_path");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (SamePath(current, root)) break;
                current = System.IO.Path.GetDirectoryName(current);
            }
        }
    }
}
