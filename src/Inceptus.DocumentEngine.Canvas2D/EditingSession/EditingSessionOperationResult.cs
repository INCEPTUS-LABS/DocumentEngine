using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed class EditingSessionOperationResult
{
    internal EditingSessionOperationResult(
        EditingSessionOperationStatus status,
        EditingSessionState state,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        ArgumentNullException.ThrowIfNull(state);
        Status = status;
        State = state;
        Diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    public EditingSessionOperationStatus Status { get; }

    public EditingSessionState State { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Succeeded => Status == EditingSessionOperationStatus.Succeeded;
}
