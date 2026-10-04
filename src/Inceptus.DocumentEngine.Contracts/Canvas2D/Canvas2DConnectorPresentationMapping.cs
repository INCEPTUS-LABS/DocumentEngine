using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Preserves the exact reversible relationship between displayed editable connector guidance
/// and its declared source space. Legacy Process-local guidance retains its translation contract;
/// saved scope-logical guidance uses the reversible compact spatial coordinate map.
/// </summary>
public sealed class Canvas2DConnectorPresentationMapping :
    IEquatable<Canvas2DConnectorPresentationMapping>
{
    private readonly Matrix2D _sceneToCanonicalGuidanceTransform;
    private readonly Matrix2D _canonicalGuidanceToSceneTransform;

    public Canvas2DConnectorPresentationMapping(
        IEnumerable<PointD> canonicalEditablePath,
        IEnumerable<PointD> displayedEditablePath,
        Matrix2D canonicalGuidanceToSceneTransform,
        Canvas2DSpatialRegionId? sourceRegionId,
        Canvas2DSpatialRegionId? targetRegionId)
    {
        ArgumentNullException.ThrowIfNull(canonicalEditablePath);
        ArgumentNullException.ThrowIfNull(displayedEditablePath);
        if (!IsTranslation(canonicalGuidanceToSceneTransform) ||
            !canonicalGuidanceToSceneTransform.TryInvert(
                out _sceneToCanonicalGuidanceTransform))
        {
            throw new ArgumentException(
                "Connector guidance requires an invertible translation-only transform.",
                nameof(canonicalGuidanceToSceneTransform));
        }

        CanonicalEditablePath = canonicalEditablePath.ToImmutableArray();
        DisplayedEditablePath = displayedEditablePath.ToImmutableArray();
        if (CanonicalEditablePath.Length < 2 ||
            DisplayedEditablePath.Length != CanonicalEditablePath.Length)
        {
            throw new ArgumentException(
                "Canonical and displayed editable connector paths require the same two-or-more point shape.",
                nameof(displayedEditablePath));
        }

        for (var index = 1; index < CanonicalEditablePath.Length - 1; index++)
        {
            if (canonicalGuidanceToSceneTransform.TransformPoint(
                    CanonicalEditablePath[index]) != DisplayedEditablePath[index])
            {
                throw new ArgumentException(
                    "Every internal displayed editable point must be the exact reversible mapping " +
                    "of its canonical guidance point.",
                    nameof(displayedEditablePath));
            }
        }

        _canonicalGuidanceToSceneTransform = canonicalGuidanceToSceneTransform;
        SourceSpace = Canvas2DConnectorPresentationSourceSpace.CanonicalProcess;
        CoordinateMap = Canvas2DSpatialCoordinateMap.Identity;
        SourceRegionId = sourceRegionId;
        TargetRegionId = targetRegionId;
    }

    /// <summary>
    /// Creates the exact editable-vertex mapping from saved expanded scope coordinates.
    /// Rendered automatic segment breakpoints are deliberately not part of these arrays.
    /// </summary>
    public Canvas2DConnectorPresentationMapping(
        IEnumerable<PointD> scopeLogicalEditablePath,
        IEnumerable<PointD> displayedEditablePath,
        Canvas2DSpatialCoordinateMap coordinateMap,
        Canvas2DSpatialRegionId? sourceRegionId,
        Canvas2DSpatialRegionId? targetRegionId)
    {
        ArgumentNullException.ThrowIfNull(scopeLogicalEditablePath);
        ArgumentNullException.ThrowIfNull(displayedEditablePath);
        ArgumentNullException.ThrowIfNull(coordinateMap);
        CanonicalEditablePath = scopeLogicalEditablePath.ToImmutableArray();
        DisplayedEditablePath = displayedEditablePath.ToImmutableArray();
        if (CanonicalEditablePath.Length < 2 ||
            DisplayedEditablePath.Length != CanonicalEditablePath.Length)
        {
            throw new ArgumentException(
                "Logical and displayed editable paths require the same two-or-more point shape.",
                nameof(displayedEditablePath));
        }
        for (var index = 0; index < CanonicalEditablePath.Length; index++)
        {
            if (coordinateMap.MapLogicalToScene(CanonicalEditablePath[index]) != DisplayedEditablePath[index])
            {
                throw new ArgumentException(
                    "Every displayed editable vertex must match the declared logical coordinate map.",
                    nameof(displayedEditablePath));
            }
        }

        _canonicalGuidanceToSceneTransform = Matrix2D.Identity;
        _sceneToCanonicalGuidanceTransform = Matrix2D.Identity;
        SourceSpace = Canvas2DConnectorPresentationSourceSpace.ScopeLogical;
        CoordinateMap = coordinateMap;
        SourceRegionId = sourceRegionId;
        TargetRegionId = targetRegionId;
    }

    public ImmutableArray<PointD> CanonicalEditablePath { get; }

    public ImmutableArray<PointD> DisplayedEditablePath { get; }

    /// <summary>Gets the complete translation, if one can represent this mapping.</summary>
    /// <exception cref="InvalidOperationException">The mapping is nonuniform.</exception>
    public Matrix2D CanonicalGuidanceToSceneTransform =>
        TryGetCanonicalGuidanceToSceneTransform(out var transform)
            ? transform
            : throw new InvalidOperationException("A nonuniform coordinate map cannot be represented by one matrix.");

    public Canvas2DConnectorPresentationSourceSpace SourceSpace { get; }

    public Canvas2DSpatialCoordinateMap CoordinateMap { get; }

    public Canvas2DSpatialRegionId? SourceRegionId { get; }

    public Canvas2DSpatialRegionId? TargetRegionId { get; }

    public bool IsSameRegion =>
        SourceRegionId is not null && SourceRegionId == TargetRegionId;

    public PointD MapCanonicalGuidanceToScene(PointD point) =>
        MapLogicalToScene(point);

    public PointD MapSceneGuidanceToCanonical(PointD point) =>
        MapSceneToLogical(point);

    public PointD MapLogicalToScene(PointD point) =>
        SourceSpace == Canvas2DConnectorPresentationSourceSpace.ScopeLogical
            ? CoordinateMap.MapLogicalToScene(point)
            : _canonicalGuidanceToSceneTransform.TransformPoint(point);

    public PointD MapSceneToLogical(PointD point)
    {
        if (SourceSpace != Canvas2DConnectorPresentationSourceSpace.ScopeLogical)
            return _sceneToCanonicalGuidanceTransform.TransformPoint(point);
        // An unchanged authored handle must recover its original saved double values, without
        // introducing an inverse-interpolation rounding edit or changing routing priority.
        var vertex = DisplayedEditablePath.IndexOf(point);
        return vertex >= 0 ? CanonicalEditablePath[vertex] : CoordinateMap.MapSceneToLogical(point);
    }

    public bool TryGetCanonicalGuidanceToSceneTransform(out Matrix2D transform)
    {
        if (SourceSpace == Canvas2DConnectorPresentationSourceSpace.ScopeLogical)
        {
            return CoordinateMap.TryGetTranslation(out transform);
        }
        transform = _canonicalGuidanceToSceneTransform;
        return true;
    }

    /// <summary>
    /// Maps an edited internal displayed point back to canonical route guidance. Connector
    /// endpoints deliberately return false because endpoint identity is handled by reconnection.
    /// </summary>
    public bool TryMapDisplayedEditablePointToCanonical(
        int editableIndex,
        PointD displayedPoint,
        out PointD canonicalPoint)
    {
        if (editableIndex <= 0 || editableIndex >= DisplayedEditablePath.Length - 1)
        {
            canonicalPoint = default;
            return false;
        }

        canonicalPoint = MapSceneGuidanceToCanonical(displayedPoint);
        return true;
    }

    public bool Equals(Canvas2DConnectorPresentationMapping? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        SourceSpace == other.SourceSpace &&
        _canonicalGuidanceToSceneTransform == other._canonicalGuidanceToSceneTransform &&
        CoordinateMap.Equals(other.CoordinateMap) &&
        SourceRegionId == other.SourceRegionId &&
        TargetRegionId == other.TargetRegionId &&
        CanonicalEditablePath.AsSpan().SequenceEqual(other.CanonicalEditablePath.AsSpan()) &&
        DisplayedEditablePath.AsSpan().SequenceEqual(other.DisplayedEditablePath.AsSpan());

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DConnectorPresentationMapping);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceSpace);
        hash.Add(_canonicalGuidanceToSceneTransform);
        hash.Add(CoordinateMap);
        hash.Add(SourceRegionId);
        hash.Add(TargetRegionId);
        foreach (var point in CanonicalEditablePath)
        {
            hash.Add(point);
        }

        foreach (var point in DisplayedEditablePath)
        {
            hash.Add(point);
        }

        return hash.ToHashCode();
    }

    private static bool IsTranslation(Matrix2D transform) =>
        transform.M11 == 1d &&
        transform.M12 == 0d &&
        transform.M21 == 0d &&
        transform.M22 == 1d;
}
