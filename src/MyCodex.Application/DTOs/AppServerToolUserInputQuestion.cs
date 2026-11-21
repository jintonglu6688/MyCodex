using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerToolUserInputQuestion
    {
        public AppServerToolUserInputQuestion()
        {
            Options = new List<AppServerToolUserInputOption>();
        }

        public string Id { get; set; }

        public string Header { get; set; }

        public string Question { get; set; }

        public string Description { get; set; }

        public bool IsOther { get; set; }

        public bool IsSecret { get; set; }

        // An optional question may be left unanswered; its answer is then omitted.
        public bool IsOptional { get; set; }

        public List<AppServerToolUserInputOption> Options { get; }
    }
}
