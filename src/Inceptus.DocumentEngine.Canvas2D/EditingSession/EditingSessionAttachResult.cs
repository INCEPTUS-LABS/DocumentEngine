using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed class EditingSessionAttachResult
{
    internal EditingSessionAttachResult(
        EditingSessionAttachStatus status,
        EditingSession? session,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        if (status is EditingSessionAttachStatus.Ready or EditingSessionAttachStatus.RuntimeFaulted)
        {
            ArgumentNullException.ThrowIfNull(session);
        }
        else if (session is not null)
        {
            throw new ArgumentException(
                "A failed or cancelled attachment cannot expose a partial Editing Session.",
                nameof(session));
        }

        Status = status;
        Session = session;
        Diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    public EditingSessionAttachStatus Status { get; }

    public EditingSession? Session { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsAttached => Session is not null;
}
