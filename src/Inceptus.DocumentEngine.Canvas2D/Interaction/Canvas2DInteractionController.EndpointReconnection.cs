using System.Collections.Immutable;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.HitTesting;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

public sealed partial class Canvas2DInteractionController
{
    private async ValueTask<Canvas2DInteractionResult?>
        TryStartConnectorEndpointReconnectionUnderGateAsync(
            EditingSessionState observed,
            Canvas2DPointerInput input,
            Canvas2DSceneHitTestResult hit,
            Canvas2DSceneItem hitItem,
            CancellationToken cancellationToken)
    {
        if (_endpointReconnectionCatalog.Registrations.IsEmpty ||
            !TryResolveConnectorEndpointHandle(
                observed.CurrentScene!,
                hitItem,
                out var connectorTarget,
                out var connectorVisualStateId,
                out var endpointKind) ||
            connectorTarget.Origin.SemanticElementId is not { } relationshipId)
        {
            return null;
        }

        if (!_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !TryResolveCurrentEndpoint(
                document,
                relationshipId,
                connectorVisualStateId,
                endpointKind,
                out var currentSemanticEndpointId,
                out var currentAnchorId))
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Stale,
                _session.CaptureState(),
                hit,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The connector endpoint no longer belongs to the current persistent Document.")]);
        }

        var startRequest = new ConnectorEndpointReconnectionStartRequest(
            document,
            observed.DocumentRevision,
            relationshipId,
            connectorVisualStateId,
            endpointKind,
            currentSemanticEndpointId,
            currentAnchorId);
        ImmutableArray<ConnectorEndpointReconnectionRegistration> matches;
        try
        {
            matches = _endpointReconnectionCatalog.GetMatchingRegistrations(startRequest);
        }
#pragma warning disable CA1031 // Plugin matching failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            return FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed,
                "A registered connector-endpoint reconnection factory failed while matching the selected endpoint.",
                exception);
        }
