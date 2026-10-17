namespace MyCodex.Application.DTOs
{
    using System.Collections.Generic;

    public class AppServerModelOption
    {
        public AppServerModelOption()
        {
            ReasoningEfforts = new List<AppServerModelEffortOption>();
            ServiceTiers = new List<AppServerModelServiceTier>();
        }

        public string ModelId { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public bool IsDefault { get; set; }

        public string DefaultReasoningEffort { get; set; }

        public List<AppServerModelEffortOption> ReasoningEfforts { get; }

        public List<AppServerModelServiceTier> ServiceTiers { get; }

        public string DefaultServiceTier { get; set; }
    }
}
