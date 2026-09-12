namespace Inceptus.DocumentEngine.Contracts.Commands;

public enum CommandExecutionStatus
{
    Committed = 0,
    EnvelopeValidationFailed = 1,
    DelegatedValidationFailed = 2,
    HandlerFailed = 3,
    ProposedStateValidationFailed = 4,
    Cancelled = 5,
    InternalFailure = 6,
}
