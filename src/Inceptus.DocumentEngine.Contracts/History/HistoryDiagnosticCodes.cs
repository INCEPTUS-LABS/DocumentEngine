namespace Inceptus.DocumentEngine.Contracts.History;

public static class HistoryDiagnosticCodes
{
    public const string MissingPolicy = "HISTORY_MISSING_POLICY";
    public const string PolicyFailure = "HISTORY_POLICY_FAILURE";
    public const string InvalidPreparation = "HISTORY_INVALID_PREPARATION";
    public const string DuplicatePolicyRegistration = "HISTORY_DUPLICATE_POLICY_REGISTRATION";
    public const string UndoUnavailable = "HISTORY_UNDO_UNAVAILABLE";
    public const string RedoUnavailable = "HISTORY_REDO_UNAVAILABLE";
    public const string CommandFactoryFailure = "HISTORY_COMMAND_FACTORY_FAILURE";
    public const string InvalidRestorationCommand = "HISTORY_INVALID_RESTORATION_COMMAND";
    public const string RuntimeReplayFailed = "HISTORY_RUNTIME_REPLAY_FAILED";
}