#pragma warning restore CA1031

        if (matches.IsEmpty)
        {
            return null;
        }

        if (matches.Length != 1)
        {
            var identities = string.Join(
                ", ",
                matches.Select(static match => match.ReconnectionId.Value));
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.AmbiguousEndpointReconnection,
                $"The selected connector endpoint matches multiple reconnection capabilities: {identities}.",
                observed,
                hit);
        }

        var displayedRoute = Canvas2DConnectorPathMetadata.Resolve(connectorTarget)
            .Select(connectorTarget.Transform.TransformPoint)
            .ToImmutableArray();
        if (displayedRoute.Length < 2)
        {
            return GestureUnavailableResult(
                Canvas2DInteractionDiagnosticCodes.InvalidGestureTarget,
                "The selected connector endpoint has no usable displayed connector route.",
                observed,
                hit);
        }

        var registration = matches[0];
        var originalPoint = endpointKind == ConnectorEndpointKind.Source
            ? displayedRoute[0]
            : displayedRoute[^1];
        var gestureId = FormattableString.Invariant(
            $"connector-endpoint-reconnection:{observed.DocumentRevision.Value}:{input.PointerId}:{connectorVisualStateId.Value}:{endpointKind}:{registration.ReconnectionId.Value}");
        var gestureSnapshot = new EditorGestureSnapshot(
            gestureId,
            Canvas2DConnectorEndpointReconnectionGestureMetadata.Kind,
            originalPoint,
            originalPoint,
            Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
                relationshipId,
                connectorVisualStateId,
                endpointKind,
                currentSemanticEndpointId,
                currentAnchorId,
                registration.ReconnectionId,
                connectorTarget.Id));
        var updatedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            gestureSnapshot);
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hit,
            cancellationToken,
            "crosshair").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = new PersistentGestureState(
                PersistentGestureKind.ConnectorEndpointReconnection,
                Canvas2DResizeDirection.SouthEast,
                input.PointerId,
                gestureId,
                observed.DocumentId,
                observed.DocumentRevision,
                hitItem.Id,
                NodeLabelOwnerSceneObjectId: null,
                connectorVisualStateId,
                hitItem.Bounds,
                connectorTarget.Bounds,
                displayedRoute,
                BendIndex: -1,
                LabelProjectedObjectId: null,
                NodeLabelOperation: Canvas2DNodeLabelGestureOperation.Move,
                MoveTargets: [],
                originalPoint,
                originalPoint,
                currentScene,
                update.SessionState.Generation,
                update.SessionState.EditorState,
                EndpointReconnection: new ConnectorEndpointReconnectionGestureState(
                    registration,
                    relationshipId,
                    connectorVisualStateId,
                    connectorTarget.Id,
                    endpointKind,
                    currentSemanticEndpointId,
                    currentAnchorId,
                    CandidateSemanticElementId: null,
                    CandidateVisualStateId: null,
                    CandidateAnchorId: null),
                SpatialRegion: connectorTarget.SpatialRegion);
            return update;
        }

        return await CleanupFailedGestureUpdateUnderGateAsync(
            gestureId,
            update).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult>
        ExecuteConnectorEndpointReconnectionMoveUnderGateAsync(
            EditingSessionState observed,
            PersistentGestureState gesture,
            PointD documentPoint,
            CancellationToken cancellationToken)
    {
        var reconnection = gesture.EndpointReconnection!;
        Canvas2DSceneHitTestResult? hit;
        try
        {
            hit = _hitTestService.HitTest(observed.CurrentScene!, documentPoint);
        }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failed = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                "The current Canvas2DScene could not be hit tested for an endpoint-reconnection target.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failed).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        ConnectorEndpointReconnectionTarget? target =
            TryResolveEndpointReconnectionTarget(observed, reconnection, hit, out var resolvedTarget)
                ? resolvedTarget
                : null;
        var effectivePoint = DocumentGeometryBoundary.Clamp(
            target?.DocumentPoint ?? documentPoint);
        if (effectivePoint == gesture.CurrentDocumentPoint &&
            reconnection.CandidateSemanticElementId == target?.SemanticElementId &&
            reconnection.CandidateVisualStateId == target?.VisualStateId &&
            reconnection.CandidateAnchorId == target?.AnchorId)
        {
            return new Canvas2DInteractionResult(
                Canvas2DInteractionStatus.Unchanged,
                observed,
                hit,
                cssCursor: "crosshair");
        }

        var active = observed.EditorState.ActiveGesture!;
        var properties = Canvas2DConnectorEndpointReconnectionGestureMetadata.CreateProperties(
            reconnection.RelationshipId,
            reconnection.ConnectorVisualStateId,
            reconnection.EndpointKind,
            reconnection.OriginalSemanticEndpointId,
            reconnection.OriginalAnchorId,
            reconnection.Registration.ReconnectionId,
            reconnection.ConnectorSceneObjectId,
            target?.SemanticElementId,
            target?.VisualStateId,
            target?.AnchorId);
        var updatedGesture = new EditorGestureSnapshot(
            active.Id,
            active.Kind,
            active.Origin,
            effectivePoint,
            properties);
        var updatedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            updatedGesture);
        var update = await ApplyEditorStateAsync(
            observed,
            updatedEditorState,
            hit,
            cancellationToken,
            "crosshair").ConfigureAwait(false);
        if (update.Status == Canvas2DInteractionStatus.Updated &&
            update.SessionState.CurrentScene is { } currentScene)
        {
            _persistentGesture = gesture with
            {
                CurrentDocumentPoint = effectivePoint,
                CurrentScene = currentScene,
                CurrentGeneration = update.SessionState.Generation,
                CurrentEditorState = update.SessionState.EditorState,
                EndpointReconnection = reconnection with
                {
                    CandidateSemanticElementId = target?.SemanticElementId,
                    CandidateVisualStateId = target?.VisualStateId,
                    CandidateAnchorId = target?.AnchorId,
                },
            };
            return update;
        }

        if (update.Status == Canvas2DInteractionStatus.Stale)
        {
            return await CancelStaleGestureUnderGateAsync(
                gesture,
                "The endpoint-reconnection preview could not be installed because its source Scene became stale.")
                .ConfigureAwait(false);
        }

        _persistentGesture = null;
        return await CleanupFailedGestureUpdateUnderGateAsync(
            gesture.GestureId,
            update).ConfigureAwait(false);
    }

    private async ValueTask<Canvas2DInteractionResult>
        CompleteConnectorEndpointReconnectionUnderGateAsync(
            EditingSessionState observed,
            PersistentGestureState gesture,
            Canvas2DPointerInput input,
            CancellationToken cancellationToken)
    {
        var reconnection = gesture.EndpointReconnection!;
        _suppressNextActivation = true;
        var documentPoint = default(PointD);
        Canvas2DInteractionResult? conversionFailure = null;
        if (!input.IsPrimary || input.Button != 0 ||
            !TryConvertPoint(
                observed,
                input.CssPoint,
                out documentPoint,
                out conversionFailure))
        {
            if (conversionFailure is not null)
            {
                _persistentGesture = null;
                return await CleanupFailedGestureUpdateUnderGateAsync(
                    gesture.GestureId,
                    conversionFailure).ConfigureAwait(false);
            }

            return await CancelConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                input.CssPoint).ConfigureAwait(false);
        }

        if (!DocumentGeometryBoundary.Contains(documentPoint))
        {
            return await CancelConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                input.CssPoint).ConfigureAwait(false);
        }

        Canvas2DSceneHitTestResult? hit;
        try
        {
            hit = _hitTestService.HitTest(observed.CurrentScene!, documentPoint);
        }
