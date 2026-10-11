using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private async ValueTask<Canvas2DInteractionResult> ExecuteElementConnectionAsync(EditingSessionState state,
        Canvas2DSceneHitTestResult? hit, PointD pointer, bool activate, CancellationToken cancellationToken)
    {
        try { return await ExecuteElementConnectionCoreAsync(state, hit, pointer, activate, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(Canvas2DInteractionStatus.Cancelled, _session.CaptureState()); }
#pragma warning disable CA1031 // A failing extension must not escape the canonical interaction boundary.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(_session.CaptureState(), Canvas2DInteractionDiagnosticCodes.ConnectionCreationFactoryFailed,
            "The selected connection capability could not prepare the operation.", exception);
        }
#pragma warning restore CA1031
    }

    private async ValueTask<Canvas2DInteractionResult> ExecuteElementConnectionCoreAsync(EditingSessionState state,
        Canvas2DSceneHitTestResult? hit, PointD pointer, bool activate, CancellationToken cancellationToken)
    {
        var selected = ConnectionToolState.GetSelection(state.EditorState)!;
        if (!_connectionCreationCatalog.TryGetRegistration(selected, out var registration) ||
            registration.CommandFactory is not IElementConnectionCreationCommandFactory factory || _creationIdentityProvider is null ||
            !_session.TryCaptureDocumentSnapshot(out var document) || document.Revision != state.DocumentRevision)
            return new(Canvas2DInteractionStatus.Unchanged, state);
        var pending = ConnectionToolState.GetPendingSource(state.EditorState);
        if (pending is not null && (pending.DocumentId != document.DocumentId || pending.Revision != document.Revision ||
            pending.Context != state.PresentationContext || pending.CreationId != selected ||
            state.CurrentScene!.Items.All(item => item.Id != pending.Source.SceneId || item.Origin.VisualStateId != pending.Source.VisualId ||
                item.Origin.ProjectedObjectId != pending.Source.ProjectedId)))
            return await ApplyEditorStateAsync(state, ConnectionToolState.WithSelection(state.EditorState, null), null, cancellationToken).ConfigureAwait(false);

        var body = hit is null ? null : state.CurrentScene!.Items.FirstOrDefault(item => item.Id == hit.SceneObjectId);
        if (body is not null && body.Layer != Canvas2DSceneLayer.Content && body.Origin.SemanticElementId is { } semanticOwner)
            body = state.CurrentScene!.Items.FirstOrDefault(item => item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.SemanticElementId == semanticOwner && item.Bounds.Contains(pointer)) ?? body;
        if (body is null)
        {
            var containers = state.ProjectedGraph!.Nodes.Where(node => Canvas2DNodePresentationMetadata.IsBoundaryContainer(node.ProjectedProperties))
                .Select(node => state.CurrentScene!.Items.FirstOrDefault(item => item.Layer == Canvas2DSceneLayer.Content &&
                    item.Origin.ProjectedObjectId == node.Id && item.IsVisible && item.Bounds.Contains(pointer)))
                .Where(item => item is not null).ToArray();
            if (containers.Length == 1) body = containers[0];
        }
        if (body?.Layer == Canvas2DSceneLayer.Label && body.Origin.VisualStateId is { } labelOwner)
            body = state.CurrentScene!.Items.FirstOrDefault(item => item.Layer == Canvas2DSceneLayer.Content &&
                item.Origin.VisualStateId == labelOwner && item.Bounds.Contains(pointer));
        if (body is not null && (body.Layer != Canvas2DSceneLayer.Content ||
            !IsEditableVisualPresentation(body) && !state.ProjectedGraph!.Nodes.Any(node => node.Id == body.Origin.ProjectedObjectId && node.RegionEnvelope is not null) ||
            body.Origin.SemanticElementId is null || body.Origin.ProjectedObjectId is null ||
            !document.SemanticModel.TryGetElement(body.Origin.SemanticElementId, out _))) body = null;
        ElementConnectionEndpoint? endpoint = null;
        if (body is not null)
        {
            document.SemanticModel.TryGetScope(body.Origin.SemanticElementId!, out var scope);
            endpoint = new(body.Origin.SemanticElementId!, body.Origin.VisualStateId!, body.Origin.ProjectedObjectId!, body.Id, scope?.Id, body.Bounds);
        }
        var request = pending is null || endpoint is null ? null : new ElementConnectionCreationRequest(document,
            state.PresentationContext, state.PresentationPlan, pending.Source, endpoint, _creationIdentityProvider);
        ImmutableArray<Diagnostic> diagnostics = endpoint is null ? [] : pending is null
            ? factory.ValidateSource(new(document, state.PresentationContext, state.PresentationPlan, endpoint.SemanticId, endpoint.VisualId))
            : factory.ValidateEndpoints(request!);
        if (!activate || endpoint is null || !diagnostics.IsEmpty)
        {
            var editor = ConnectionToolState.WithPendingSource(state.EditorState, pending is null ? null : pending with { CurrentPoint = pointer });
            editor = Copy(editor, editor.Selection, hoveredObjectId: diagnostics.IsEmpty ? body?.Id : null, activeGesture: null);
            var result = editor.Equals(state.EditorState) ? new Canvas2DInteractionResult(Canvas2DInteractionStatus.Unchanged, state,
                    cssCursor: diagnostics.IsEmpty ? "crosshair" : "not-allowed")
                : await ApplyEditorStateAsync(state, editor, hit, cancellationToken, diagnostics.IsEmpty ? "crosshair" : "not-allowed").ConfigureAwait(false);
            return activate && !diagnostics.IsEmpty ? new(Canvas2DInteractionStatus.Failed, result.SessionState, hit, diagnostics: diagnostics) : result;
        }
        if (pending is null)
        {
            var preview = new ElementConnectionPreview(document.DocumentId, document.Revision, state.PresentationContext,
                selected, endpoint, pointer, registration.VisualDescriptor);
            return await ApplyEditorStateAsync(state, ConnectionToolState.WithPendingSource(state.EditorState, preview), hit,
                cancellationToken, "crosshair").ConfigureAwait(false);
        }

        var candidates = await factory.CreateCandidatesAsync(request!, cancellationToken).ConfigureAwait(false);
        var plan = await _session.ChooseElementConnectionAsync(document, candidates, cancellationToken).ConfigureAwait(false);
        if (plan is null) return new(Canvas2DInteractionStatus.Failed, state, hit, diagnostics:
            [new("CANVAS2D_ELEMENT_CONNECTION_NO_ROUTE", DiagnosticSeverity.Error, "No feasible compatible boundary-point pair could be prepared.")]);
        var completed = await _session.CompleteElementConnectionAsync(state, pending.Source, endpoint, plan.Command, cancellationToken).ConfigureAwait(false);
        if (!completed.IsCommitted) return new(Canvas2DInteractionStatus.Failed, _session.CaptureState(), hit,
            diagnostics: completed.Diagnostics, persistentOperation: completed);
        await _session.WaitForIdleAsync(CancellationToken.None).ConfigureAwait(false);
        var rebuilt = _session.CaptureState();
        var updated = ConnectionToolState.WithSelection(Copy(rebuilt.EditorState, [plan.CreatedConnectorVisualStateId],
            hoveredObjectId: null, activeGesture: null), null);
        await ApplyEditorStateAsync(rebuilt, updated, null, CancellationToken.None).ConfigureAwait(false);
        return new(Canvas2DInteractionStatus.Committed, _session.CaptureState(), persistentOperation: completed);
    }
}
