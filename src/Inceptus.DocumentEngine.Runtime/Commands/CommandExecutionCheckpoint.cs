namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Internal verification seam for transaction creation and the final pre-install
/// cancellation boundary. It is not part of the public Command execution contract.
/// </summary>
internal enum CommandExecutionCheckpoint
{
    TransactionCreated,
    BeforeFinalCancellationCheck,
    AfterFinalCancellationCheck,
}