#pragma warning disable CA1031 // Malformed transient geometry becomes a stable interaction diagnostic.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.HitTestFailed,
                "The current Canvas2DScene could not be hit tested for endpoint-reconnection completion.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failure).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (!TryResolveEndpointReconnectionTarget(
                observed,
                reconnection,
                hit,
                out var target))
        {
            return await CancelConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                input.CssPoint).ConfigureAwait(false);
        }

        if (!_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !_endpointReconnectionCatalog.TryGetRegistration(
                reconnection.Registration.ReconnectionId,
                out var currentRegistration) ||
            !ReferenceEquals(currentRegistration, reconnection.Registration) ||
            !HasExpectedCurrentEndpoint(document, reconnection) ||
            !IsAvailableReconnectionTarget(document, reconnection, target))
        {
            return await CancelStaleGestureUnderGateAsync(
                gesture,
                "The connector endpoint or reconnection target no longer belongs to the current persistent Document.")
                .ConfigureAwait(false);
        }

        if (target.AnchorId == reconnection.OriginalAnchorId &&
            target.SemanticElementId == reconnection.OriginalSemanticEndpointId)
        {
            return await CancelConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                input.CssPoint).ConfigureAwait(false);
        }

        var startRequest = new ConnectorEndpointReconnectionStartRequest(
            document,
            observed.DocumentRevision,
            reconnection.RelationshipId,
            reconnection.ConnectorVisualStateId,
            reconnection.EndpointKind,
            reconnection.OriginalSemanticEndpointId,
            reconnection.OriginalAnchorId);
        bool stillMatches;
        try
        {
            stillMatches = currentRegistration.CommandFactory.CanStart(startRequest);
        }
#pragma warning disable CA1031 // Plugin matching failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            _persistentGesture = null;
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed,
                "The selected endpoint-reconnection factory failed during completion revalidation.",
                exception);
            return await CleanupFailedGestureUpdateUnderGateAsync(
                gesture.GestureId,
                failure).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (!stillMatches)
        {
            _persistentGesture = null;
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed,
                    DiagnosticSeverity.Error,
                    "The selected endpoint-reconnection capability no longer matches the connector endpoint.")])
                .ConfigureAwait(false);
        }

        var clearedEditorState = Copy(
            observed.EditorState,
            observed.EditorState.Selection,
            hoveredObjectId: null,
            activeGesture: null);
        _persistentGesture = null;
        ConnectorEndpointReconnectionPlanResult? planned;
        try
        {
            planned = currentRegistration.CommandFactory.CreatePlan(
                new ConnectorEndpointReconnectionRequest(
                    document,
                    observed.DocumentRevision,
                    reconnection.RelationshipId,
                    reconnection.ConnectorVisualStateId,
                    reconnection.EndpointKind,
                    reconnection.OriginalSemanticEndpointId,
                    reconnection.OriginalAnchorId,
                    target.SemanticElementId,
                    target.VisualStateId,
                    target.AnchorId));
        }
#pragma warning disable CA1031 // Plugin planning failures become stable interaction diagnostics.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            var failure = FailureResult(
                observed,
                Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed,
                "The selected endpoint-reconnection factory could not create a Command plan.",
                exception);
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                failure.Diagnostics).ConfigureAwait(false);
        }
