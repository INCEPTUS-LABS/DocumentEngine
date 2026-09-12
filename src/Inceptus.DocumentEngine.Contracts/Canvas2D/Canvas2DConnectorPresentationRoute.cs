using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// One derived connector path and its reversible canonical-guidance mapping.
/// </summary>
public sealed class Canvas2DConnectorPresentationRoute :
    IEquatable<Canvas2DConnectorPresentationRoute>
{
    public Canvas2DConnectorPresentationRoute(
        IEnumerable<PointD> displayedLogicalPath,
        Canvas2DConnectorPresentationMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(displayedLogicalPath);
        ArgumentNullException.ThrowIfNull(mapping);
        DisplayedLogicalPath = displayedLogicalPath.ToImmutableArray();
        if (DisplayedLogicalPath.Length < 2)
        {
            throw new ArgumentException(
                "A displayed connector route requires at least two points.",
                nameof(displayedLogicalPath));
        }

        Mapping = mapping;
    }

    public ImmutableArray<PointD> DisplayedLogicalPath { get; }

    public Canvas2DConnectorPresentationMapping Mapping { get; }

    public bool Equals(Canvas2DConnectorPresentationRoute? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DisplayedLogicalPath.AsSpan().SequenceEqual(other.DisplayedLogicalPath.AsSpan()) &&
        Mapping.Equals(other.Mapping);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DConnectorPresentationRoute);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in DisplayedLogicalPath)
        {
            hash.Add(point);
        }

        hash.Add(Mapping);
        return hash.ToHashCode();
    }

}
