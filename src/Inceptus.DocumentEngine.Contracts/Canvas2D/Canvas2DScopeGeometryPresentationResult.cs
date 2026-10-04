using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Pure expanded frame/capacity preparation; the host installs it only at commit.</summary>
public sealed class Canvas2DScopeGeometryPresentationResult
{
    private Canvas2DScopeGeometryPresentationResult(
        bool succeeded,
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        Canvas2DSpatialPresentationPlan? plan,
        IEnumerable<Diagnostic>? diagnostics,
        IEnumerable<SpatialScopeWidthSnapshot>? spatialWidths = null)
    {
        Succeeded = succeeded;
        Regions = RoutingStateCollection.Unique(regions, static region => region.Id.Value, nameof(regions));
        SpatialWidths = RoutingStateCollection.Unique(spatialWidths ?? [], static width => width.ProfileId.Value, nameof(spatialWidths));
        Plan = plan;
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        if (succeeded == Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new ArgumentException("Success cannot contain errors; failure requires an error.", nameof(diagnostics));
        if (!succeeded && (!Regions.IsEmpty || !SpatialWidths.IsEmpty || plan is not null))
            throw new ArgumentException("Failed preparation cannot publish geometry.");
        var active = Regions.Where(static region => region.IsActive).ToDictionary(static region => region.Id);
        if (plan is null ? active.Count != 0 : !plan.CoordinateMap.IsIdentity || plan.Regions.Length != active.Count ||
            plan.Regions.Any(region => !active.TryGetValue(region.Id, out var saved) ||
                saved.ProfileId != region.ModelProfileId || saved.ContainerSemanticElementId != region.ContainerSemanticElementId ||
                saved.ContentBounds != region.Bounds || saved.LocalToScopeTransform != region.LocalToSceneTransform))
            throw new ArgumentException("The expanded plan must describe exactly the active prepared regions.", nameof(plan));
    }

    public bool Succeeded { get; }
    public ImmutableArray<SpatialRegionGeometrySnapshot> Regions { get; }
    public ImmutableArray<SpatialScopeWidthSnapshot> SpatialWidths { get; }
    public Canvas2DSpatialPresentationPlan? Plan { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static Canvas2DScopeGeometryPresentationResult Success(
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        Canvas2DSpatialPresentationPlan? plan = null,
        IEnumerable<Diagnostic>? diagnostics = null) => new(true, regions, plan, diagnostics);

    public static Canvas2DScopeGeometryPresentationResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(false, [], null, diagnostics);

    public static Canvas2DScopeGeometryPresentationResult Success(
        IEnumerable<SpatialRegionGeometrySnapshot> regions,
        IEnumerable<SpatialScopeWidthSnapshot> spatialWidths,
        Canvas2DSpatialPresentationPlan? plan = null,
        IEnumerable<Diagnostic>? diagnostics = null) => new(true, regions, plan, diagnostics, spatialWidths);
}
