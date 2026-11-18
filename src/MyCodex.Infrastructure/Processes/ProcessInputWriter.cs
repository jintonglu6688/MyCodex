using System.Diagnostics;
using System.IO;
using System.Text;

namespace MyCodex.Infrastructure.Processes
{
    public static class ProcessInputWriter
    {
        // Process.StandardInput encodes with the console code page (CP936, CP1252...),
        // while our child protocols read UTF-8 JSON lines.
        public static StreamWriter CreateUtf8(Process process)
        {
            return CreateUtf8(process.StandardInput.BaseStream);
        }

        internal static StreamWriter CreateUtf8(Stream stream)
        {
            return new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }
    }
}
