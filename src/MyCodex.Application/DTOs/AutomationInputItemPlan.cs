using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AutomationInputItemPlan
    {
        private AutomationInputItemPlan(bool succeeded, List<AppServerUserInputItem> inputItems, string errorMessage)
        {
            Succeeded = succeeded;
            InputItems = inputItems ?? new List<AppServerUserInputItem>();
            ErrorMessage = errorMessage;
        }

        public bool Succeeded { get; private set; }

        public List<AppServerUserInputItem> InputItems { get; private set; }

        public string ErrorMessage { get; private set; }

        public static AutomationInputItemPlan Ok(List<AppServerUserInputItem> inputItems)
        {
            return new AutomationInputItemPlan(true, inputItems, null);
        }

        public static AutomationInputItemPlan Fail(string errorMessage)
        {
            return new AutomationInputItemPlan(false, new List<AppServerUserInputItem>(), errorMessage);
        }
    }
}
