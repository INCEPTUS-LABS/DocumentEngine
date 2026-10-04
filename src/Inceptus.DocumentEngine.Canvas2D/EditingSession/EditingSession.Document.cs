using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    internal bool TryCaptureAnchorPolicyForInteraction(
        Canvas2DScene expectedScene,
        EditingSessionGeneration expectedGeneration,
        EditorStateSnapshot expectedEditorState,
        VisualStateId visualStateId,
        out VisualStateSnapshot? visualState,
        out ElementConnectorAnchorPolicy? policy,
        out VisualModelSnapshot? visualModel)
    {
        ArgumentNullException.ThrowIfNull(expectedScene);
        ArgumentNullException.ThrowIfNull(expectedEditorState);
        ArgumentNullException.ThrowIfNull(visualStateId);
        visualState = null;
        policy = null;
        visualModel = null;

        lock (_sync)
        {
            if (_closing || _closed ||
                _status != EditingSessionStatus.Ready ||
                !ReferenceEquals(_currentScene, expectedScene) ||
                _generation != expectedGeneration ||
                !ReferenceEquals(EditorState.CaptureSnapshot(), expectedEditorState))
            {
                return false;
            }

            var snapshot = AttachedDocument.CaptureSnapshot();
            if (snapshot.DocumentId != expectedScene.DocumentId ||
                snapshot.Revision != expectedScene.SourceRevision ||
                !snapshot.VisualModel.TryGetVisualState(visualStateId, out visualState) ||
                visualState is null ||
                !snapshot.SemanticModel.TryGetElement(
                    visualState.SemanticElementId,
                    out var element) ||
                element is null)
            {
                visualState = null;
                return false;
            }

            policy = ConnectorAnchorPolicyProvider.Resolve(element.TypeId);
            visualModel = snapshot.VisualModel;
            return true;
        }
    }

    /// <summary>
    /// Tries to capture the current immutable authoritative Document snapshot owned by this
    /// Editing Session. The snapshot is observation-only and is unavailable once closing begins.
    /// </summary>
    public bool TryCaptureDocumentSnapshot(
        [NotNullWhen(true)] out DocumentSnapshot? snapshot)
    {
        lock (_sync)
        {
            if (_closing || _closed || _document is null || _commandGate.CurrentCount == 0)
            {
                snapshot = null;
                return false;
            }

            snapshot = _document.CaptureSnapshot();
            return true;
        }
    }

    /// <summary>Waits for accepted mutations and captures one completely installed revision.</summary>
    public async ValueTask<DocumentSnapshot?> CaptureDocumentSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _closing || _closed ? null : _document?.CaptureSnapshot();
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }
}