#pragma warning restore CA1031

        if (planned is null)
        {
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                [Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.EndpointReconnectionFactoryFailed,
                    DiagnosticSeverity.Error,
                    "The selected endpoint-reconnection factory returned no Command plan result.")])
                .ConfigureAwait(false);
        }

        if (!planned.Succeeded || planned.Plan is not { } plan)
        {
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                planned.Diagnostics).ConfigureAwait(false);
        }

        if (plan.RelationshipId != reconnection.RelationshipId ||
            plan.ConnectorVisualStateId != reconnection.ConnectorVisualStateId ||
            plan.Command.TargetDocumentId != observed.DocumentId ||
            plan.Command.ExpectedRevision != observed.DocumentRevision)
        {
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                planned.Diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The endpoint-reconnection plan does not target the captured connector and Document revision.")))
                .ConfigureAwait(false);
        }

        if (!_session.TryCaptureDocumentSnapshot(out var executionDocument) ||
            executionDocument.DocumentId != observed.DocumentId ||
            executionDocument.Revision != observed.DocumentRevision ||
            !HasExpectedCurrentEndpoint(executionDocument, reconnection) ||
            !IsAvailableReconnectionTarget(executionDocument, reconnection, target))
        {
            return await EndFailedConnectorEndpointReconnectionUnderGateAsync(
                gesture,
                planned.Diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.StaleGesture,
                    DiagnosticSeverity.Error,
                    "The connector endpoint or candidate anchor changed before reconnection execution.")))
                .ConfigureAwait(false);
        }

        var commandResult = await _session.CompletePersistentGestureAsync(
            observed.CurrentScene!,
            observed.Generation,
            observed.EditorState,
            clearedEditorState,
            plan.Command,
            cancellationToken).ConfigureAwait(false);
        if (!commandResult.IsCommitted)
        {
            var cleanup = await _session.ClearPersistentGestureFromInteractionAsync(
                gesture.GestureId,
                CancellationToken.None).ConfigureAwait(false);
            var diagnostics = planned.Diagnostics
                .AddRange(commandResult.Diagnostics)
                .AddRange(cleanup.Diagnostics);
            var status = commandResult.Status == HistoryOperationStatus.Cancelled
                ? Canvas2DInteractionStatus.Cancelled
                : diagnostics.Any(diagnostic => StringComparer.Ordinal.Equals(
                    diagnostic.Code,
                    Canvas2DInteractionDiagnosticCodes.StaleGesture))
                    ? Canvas2DInteractionStatus.Stale
                    : Canvas2DInteractionStatus.Failed;
            if (status == Canvas2DInteractionStatus.Failed &&
                !diagnostics.Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error))
            {
                diagnostics = diagnostics.Add(Diagnostic(
                    Canvas2DInteractionDiagnosticCodes.CommandRejected,
                    DiagnosticSeverity.Error,
                    "The endpoint-reconnection Command was rejected without changing the Document."));
            }

            return new Canvas2DInteractionResult(
                status,
                cleanup.State,
                diagnostics: diagnostics,
                persistentOperation: commandResult);
        }

        await _session.WaitForIdleAsync(CancellationToken.None).ConfigureAwait(false);
        var rebuilt = _session.CaptureState();
        var completionDiagnostics = planned.Diagnostics.AddRange(commandResult.Diagnostics);
        var connectorIsInScene = rebuilt.CurrentScene?.Items.Any(item =>
            item.Origin.VisualStateId == reconnection.ConnectorVisualStateId) == true;
        var connectorIsSelected = rebuilt.EditorState.Selection.Contains(
            reconnection.ConnectorVisualStateId);
        if (!rebuilt.IsGraphicalInteractionEnabled ||
            rebuilt.DocumentId != observed.DocumentId ||
            rebuilt.DocumentRevision < commandResult.CommittedRevision ||
            rebuilt.CurrentScene is null ||
            !connectorIsInScene ||
            !connectorIsSelected ||
            rebuilt.EditorState.ActiveGesture is not null)
        {
            completionDiagnostics = completionDiagnostics.Add(Diagnostic(
                Canvas2DInteractionDiagnosticCodes.EditorStateUpdateFailed,
                DiagnosticSeverity.Error,
                "The reconnected connector outcome is inconsistent with the authoritative rebuilt Scene and transient selection."));
        }

        return new Canvas2DInteractionResult(
            Canvas2DInteractionStatus.Committed,
            rebuilt,
            diagnostics: completionDiagnostics,
            persistentOperation: commandResult,
            cssCursor: "pointer");
    }

    private bool TryResolveEndpointReconnectionTarget(
        EditingSessionState observed,
        ConnectorEndpointReconnectionGestureState reconnection,
        Canvas2DSceneHitTestResult? hitResult,
        out ConnectorEndpointReconnectionTarget target)
    {
        target = null!;
        if (hitResult is null || observed.CurrentScene is null)
        {
            return false;
        }

        var hitItem = observed.CurrentScene.Items.SingleOrDefault(
            item => item.Id == hitResult.SceneObjectId);
        var requiredRole = RequiredRole(reconnection.EndpointKind);
        if (hitItem is null ||
            !IsEditableVisualPresentation(hitItem) ||
            !TryResolveConnectorAnchorHandle(
                observed.CurrentScene,
                observed.EditorState,
                hitItem,
                out var ownerTarget,
                out var anchorId,
                out _,
                out var anchorKind,
                out _,
                out var role,
                out _,
                requireSelectedOwner: false) ||
            anchorKind != ResolvedConnectorAnchorKind.Dynamic ||
            role != requiredRole ||
            ownerTarget.Origin.SemanticElementId is not { } semanticElementId ||
            ownerTarget.Origin.VisualStateId is not { } visualStateId ||
            !IsEditableVisualPresentation(ownerTarget) ||
            !_session.TryCaptureDocumentSnapshot(out var document) ||
            document.DocumentId != observed.DocumentId ||
            document.Revision != observed.DocumentRevision ||
            !TryResolvePersistentAnchor(
                document,
                semanticElementId,
                visualStateId,
                anchorId,
                requiredRole) ||
            ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
                document.VisualModel,
                anchorId,
                reconnection.ConnectorVisualStateId,
                reconnection.EndpointKind))
        {
            return false;
        }

        target = new ConnectorEndpointReconnectionTarget(
            semanticElementId,
            visualStateId,
            anchorId,
            Center(hitItem.Bounds));
        return true;
    }

    private static bool TryResolveConnectorEndpointHandle(
        Canvas2DScene scene,
        Canvas2DSceneItem hitItem,
        out Canvas2DSceneItem connectorTarget,
        out VisualStateId connectorVisualStateId,
        out ConnectorEndpointKind endpointKind)
    {
        connectorTarget = null!;
        connectorVisualStateId = null!;
        endpointKind = default;
        if (!IsConnectorEndpointHandle(hitItem) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorEndpointMetadata.HandleRole,
                out var endpointRole) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorEndpointMetadata.TargetSceneObjectId,
                out var targetSceneObjectId) ||
            !TryGetTextMetadata(
                hitItem,
                Canvas2DConnectorEndpointMetadata.TargetVisualStateId,
                out var targetVisualStateId))
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(
                endpointRole,
                Canvas2DConnectorEndpointMetadata.StartEndpointRole))
        {
            endpointKind = ConnectorEndpointKind.Source;
        }
        else if (StringComparer.Ordinal.Equals(
                     endpointRole,
                     Canvas2DConnectorEndpointMetadata.EndEndpointRole))
        {
            endpointKind = ConnectorEndpointKind.Target;
        }
        else
        {
            return false;
        }
        connectorVisualStateId = new VisualStateId(targetVisualStateId);
        var targetId = new SceneObjectId(targetSceneObjectId);
        connectorTarget = scene.Items.SingleOrDefault(item => item.Id == targetId)!;
        return connectorTarget is not null &&
            connectorTarget.Layer == Canvas2DSceneLayer.Connector &&
            connectorTarget.Geometry.Kind == Canvas2DSceneGeometryKind.Path &&
            Canvas2DConnectorPathMetadata.Resolve(connectorTarget).Length >= 2 &&
            connectorTarget.Origin.VisualStateId == connectorVisualStateId &&
            hitItem.Origin.VisualStateId == connectorVisualStateId &&
            hitItem.Origin.SemanticElementId == connectorTarget.Origin.SemanticElementId &&
            hitItem.Origin.RelatedSceneObjectIds.Contains(targetId);
    }

    private static bool TryResolveCurrentEndpoint(
        DocumentSnapshot document,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId,
        ConnectorEndpointKind endpointKind,
        out SemanticElementId semanticEndpointId,
        out ConnectorAnchorId anchorId)
    {
        semanticEndpointId = null!;
        anchorId = null!;
        if (!document.VisualModel.TryGetVisualState(connectorVisualStateId, out var connector) ||
            connector is null ||
            connector.SemanticElementId != relationshipId)
        {
            return false;
        }

        var resolvedAnchorId = endpointKind == ConnectorEndpointKind.Source
            ? connector.SourceAnchorId
            : connector.TargetAnchorId;
        if (resolvedAnchorId is null)
        {
            return false;
        }

        anchorId = resolvedAnchorId;
        return TryResolvePersistentAnchorOwner(
            document,
            anchorId,
            RequiredRole(endpointKind),
            out semanticEndpointId,
            out _);
    }

    private static bool TryResolvePersistentAnchorOwner(
        DocumentSnapshot document,
        ConnectorAnchorId anchorId,
        ConnectorAnchorRole requiredRole,
        out SemanticElementId semanticElementId,
        out VisualStateId visualStateId)
    {
        semanticElementId = null!;
        visualStateId = null!;
        VisualStateSnapshot? owner = null;
        foreach (var visualState in document.VisualModel.VisualStates)
        {
            if (!visualState.ConnectorAnchors.Any(anchor =>
                    anchor.Id == anchorId && anchor.Role == requiredRole))
            {
                continue;
            }

            if (owner is not null)
            {
                return false;
            }

            owner = visualState;
        }

        if (owner is null)
        {
            return false;
        }

        semanticElementId = owner.SemanticElementId;
        visualStateId = owner.Id;
        return true;
    }

    private static bool HasExpectedCurrentEndpoint(
        DocumentSnapshot document,
        ConnectorEndpointReconnectionGestureState reconnection) =>
        TryResolveCurrentEndpoint(
            document,
            reconnection.RelationshipId,
            reconnection.ConnectorVisualStateId,
            reconnection.EndpointKind,
            out var semanticEndpointId,
            out var anchorId) &&
        semanticEndpointId == reconnection.OriginalSemanticEndpointId &&
        anchorId == reconnection.OriginalAnchorId;

    private static bool IsAvailableReconnectionTarget(
        DocumentSnapshot document,
        ConnectorEndpointReconnectionGestureState reconnection,
        ConnectorEndpointReconnectionTarget target) =>
        TryResolvePersistentAnchor(
            document,
            target.SemanticElementId,
            target.VisualStateId,
            target.AnchorId,
            RequiredRole(reconnection.EndpointKind)) &&
        !ConnectorAnchorOccupancy.IsOccupiedByOtherEndpoint(
            document.VisualModel,
            target.AnchorId,
            reconnection.ConnectorVisualStateId,
            reconnection.EndpointKind);

    private async ValueTask<Canvas2DInteractionResult>
        CancelConnectorEndpointReconnectionUnderGateAsync(
            PersistentGestureState gesture,
            PointD cssPoint)
    {
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        var presentation = ResolvePointerPresentation(cleared.State, cssPoint);
        return FromSessionResult(
            cleared,
            presentation.HitResult,
            presentation.CssCursor);
    }

    private async ValueTask<Canvas2DInteractionResult>
        EndFailedConnectorEndpointReconnectionUnderGateAsync(
            PersistentGestureState gesture,
            IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _persistentGesture = null;
        var cleared = await _session.ClearPersistentGestureFromInteractionAsync(
            gesture.GestureId,
            CancellationToken.None).ConfigureAwait(false);
        var combined = diagnostics.Concat(cleared.Diagnostics).ToImmutableArray();
        return new Canvas2DInteractionResult(
            combined.Any(static diagnostic => StringComparer.Ordinal.Equals(
                diagnostic.Code,
                Canvas2DInteractionDiagnosticCodes.StaleGesture))
                ? Canvas2DInteractionStatus.Stale
                : Canvas2DInteractionStatus.Failed,
            cleared.State,
            diagnostics: combined);
    }

    private static ConnectorAnchorRole RequiredRole(ConnectorEndpointKind endpointKind) =>
        endpointKind == ConnectorEndpointKind.Source
            ? ConnectorAnchorRole.Source
            : ConnectorAnchorRole.Target;
}
