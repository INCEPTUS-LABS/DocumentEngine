using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed class Canvas2DInteractionResult
{
    internal Canvas2DInteractionResult(
        Canvas2DInteractionStatus status,
        EditingSessionState sessionState,
        Canvas2DSceneHitTestResult? hitResult = null,
        IEnumerable<Diagnostic>? diagnostics = null,
        HistoryOperationResult? persistentOperation = null,
        string cssCursor = "default",
        Canvas2DConnectorRouteContextAction? connectorRouteContextAction = null,
        Canvas2DConnectorAnchorContextAction? connectorAnchorContextAction = null,
        Canvas2DNodeLabelContextAction? nodeLabelContextAction = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "The status must be defined.");
        }

        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentException.ThrowIfNullOrWhiteSpace(cssCursor);
        Status = status;
        SessionState = sessionState;
        HitResult = hitResult;
        PersistentOperation = persistentOperation;
        ConnectorRouteContextAction = connectorRouteContextAction;
        ConnectorAnchorContextAction = connectorAnchorContextAction;
        NodeLabelContextAction = nodeLabelContextAction;
        CssCursor = cssCursor;
        Diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    public Canvas2DInteractionStatus Status { get; }

    public EditingSessionState SessionState { get; }

    public Canvas2DSceneHitTestResult? HitResult { get; }

    public SceneObjectId? TargetId => HitResult?.SceneObjectId;

    public Canvas2DSceneOriginTrace? TargetOrigin => HitResult?.Origin;

    public PointD? DocumentPoint => HitResult?.DocumentPoint;

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public string CssCursor { get; }

    /// <summary>
    /// Gets the canonical History-aware Command result when this interaction completed a
    /// persistent gesture; otherwise <see langword="null"/>.
    /// </summary>
    public HistoryOperationResult? PersistentOperation { get; }

    /// <summary>
    /// Gets a transient connector-route menu action when the exact context target supports one.
    /// </summary>
    public Canvas2DConnectorRouteContextAction? ConnectorRouteContextAction { get; }

    /// <summary>
    /// Gets a transient node connector-anchor menu action when the exact context target supports
    /// one.
    /// </summary>
    public Canvas2DConnectorAnchorContextAction? ConnectorAnchorContextAction { get; }

    /// <summary>
    /// Gets a transient reset action when the exact context target is a manually placed
    /// node-owned label.
    /// </summary>
    public Canvas2DNodeLabelContextAction? NodeLabelContextAction { get; }

    public bool Succeeded => Status is
        Canvas2DInteractionStatus.Updated or
        Canvas2DInteractionStatus.Unchanged or
        Canvas2DInteractionStatus.Committed;

    public bool StateChanged => Status is
        Canvas2DInteractionStatus.Updated or Canvas2DInteractionStatus.Committed;
}
