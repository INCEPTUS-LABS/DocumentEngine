namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Declares whether a successfully committed Command participates in linear History.
/// </summary>
public enum HistoryRecordingBehavior
{
    NotUndoable = 0,
    Undoable = 1,
}
