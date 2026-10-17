namespace MyCodex.Application.DTOs
{
    public enum ApprovalRequestConfidence
    {
        Unknown = 0,
        HeuristicCommand = 1,
        HighConfidenceCommand = 2,
        ExactFromProtocol = 3
    }
}
