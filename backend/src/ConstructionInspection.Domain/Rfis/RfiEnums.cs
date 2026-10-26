namespace ConstructionInspection.Domain.Rfis;

public enum RfiStatus
{
    Draft = 0,
    Open = 1,
    Responded = 2,
    Closed = 3,
    Cancelled = 4
}

public enum RfiEntryType
{
    Response = 0,
    ClarificationRequest = 1
}
