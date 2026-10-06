using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

internal enum EditingSessionPipelineStatus
{
    Succeeded,
    Failed,
    Cancelled,
}

internal sealed class EditingSessionPipelineResult
{
    private EditingSessionPipelineResult(
        EditingSessionPipelineStatus status,
        EditingSessionPipelineArtifacts? artifacts,
        Canvas2DScene? scene,
        IEnumerable<Diagnostic>? diagnostics,
        bool reusedPanContent = false,
        bool reusedMoveContent = false,
        bool reusedSelectionContent = false,
        bool reusedPlacementContent = false,
        bool reusedSpatialResizeContent = false,
        bool reusedNodeLabelMoveContent = false,
        bool reusedRouteBendContent = false,
        bool reusedConnectorLabelMoveContent = false)
    {
        Status = status;
        Artifacts = artifacts;
        Scene = scene;
        ReusedPanContent = reusedPanContent;
        ReusedMoveContent = reusedMoveContent;
        ReusedSelectionContent = reusedSelectionContent;
        ReusedPlacementContent = reusedPlacementContent;
        ReusedSpatialResizeContent = reusedSpatialResizeContent;
        ReusedNodeLabelMoveContent = reusedNodeLabelMoveContent;
        ReusedRouteBendContent = reusedRouteBendContent;
        ReusedConnectorLabelMoveContent = reusedConnectorLabelMoveContent;
        Diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    internal EditingSessionPipelineStatus Status { get; }

    internal EditingSessionPipelineArtifacts? Artifacts { get; }

    internal Canvas2DScene? Scene { get; }

    internal bool ReusedPanContent { get; }

    internal bool ReusedMoveContent { get; }

    internal bool ReusedSelectionContent { get; }

    internal bool ReusedPlacementContent { get; }
    internal bool ReusedSpatialResizeContent { get; }
    internal bool ReusedNodeLabelMoveContent { get; }
    internal bool ReusedRouteBendContent { get; }
    internal bool ReusedConnectorLabelMoveContent { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static EditingSessionPipelineResult Success(
        EditingSessionPipelineArtifacts artifacts,
        Canvas2DScene scene,
        IEnumerable<Diagnostic>? diagnostics = null,
        bool reusedPanContent = false,
        bool reusedMoveContent = false,
        bool reusedSelectionContent = false,
        bool reusedPlacementContent = false,
        bool reusedSpatialResizeContent = false,
        bool reusedNodeLabelMoveContent = false,
        bool reusedRouteBendContent = false,
        bool reusedConnectorLabelMoveContent = false)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(scene);
        return new(
            EditingSessionPipelineStatus.Succeeded,
            artifacts,
            scene,
            diagnostics,
            reusedPanContent,
            reusedMoveContent,
            reusedSelectionContent,
            reusedPlacementContent, reusedSpatialResizeContent, reusedNodeLabelMoveContent, reusedRouteBendContent,
            reusedConnectorLabelMoveContent);
    }

    internal static EditingSessionPipelineResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(EditingSessionPipelineStatus.Failed, null, null, diagnostics);

    internal static EditingSessionPipelineResult Cancelled(IEnumerable<Diagnostic>? diagnostics = null) =>
        new(EditingSessionPipelineStatus.Cancelled, null, null, diagnostics);

}
