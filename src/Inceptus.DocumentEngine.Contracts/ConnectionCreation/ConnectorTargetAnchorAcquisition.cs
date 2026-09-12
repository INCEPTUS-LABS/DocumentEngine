using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.ConnectionCreation;

/// <summary>
/// Resolves an existing or proposed Target anchor on exactly one pointer-selected edge.
/// It never searches another edge and never performs persistent mutation.
/// </summary>
public static class ConnectorTargetAnchorAcquisition
{
    public static TargetAnchorAcquisitionResult Acquire(
        TargetAnchorAcquisitionRequest request,
        Func<ConnectorAnchorId> proposedAnchorIdFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proposedAnchorIdFactory);
        if (!request.Document.VisualModel.TryGetVisualState(
                request.TargetVisualStateId,
                out var visualState) ||
            visualState is null ||
            visualState.SemanticElementId != request.TargetSemanticElementId ||
            !request.Document.SemanticModel.TryGetElement(
                request.TargetSemanticElementId,
                out var semanticElement) ||
            semanticElement is null)
        {
            return TargetAnchorAcquisitionResult.Rejected(
                TargetAnchorAcquisitionRejectionReason.InvalidTarget);
        }

        var bounds = request.TargetBounds;
        if (!bounds.Contains(request.DocumentPoint))
        {
            return TargetAnchorAcquisitionResult.Rejected(
                TargetAnchorAcquisitionRejectionReason.InvalidGeometry);
        }

        ConnectorAnchorSide side;
        try
        {
            side = ConnectorTargetEdgeResolver.Resolve(bounds, request.DocumentPoint);
        }
        catch (ArgumentOutOfRangeException)
        {
            return TargetAnchorAcquisitionResult.Rejected(
                TargetAnchorAcquisitionRejectionReason.InvalidGeometry);
        }

        ResolvedConnectorAnchor[] sideAnchors;
        try
        {
            sideAnchors = ElementConnectorAnchorResolver.Resolve(visualState, request.Policy)
                .Where(anchor => anchor.Side == side)
                .OrderBy(static anchor => anchor.Order)
                .ThenBy(static anchor => anchor.Id.Value, StringComparer.Ordinal)
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            return TargetAnchorAcquisitionResult.Rejected(
                TargetAnchorAcquisitionRejectionReason.PolicyRejected);
        }

        var edgeParameter = ConnectorAnchorGeometryResolver.ResolveEdgeParameter(
            bounds,
            side,
            request.DocumentPoint);
        var existing = sideAnchors
            .Where(anchor =>
                anchor.Allows(ConnectorAnchorRole.Target) &&
                !ConnectorAnchorOccupancy.IsOccupied(
                    request.Document.VisualModel,
                    anchor.Id))
            .Select(anchor => new
            {
                Anchor = anchor,
                Distance = Math.Abs(
                    ConnectorAnchorGeometryResolver.ResolveNormalizedPosition(
                        anchor.Order,
                        sideAnchors.Length) - edgeParameter),
            })
            .OrderBy(static candidate => candidate.Distance)
            .ThenBy(static candidate => candidate.Anchor.Order)
            .ThenBy(static candidate => candidate.Anchor.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (existing is not null)
        {
            return TargetAnchorAcquisitionResult.Existing(
                request.TargetSemanticElementId,
                request.TargetVisualStateId,
                side,
                existing.Anchor.Id,
                ConnectorAnchorGeometryResolver.ResolvePoint(
                    bounds,
                    side,
                    existing.Anchor.Order,
                    sideAnchors.Length));
        }

        if (!ElementConnectorAnchorPolicyEvaluator.CanAdd(
                request.Policy,
                visualState,
                side,
                ConnectorAnchorRole.Target))
        {
            return TargetAnchorAcquisitionResult.Rejected(
                TargetAnchorAcquisitionRejectionReason.PolicyRejected);
        }

        var insertionIndex = ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
            visualState.ConnectorAnchors,
            side,
            edgeParameter);
        var proposedAnchorId = proposedAnchorIdFactory();
        ArgumentNullException.ThrowIfNull(proposedAnchorId);
        return TargetAnchorAcquisitionResult.Proposed(
            request.TargetSemanticElementId,
            request.TargetVisualStateId,
            side,
            proposedAnchorId,
            ConnectorAnchorGeometryResolver.ResolvePoint(
                bounds,
                side,
                insertionIndex,
                sideAnchors.Length + 1),
            insertionIndex);
    }
}
