using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Commands;

/// <summary>
/// Keeps required Visual State compatibility bounds synchronized with structural
/// owner-boundary placement without invoking a full layout algorithm.
/// </summary>
internal static class BoundaryAttachmentGeometrySynchronizer
{
    internal static ImmutableArray<Diagnostic> SynchronizeDependents(
        DocumentSnapshot document,
        IDictionary<VisualStateId, VisualStateSnapshot> replacements)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacements);

        if (replacements.Count == 0)
        {
            return [];
        }

        var diagnostics = new List<Diagnostic>();
        var effectiveVisuals = document.VisualModel.VisualStates.ToDictionary(
            static visualState => visualState.Id,
            visualState => replacements.TryGetValue(visualState.Id, out var replacement)
                ? replacement
                : visualState);
        var dependentsByOwner = document.SemanticModel.Elements
            .Where(static element => element.AttachedToElementId is not null)
            .GroupBy(static element => element.AttachedToElementId!)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(
                    static element => element.Id.Value,
                    StringComparer.Ordinal).ToArray());

        var pendingOwners = new Queue<SemanticElementId>(replacements.Values
            .Select(static visualState => visualState.SemanticElementId)
            .Distinct()
            .OrderBy(static id => id.Value, StringComparer.Ordinal));
        var visitedOwners = new HashSet<SemanticElementId>();

        while (pendingOwners.TryDequeue(out var ownerSemanticElementId))
        {
            if (!visitedOwners.Add(ownerSemanticElementId) ||
                !dependentsByOwner.TryGetValue(ownerSemanticElementId, out var dependents))
            {
                continue;
            }

            foreach (var dependentElement in dependents)
            {
                var dependentVisuals = effectiveVisuals.Values
                    .Where(visualState =>
                        visualState.SemanticElementId == dependentElement.Id)
                    .OrderBy(static visualState => visualState.Id.Value, StringComparer.Ordinal)
                    .ToArray();
                foreach (var dependentVisual in dependentVisuals)
                {
                    if (dependentVisual.BoundaryAttachment is null)
                    {
                        diagnostics.Add(Error(
                            CommandExecutionDiagnosticCodes
                                .VisualStateDoesNotSupportBoundaryAttachment,
                            $"Attached semantic element '{dependentElement.Id}' has visual state '{dependentVisual.Id}' without boundary placement.",
                            dependentVisual.Id.Value));
                        continue;
                    }

                    if (!TryResolveOwnerVisual(
                            effectiveVisuals.Values,
                            ownerSemanticElementId,
                            dependentVisual.Id,
                            out var ownerVisual,
                            out var ownerDiagnostic))
                    {
                        diagnostics.Add(ownerDiagnostic!);
                        continue;
                    }

                    if (!TryResolveAttachedBounds(
                            ownerVisual!,
                            dependentVisual,
                            dependentVisual.BoundaryAttachment,
                            out var derivedBounds))
                    {
                        diagnostics.Add(Error(
                            CommandExecutionDiagnosticCodes.VisualStateGeometryInvalid,
                            $"Synchronizing visual state '{dependentVisual.Id}' to owner '{ownerSemanticElementId}' would produce non-renderable bounds.",
                            dependentVisual.Id.Value));
                        continue;
                    }

                    if (dependentVisual.Position != derivedBounds.TopLeft)
                    {
                        var replacement = WithBounds(dependentVisual, derivedBounds);
                        replacements[replacement.Id] = replacement;
                        effectiveVisuals[replacement.Id] = replacement;
                    }

                    pendingOwners.Enqueue(dependentElement.Id);
                }
            }
        }

        return [.. diagnostics];
    }

    internal static bool TryResolveOwnerVisual(
        DocumentSnapshot document,
        SemanticElementId ownerSemanticElementId,
        VisualStateId dependentVisualStateId,
        out VisualStateSnapshot? ownerVisual,
        out Diagnostic? diagnostic) =>
        TryResolveOwnerVisual(
            document.VisualModel.VisualStates,
            ownerSemanticElementId,
            dependentVisualStateId,
            out ownerVisual,
            out diagnostic);

    internal static bool TryResolveAttachedBounds(
        VisualStateSnapshot ownerVisual,
        VisualStateSnapshot attachedVisual,
        BoundaryAttachmentPlacement placement,
        out RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(ownerVisual);
        ArgumentNullException.ThrowIfNull(attachedVisual);
        ArgumentNullException.ThrowIfNull(placement);

        var ownerBounds = new RectD(
            ownerVisual.Position.X,
            ownerVisual.Position.Y,
            ownerVisual.Size.Width,
            ownerVisual.Size.Height);
        return TryResolveAttachedBounds(
            ownerBounds,
            attachedVisual,
            placement,
            out bounds);
    }

    internal static bool TryResolveAttachedBounds(
        RectD ownerBounds,
        VisualStateSnapshot attachedVisual,
        BoundaryAttachmentPlacement placement,
        out RectD bounds)
    {
        ArgumentNullException.ThrowIfNull(attachedVisual);
        ArgumentNullException.ThrowIfNull(placement);

        try
        {
            if (!VisualCommandGeometry.HasRenderableBounds(
                    ownerBounds.TopLeft,
                    ownerBounds.Size))
            {
                bounds = default;
                return false;
            }

            bounds = placement.ResolveBounds(ownerBounds, attachedVisual.Size);
            return VisualCommandGeometry.HasRenderableBounds(bounds.TopLeft, bounds.Size);
        }
        catch (ArgumentOutOfRangeException)
        {
            bounds = default;
            return false;
        }
    }

    internal static RectD PersistentBounds(VisualStateSnapshot visualState)
    {
        ArgumentNullException.ThrowIfNull(visualState);
        return new RectD(
            visualState.Position.X,
            visualState.Position.Y,
            visualState.Size.Width,
            visualState.Size.Height);
    }

    internal static VisualStateSnapshot WithBounds(
        VisualStateSnapshot visualState,
        RectD bounds,
        BoundaryAttachmentPlacement? boundaryAttachment = null) =>
        new(
            visualState.Id,
            visualState.SemanticElementId,
            bounds.TopLeft,
            bounds.Size,
            visualState.PlacementMode,
            visualState.Route,
            visualState.Properties,
            visualState.ConnectorAnchors,
            visualState.SourceAnchorId,
            visualState.TargetAnchorId,
            boundaryAttachment ?? visualState.BoundaryAttachment);

    private static bool TryResolveOwnerVisual(
        IEnumerable<VisualStateSnapshot> visualStates,
        SemanticElementId ownerSemanticElementId,
        VisualStateId dependentVisualStateId,
        out VisualStateSnapshot? ownerVisual,
        out Diagnostic? diagnostic)
    {
        var owners = visualStates
            .Where(visualState => visualState.SemanticElementId == ownerSemanticElementId)
            .Take(2)
            .ToArray();
        if (owners.Length == 1)
        {
            ownerVisual = owners[0];
            diagnostic = null;
            return true;
        }

        ownerVisual = null;
        diagnostic = Error(
            CommandExecutionDiagnosticCodes.BoundaryAttachmentOwnerUnavailable,
            owners.Length == 0
                ? $"Boundary-attached visual state '{dependentVisualStateId}' has no visual owner for semantic element '{ownerSemanticElementId}'."
                : $"Boundary-attached visual state '{dependentVisualStateId}' has more than one visual owner for semantic element '{ownerSemanticElementId}'.",
            dependentVisualStateId.Value);
        return false;
    }

    private static Diagnostic Error(string code, string message, string sourceIdentity) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity);
}
