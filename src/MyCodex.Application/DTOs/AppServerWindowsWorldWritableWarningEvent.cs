using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerWindowsWorldWritableWarningEvent
    {
        public AppServerWindowsWorldWritableWarningEvent()
        {
            SamplePaths = new List<string>();
        }

        public List<string> SamplePaths { get; private set; }

        public uint ExtraCount { get; set; }

        public bool FailedScan { get; set; }
    }
}
