namespace Inceptus.DocumentEngine.Contracts.History;

public enum HistoryOperationStatus
{
    Committed = 0,
    NothingToUndo = 1,
    NothingToRedo = 2,
    CommandFailed = 3,
    Cancelled = 4,
    InternalFailure = 5,
    Applied = 6,
}
