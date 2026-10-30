using System;
using System.IO;
using NUnit.Framework;

// Outside any namespace so it runs once before every test in the assembly.
// Tests leave their scratch folders behind; the runsettings file points WorkDirectory at a dedicated
// TestWork folder, and each run removes what earlier runs left there.
[SetUpFixture]
public sealed class TestWorkDirectoryCleanup
{
    private const string DedicatedFolderName = "TestWork";

    // Entries touched this recently may belong to another test run that is still going.
    private static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(30);

    [OneTimeSetUp]
    public void RemoveEarlierRunLeftovers()
    {
        var workDirectory = new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);
        // Without the runsettings file the work directory is the build output, which must never be emptied.
        if (!string.Equals(workDirectory.Name, DedicatedFolderName, StringComparison.OrdinalIgnoreCase) || !workDirectory.Exists)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - MinimumAge;
        foreach (var entry in workDirectory.EnumerateFileSystemInfos())
        {
            if (entry.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            try
            {
                Delete(entry);
            }
            catch (IOException)
            {
                // A process from an earlier run may still hold a file; the next run tries again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Delete(FileSystemInfo entry)
    {
        // A link is removed itself; its target is outside this folder and is left alone.
        var directory = entry as DirectoryInfo;
        if (directory != null && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            foreach (var child in directory.EnumerateFileSystemInfos())
            {
                Delete(child);
            }
        }

        entry.Attributes = FileAttributes.Normal;
        entry.Delete();
    }
}
