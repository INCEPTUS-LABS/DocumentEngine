using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Preserves the exact reversible relationship between displayed editable connector guidance
/// and canonical Process-local guidance. Displayed endpoints may use different visual regions;
/// internal editable points always use the one canonical-guidance transform.
/// </summary>
public sealed class Canvas2DConnectorPresentationMapping :
    IEquatable<Canvas2DConnectorPresentationMapping>
{
    private readonly Matrix2D _sceneToCanonicalGuidanceTransform;

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

        CanonicalGuidanceToSceneTransform = canonicalGuidanceToSceneTransform;
        SourceRegionId = sourceRegionId;
        TargetRegionId = targetRegionId;
    }

    public ImmutableArray<PointD> CanonicalEditablePath { get; }

    public ImmutableArray<PointD> DisplayedEditablePath { get; }

    public Matrix2D CanonicalGuidanceToSceneTransform { get; }

    public Canvas2DSpatialRegionId? SourceRegionId { get; }

    public Canvas2DSpatialRegionId? TargetRegionId { get; }

    public bool IsSameRegion =>
        SourceRegionId is not null && SourceRegionId == TargetRegionId;

    public PointD MapCanonicalGuidanceToScene(PointD point) =>
        CanonicalGuidanceToSceneTransform.TransformPoint(point);

    public PointD MapSceneGuidanceToCanonical(PointD point) =>
        _sceneToCanonicalGuidanceTransform.TransformPoint(point);

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
        CanonicalGuidanceToSceneTransform == other.CanonicalGuidanceToSceneTransform &&
        SourceRegionId == other.SourceRegionId &&
        TargetRegionId == other.TargetRegionId &&
        CanonicalEditablePath.AsSpan().SequenceEqual(other.CanonicalEditablePath.AsSpan()) &&
        DisplayedEditablePath.AsSpan().SequenceEqual(other.DisplayedEditablePath.AsSpan());

    public override bool Equals(object? obj) =>
        Equals(obj as Canvas2DConnectorPresentationMapping);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(CanonicalGuidanceToSceneTransform);
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
