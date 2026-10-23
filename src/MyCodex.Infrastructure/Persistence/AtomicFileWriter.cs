using System;
using System.IO;
using System.Text;

namespace MyCodex.Infrastructure.Persistence
{
    internal static class AtomicFileWriter
    {
        // A unique, disk-flushed temp file swapped into place means a crash or a concurrent writer never leaves a torn file.
        public static void Write(string path, Action<Stream> write, string backupPath = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, backupPath, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        public static void WriteText(string path, string text, string backupPath = null)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
            Write(path, stream => stream.Write(bytes, 0, bytes.Length), backupPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
