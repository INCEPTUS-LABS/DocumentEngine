using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Canvas2D.Publishing;

/// <summary>
/// One immutable, read-only Document and final presentation tuple captured from a current
/// Editing Session generation. The Scene is detached and is never installed into the editor.
/// </summary>
public sealed record EditingSessionPresentationCapture(
    DocumentSnapshot Document,
    DocumentScopeId ActiveScopeId,
    EditingSessionGeneration Generation,
    ProjectedGraph ProjectedGraph,
    LayoutResult LayoutResult,
    RoutingResult RoutingResult,
    Canvas2DScene Scene);

public enum EditingSessionPresentationCaptureStatus
{
    Succeeded,
    Unavailable,
    Failed,
    Cancelled,
}

public sealed record EditingSessionPresentationCaptureResult
{
    private EditingSessionPresentationCaptureResult(
        EditingSessionPresentationCaptureStatus status,
        EditingSessionPresentationCapture? capture,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Status = status;
        Capture = capture;
        Diagnostics = diagnostics?.ToImmutableArray() ?? [];
    }

    public EditingSessionPresentationCaptureStatus Status { get; }

    public EditingSessionPresentationCapture? Capture { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Succeeded =>
        Status == EditingSessionPresentationCaptureStatus.Succeeded && Capture is not null;

    internal static EditingSessionPresentationCaptureResult Success(
        EditingSessionPresentationCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new(EditingSessionPresentationCaptureStatus.Succeeded, capture, null);
    }

    internal static EditingSessionPresentationCaptureResult Failure(
        EditingSessionPresentationCaptureStatus status,
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (status == EditingSessionPresentationCaptureStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new(status, null, diagnostics);
    }
}
