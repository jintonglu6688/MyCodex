namespace MyCodex.WinForms.Skills
{
    internal static class PortableSkillPath
    {
        /// <summary>Trims the path, uses forward slashes, and removes trailing slashes; returns an empty string for blank input.</summary>
        internal static string Normalize(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
