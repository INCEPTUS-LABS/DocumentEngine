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
        IEnumerable<Diagnostic>? diagnostics)
    {
        Status = status;
        Artifacts = artifacts;
        Scene = scene;
        Diagnostics = EditingSessionDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
    }

    internal EditingSessionPipelineStatus Status { get; }

    internal EditingSessionPipelineArtifacts? Artifacts { get; }

    internal Canvas2DScene? Scene { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static EditingSessionPipelineResult Success(
        EditingSessionPipelineArtifacts artifacts,
        Canvas2DScene scene,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(scene);
        return new(
            EditingSessionPipelineStatus.Succeeded,
            artifacts,
            scene,
            diagnostics);
    }

    internal static EditingSessionPipelineResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(EditingSessionPipelineStatus.Failed, null, null, diagnostics);

    internal static EditingSessionPipelineResult Cancelled(IEnumerable<Diagnostic>? diagnostics = null) =>
        new(EditingSessionPipelineStatus.Cancelled, null, null, diagnostics);

}
